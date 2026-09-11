using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Aurora.Architecture.Tests.Metadata;

/// <summary>
/// Reads a compiled assembly's metadata and IL straight out of its PE file.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here loads or executes the assembly, so an assembly whose dependencies are absent from
/// this test process still scans, and a rule can be pointed at any <c>.dll</c> on disk.
/// </para>
/// <para>
/// Why not reflection: <c>docs/reviews/B-03.md</c> m-1 proved that reflecting over fields,
/// properties, parameters and return types cannot see a <c>double</c> local or a <c>(double)</c>
/// cast inside a method body, which is exactly the violation the floating-point rule exists to
/// stop. IL is the level at which that violation is visible and cannot be hidden.
/// </para>
/// </remarks>
internal static class AssemblyScanner
{
    private const string CompilerGeneratedAttribute = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";

    private static readonly Dictionary<int, OpCode> OpCodeTable = BuildOpCodeTable();

    public static ScannedAssembly Read(string assemblyPath)
    {
        byte[] bytes = File.ReadAllBytes(assemblyPath);
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();

        return new ScannedAssembly(
            Name: reader.GetString(reader.GetAssemblyDefinition().Name),
            Path: assemblyPath,
            ReferencedAssemblies:
            [
                .. reader.AssemblyReferences
                    .Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
                    .OrderBy(static name => name, StringComparer.Ordinal),
            ],
            Types:
            [
                .. reader.TypeDefinitions
                    .Select(handle => ReadType(peReader, reader, handle))
                    .Where(static type => type.FullName != "<Module>"),
            ]);
    }

    private static ScannedType ReadType(PEReader peReader, MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition definition = reader.GetTypeDefinition(handle);

        return new ScannedType(
            FullName: MetadataNames.Of(reader, handle),
            AssemblyName: reader.GetString(reader.GetAssemblyDefinition().Name),
            Attributes: definition.Attributes,
            BaseTypeName: NameOfTypeHandle(reader, definition.BaseType),
            InterfaceNames:
            [
                .. definition.GetInterfaceImplementations()
                    .Select(i => NameOfTypeHandle(reader, reader.GetInterfaceImplementation(i).Interface))
                    .Where(static name => name is not null)
                    .Select(static name => name!),
            ],
            Fields:
            [
                .. definition.GetFields().Select(f =>
                {
                    FieldDefinition field = reader.GetFieldDefinition(f);
                    return new ScannedField(
                        reader.GetString(field.Name),
                        field.DecodeSignature(TypeUseProvider.Instance, null),
                        field.Attributes);
                }),
            ],
            Properties:
            [
                .. definition.GetProperties().Select(p =>
                {
                    PropertyDefinition property = reader.GetPropertyDefinition(p);
                    MethodSignature<TypeUse> signature =
                        property.DecodeSignature(TypeUseProvider.Instance, null);
                    return new ScannedProperty(
                        reader.GetString(property.Name),
                        signature.ReturnType,
                        !signature.Header.IsInstance);
                }),
            ],
            Methods: [.. definition.GetMethods().Select(m => ReadMethod(peReader, reader, m))],
            IsCompilerGenerated: HasCompilerGeneratedAttribute(reader, definition.GetCustomAttributes()));
    }

    private static ScannedMethod ReadMethod(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinitionHandle handle)
    {
        MethodDefinition method = reader.GetMethodDefinition(handle);
        MethodSignature<TypeUse> signature = method.DecodeSignature(TypeUseProvider.Instance, null);

        return new ScannedMethod(
            Name: reader.GetString(method.Name),
            Attributes: method.Attributes,
            ReturnType: signature.ReturnType,
            Parameters: signature.ParameterTypes,
            Body: method.RelativeVirtualAddress == 0
                ? ScannedMethodBody.Absent
                : ReadBody(peReader, reader, method.RelativeVirtualAddress));
    }

