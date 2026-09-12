using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;

namespace Aurora.Architecture.Tests.Metadata;

/// <summary>
/// Decodes a metadata signature blob into a <see cref="TypeUse"/>: a readable rendering plus the
/// flattened set of every named type the signature mentions.
/// </summary>
internal sealed class TypeUseProvider : ISignatureTypeProvider<TypeUse, object?>
{
    public static readonly TypeUseProvider Instance = new();

    private TypeUseProvider()
    {
    }

    public TypeUse GetPrimitiveType(PrimitiveTypeCode typeCode) => TypeUse.Named(typeCode switch
    {
        PrimitiveTypeCode.Boolean => "System.Boolean",
        PrimitiveTypeCode.Byte => "System.Byte",
        PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.Int16 => "System.Int16",
        PrimitiveTypeCode.Int32 => "System.Int32",
        PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Single => "System.Single",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.TypedReference => "System.TypedReference",
        PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.UInt32 => "System.UInt32",
        PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.Void => "System.Void",
        _ => typeCode.ToString(),
    });

    public TypeUse GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
        TypeUse.Named(MetadataNames.Of(reader, handle));

    public TypeUse GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
        TypeUse.Named(MetadataNames.Of(reader, handle));

    public TypeUse GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public TypeUse GetSZArrayType(TypeUse elementType) =>
        TypeUse.Combine(elementType.Display + "[]", [elementType]);

    public TypeUse GetArrayType(TypeUse elementType, ArrayShape shape) =>
        TypeUse.Combine(
            elementType.Display + "[" + new string(',', shape.Rank - 1) + "]",
            [elementType]);

    public TypeUse GetByReferenceType(TypeUse elementType) =>
        TypeUse.Combine(elementType.Display + "&", [elementType]);

    public TypeUse GetPointerType(TypeUse elementType) =>
        TypeUse.Combine(elementType.Display + "*", [elementType]);

    public TypeUse GetPinnedType(TypeUse elementType) => elementType;

    public TypeUse GetGenericInstantiation(TypeUse genericType, ImmutableArray<TypeUse> typeArguments) =>
        TypeUse.Combine(
            genericType.Display + "<" + string.Join(", ", typeArguments.Select(static a => a.Display)) + ">",
            [genericType, .. typeArguments]);

    // A modopt/modreq wrapper contributes its own name too: `volatile int` carries
    // System.Runtime.CompilerServices.IsVolatile, and a rule banning a type should see it
    // wherever it is mentioned.
    public TypeUse GetModifiedType(TypeUse modifier, TypeUse unmodifiedType, bool isRequired) =>
        TypeUse.Combine(unmodifiedType.Display, [unmodifiedType, modifier]);

    public TypeUse GetFunctionPointerType(MethodSignature<TypeUse> signature) =>
        TypeUse.Combine(
            "delegate*<" + string.Join(
                ", ",
                signature.ParameterTypes.Select(static p => p.Display).Append(signature.ReturnType.Display)) + ">",
            [signature.ReturnType, .. signature.ParameterTypes]);

    // An open generic parameter names no concrete type, so it contributes no name to match on.
    // `!0` cannot be double; `List<!0>` closed over double is decoded at the use site, where the
    // instantiation above does contribute System.Double.
    public TypeUse GetGenericTypeParameter(object? genericContext, int index) =>
        TypeUse.Unnamed("!" + index.ToString(CultureInfo.InvariantCulture));

    public TypeUse GetGenericMethodParameter(object? genericContext, int index) =>
        TypeUse.Unnamed("!!" + index.ToString(CultureInfo.InvariantCulture));
}
