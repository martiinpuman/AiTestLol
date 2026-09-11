using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Aurora.Architecture.Tests.Metadata;

/// <summary>A field, as metadata declares it.</summary>
internal sealed record ScannedField(string Name, TypeUse Type, FieldAttributes Attributes)
{
    public bool IsStatic => (Attributes & FieldAttributes.Static) != 0;
}

/// <summary>A property, as metadata declares it. The type is the property's own type.</summary>
internal sealed record ScannedProperty(string Name, TypeUse Type, bool IsStatic);

/// <summary>
/// One member or type named by an IL instruction: the target of a <c>call</c>, the field of a
/// <c>ldfld</c>, the type of a <c>newobj</c>, the argument of a <c>ldtoken</c>.
/// </summary>
internal sealed record MemberUse(
    string OpCode,
    string DeclaringType,
    string MemberName,
    TypeUse Signature,
    ImmutableArray<TypeUse> GenericArguments)
{
    public override string ToString() =>
        MemberName.Length == 0 ? DeclaringType : DeclaringType + "::" + MemberName;
}

/// <summary>
/// What a method body contains, at the only level of detail that cannot be dodged: its local
/// variable types, the opcodes it executes and every member and type its instructions name.
/// </summary>
/// <remarks>
/// This is the part <c>B-03</c>'s review proved a signature-only rule cannot see
/// (<c>docs/reviews/B-03.md</c> m-1): a <c>double</c> local and a <c>(double)</c> cast live here
/// and nowhere else.
/// </remarks>
internal sealed record ScannedMethodBody(
    ImmutableArray<TypeUse> Locals,
    ImmutableArray<string> OpCodes,
    ImmutableArray<MemberUse> MemberReferences)
{
    public static readonly ScannedMethodBody Absent = new([], [], []);
}

/// <summary>A method or constructor, as metadata declares it, together with its body.</summary>
internal sealed record ScannedMethod(
    string Name,
    MethodAttributes Attributes,
    TypeUse ReturnType,
    ImmutableArray<TypeUse> Parameters,
    ScannedMethodBody Body)
{
    public bool IsConstructor => Name is ".ctor";

    public MethodAttributes Accessibility => Attributes & MethodAttributes.MemberAccessMask;

    /// <summary>Reachable from outside the declaring assembly: <c>public</c>, or <c>protected</c> on an unsealed type.</summary>
    public bool IsPublicOrProtected =>
        Accessibility is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;
}

/// <summary>A type, as metadata declares it.</summary>
internal sealed record ScannedType(
    string FullName,
    string AssemblyName,
    TypeAttributes Attributes,
    string? BaseTypeName,
    ImmutableArray<string> InterfaceNames,
    ImmutableArray<ScannedField> Fields,
    ImmutableArray<ScannedProperty> Properties,
    ImmutableArray<ScannedMethod> Methods,
    bool IsCompilerGenerated)
{
    public string Namespace
    {
        get
        {
            int lastDot = FullName.LastIndexOf('.');
            return lastDot < 0 ? string.Empty : FullName[..lastDot];
        }
    }

    /// <summary>Every type this type mentions in a member signature, a local or an instruction.</summary>
    public ImmutableArray<TypeUse> AllTypeUses =>
    [
        .. Fields.Select(static f => f.Type),
        .. Properties.Select(static p => p.Type),
        .. Methods.SelectMany(static m =>
            new[] { m.ReturnType }
                .Concat(m.Parameters)
                .Concat(m.Body.Locals)
                .Concat(m.Body.MemberReferences.Select(static r => r.Signature))
                .Concat(m.Body.MemberReferences.SelectMany(static r => r.GenericArguments))),
    ];
}

/// <summary>One compiled assembly, read from its PE file without loading or executing it.</summary>
internal sealed record ScannedAssembly(
    string Name,
    string Path,
    ImmutableArray<string> ReferencedAssemblies,
    ImmutableArray<ScannedType> Types)
{
    public override string ToString() => Name;
}