    private static ScannedMethodBody ReadBody(PEReader peReader, MetadataReader reader, int relativeVirtualAddress)
    {
        MethodBodyBlock block = peReader.GetMethodBody(relativeVirtualAddress);

        ImmutableArray<TypeUse> locals = block.LocalSignature.IsNil
            ? []
            : reader.GetStandaloneSignature(block.LocalSignature)
                .DecodeLocalSignature(TypeUseProvider.Instance, null);

        var opCodes = ImmutableArray.CreateBuilder<string>();
        var members = ImmutableArray.CreateBuilder<MemberUse>();
        WalkIl(reader, block.GetILContent(), opCodes, members);

        return new ScannedMethodBody(locals, opCodes.ToImmutable(), members.ToImmutable());
    }

    /// <summary>
    /// Walks a method body instruction by instruction, recording every opcode and resolving every
    /// metadata token an instruction names.
    /// </summary>
    /// <remarks>
    /// An instruction this walker cannot decode throws rather than ending the walk: a scanner that
    /// silently stops half way through a body reports "no violations found" for the half it never
    /// read, which is the vacuous-check failure mode this whole project exists to prevent.
    /// </remarks>
    private static void WalkIl(
        MetadataReader reader,
        ImmutableArray<byte> il,
        ImmutableArray<string>.Builder opCodes,
        ImmutableArray<MemberUse>.Builder members)
    {
        int offset = 0;
        while (offset < il.Length)
        {
            int start = offset;
            int key = il[offset++];
            if (key == 0xFE)
            {
                if (offset >= il.Length)
                {
                    throw new BadImageFormatException(
                        FormattableString.Invariant($"IL ends inside a two-byte opcode at offset {start}."));
                }

                key = 0xFE00 | il[offset++];
            }

            if (!OpCodeTable.TryGetValue(key, out OpCode opCode))
            {
                throw new BadImageFormatException(
                    FormattableString.Invariant($"Unknown IL opcode 0x{key:X} at offset {start}."));
            }

            opCodes.Add(opCode.Name ?? key.ToString("X", CultureInfo.InvariantCulture));

            int operandSize = OperandSize(opCode.OperandType, il, offset);
            if (IsTokenOperand(opCode.OperandType))
            {
                int token = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset, 4));
                MemberUse? use = ResolveToken(reader, opCode.Name ?? string.Empty, token);
                if (use is not null)
                {
                    members.Add(use);
                }
            }

