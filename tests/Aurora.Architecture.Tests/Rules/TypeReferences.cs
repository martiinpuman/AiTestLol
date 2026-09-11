using System.Collections.Generic;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>One place a type is named, with the member that names it and the site it appears at.</summary>
internal readonly record struct TypeReference(string Subject, ViolationSite Site, TypeUse Use, string Context);

/// <summary>One member or type an IL instruction names, with the method that names it.</summary>
internal readonly record struct InstructionReference(string Subject, MemberUse Member);

/// <summary>
/// Every place a <see cref="ScannedType"/> names another type, and every member its instructions
/// call.
/// </summary>
/// <remarks>
/// One enumeration shared by every "this type must not appear" rule, so each such rule has the
/// same reach: signatures <b>and</b> bodies. A rule built on its own narrower walk is how a rule
/// ends up claiming more than it checks (<c>docs/reviews/B-03.md</c> m-1).
/// </remarks>
internal static class TypeReferences
{
    public static IEnumerable<TypeReference> In(ScannedType type)
    {
        if (type.BaseTypeName is not null)
        {
            yield return new TypeReference(
                type.FullName,
                ViolationSite.TypeShape,
                TypeUse.Named(type.BaseTypeName),
                "base type");
        }

        foreach (string interfaceName in type.InterfaceNames)
        {
            yield return new TypeReference(
                type.FullName,
                ViolationSite.TypeShape,
                TypeUse.Named(interfaceName),
                "implemented interface");
        }

        foreach (ScannedField field in type.Fields)
        {
            yield return new TypeReference(
                $"{type.FullName}.{field.Name}",
                ViolationSite.Field,
                field.Type,
                field.IsStatic ? "static field" : "field");
        }

        foreach (ScannedProperty property in type.Properties)
        {
            yield return new TypeReference(
                $"{type.FullName}.{property.Name}",
                ViolationSite.Property,
                property.Type,
                property.IsStatic ? "static property" : "property");
        }

        foreach (ScannedMethod method in type.Methods)
        {
            string subject = $"{type.FullName}.{method.Name}";

            yield return new TypeReference(subject, ViolationSite.Signature, method.ReturnType, "return type");

            foreach (TypeUse parameter in method.Parameters)
            {
                yield return new TypeReference(subject, ViolationSite.Signature, parameter, "parameter");
            }

            foreach (TypeUse local in method.Body.Locals)
            {
                yield return new TypeReference(subject, ViolationSite.Local, local, "local variable");
            }

            foreach (MemberUse member in method.Body.MemberReferences)
            {
                yield return new TypeReference(
                    subject,
                    ViolationSite.MemberReference,
                    TypeUse.Named(member.DeclaringType),
                    $"{member.OpCode} {member}");

                yield return new TypeReference(
                    subject,
                    ViolationSite.MemberReference,
                    member.Signature,
                    $"signature of {member.OpCode} {member}");

                foreach (TypeUse argument in member.GenericArguments)
                {
                    yield return new TypeReference(
                        subject,
                        ViolationSite.MemberReference,
                        argument,
                        $"generic argument of {member.OpCode} {member}");
                }
            }
        }
    }

    /// <summary>Every member an instruction in this type names, with the method that names it.</summary>
    public static IEnumerable<InstructionReference> InstructionsIn(ScannedType type)
    {
        foreach (ScannedMethod method in type.Methods)
        {
            foreach (MemberUse member in method.Body.MemberReferences)
            {
                yield return new InstructionReference($"{type.FullName}.{method.Name}", member);
            }
        }
    }
}