            offset += operandSize;
        }
    }

    private static bool IsTokenOperand(OperandType operandType) => operandType
        is OperandType.InlineField
        or OperandType.InlineMethod
        or OperandType.InlineSig
        or OperandType.InlineTok
        or OperandType.InlineType;

    private static int OperandSize(OperandType operandType, ImmutableArray<byte> il, int offset) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget
            or OperandType.InlineField
            or OperandType.InlineI
            or OperandType.InlineMethod
            or OperandType.InlineSig
            or OperandType.InlineString
            or OperandType.InlineTok
            or OperandType.InlineType
            or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset, 4))),
        _ => throw new BadImageFormatException(
            FormattableString.Invariant($"Unsupported IL operand type {operandType}.")),
    };

    private static MemberUse? ResolveToken(MetadataReader reader, string opCode, int token)
    {
        EntityHandle handle = MetadataTokens.EntityHandle(token);
        if (handle.IsNil)
        {
            return null;
        }

        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                MethodSignature<TypeUse> signature = method.DecodeSignature(TypeUseProvider.Instance, null);
                return new MemberUse(
                    opCode,
                    MetadataNames.Of(reader, method.GetDeclaringType()),
                    reader.GetString(method.Name),
                    Flatten(signature),
                    []);
            }

            case HandleKind.FieldDefinition:
            {
                FieldDefinition field = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                return new MemberUse(
                    opCode,
                    MetadataNames.Of(reader, field.GetDeclaringType()),
                    reader.GetString(field.Name),
                    field.DecodeSignature(TypeUseProvider.Instance, null),
                    []);
            }

            case HandleKind.MemberReference:
                return ResolveMemberReference(reader, opCode, (MemberReferenceHandle)handle, []);

            case HandleKind.MethodSpecification:
            {
                MethodSpecification specification = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                ImmutableArray<TypeUse> arguments =
                    specification.DecodeSignature(TypeUseProvider.Instance, null);

                if (specification.Method.Kind == HandleKind.MemberReference)
                {
                    return ResolveMemberReference(
                        reader,
                        opCode,
                        (MemberReferenceHandle)specification.Method,
                        arguments);
                }

                MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)specification.Method);
                MethodSignature<TypeUse> signature = method.DecodeSignature(TypeUseProvider.Instance, null);
                return new MemberUse(
                    opCode,
                    MetadataNames.Of(reader, method.GetDeclaringType()),
                    reader.GetString(method.Name),
                    Flatten(signature),
                    arguments);
            }

            case HandleKind.TypeDefinition:
            case HandleKind.TypeReference:
            case HandleKind.TypeSpecification:
            {
                string? name = NameOfTypeHandle(reader, handle);
                return name is null ? null : new MemberUse(opCode, name, string.Empty, TypeUse.Named(name), []);
            }

            case HandleKind.StandaloneSignature:
            {
                StandaloneSignature signature = reader.GetStandaloneSignature((StandaloneSignatureHandle)handle);
                return new MemberUse(
                    opCode,
                    "<calli>",
                    string.Empty,
                    Flatten(signature.DecodeMethodSignature(TypeUseProvider.Instance, null)),
                    []);
            }

            default:
                return null;
        }
    }

    private static MemberUse ResolveMemberReference(
        MetadataReader reader,
        string opCode,
        MemberReferenceHandle handle,
        ImmutableArray<TypeUse> genericArguments)
    {
        MemberReference reference = reader.GetMemberReference(handle);
        string declaringType = NameOfTypeHandle(reader, reference.Parent) ?? "<unknown>";
        TypeUse signature = reference.GetKind() == MemberReferenceKind.Field
            ? reference.DecodeFieldSignature(TypeUseProvider.Instance, null)
            : Flatten(reference.DecodeMethodSignature(TypeUseProvider.Instance, null));

        return new MemberUse(
            opCode,
            declaringType,
            reader.GetString(reference.Name),
            signature,
            genericArguments);
    }

    private static TypeUse Flatten(MethodSignature<TypeUse> signature) =>
        TypeUse.Combine(
            signature.ReturnType.Display
                + "(" + string.Join(", ", signature.ParameterTypes.Select(static p => p.Display)) + ")",
            [signature.ReturnType, .. signature.ParameterTypes]);

    private static string? NameOfTypeHandle(MetadataReader reader, EntityHandle handle)
    {
        if (handle.IsNil)
        {
            return null;
        }

        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return MetadataNames.Of(reader, (TypeDefinitionHandle)handle);

            case HandleKind.TypeReference:
                return MetadataNames.Of(reader, (TypeReferenceHandle)handle);

            case HandleKind.TypeSpecification:
            {
                // A constructed type renders as `Ns.Base`1<System.Int32>`; the name a base-type or
                // interface rule matches on is the open type, so the instantiation is trimmed off.
                string display = reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                    .DecodeSignature(TypeUseProvider.Instance, null)
                    .Display;
                int argumentList = display.IndexOf('<', StringComparison.Ordinal);
                return argumentList < 0 ? display : display[..argumentList];
            }

            case HandleKind.MethodDefinition:
            {
                MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                return MetadataNames.Of(reader, method.GetDeclaringType());
            }

            default:
                return null;
        }
    }

    private static bool HasCompilerGeneratedAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes)
    {
        foreach (CustomAttributeHandle handle in attributes)
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            if (NameOfTypeHandle(reader, attribute.Constructor) == CompilerGeneratedAttribute)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The IL opcode table, read from <see cref="OpCodes"/> rather than hand-written.
    /// </summary>
    /// <remarks>
    /// A hand-written table with one wrong operand length desynchronises the walk and turns the
    /// rest of every affected method body into noise - silently. The BCL already knows every
    /// opcode's operand shape, so the table is derived from it.
    /// </remarks>
    private static Dictionary<int, OpCode> BuildOpCodeTable()
    {
        var table = new Dictionary<int, OpCode>();

        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpCode) || field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            table[unchecked((ushort)opCode.Value)] = opCode;
        }

        return table;
    }
}
