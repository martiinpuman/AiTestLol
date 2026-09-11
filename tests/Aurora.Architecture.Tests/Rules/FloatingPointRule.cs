using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rule <b>F1</b> - no floating-point type is mentioned in a member signature, a local
/// variable, a floating-point IL instruction, or a member an instruction calls.
/// </summary>
/// <remarks>
/// <para>
/// <b>Exactly what the mechanism inspects,</b> so the name claims nothing more (the rule
/// <c>docs/reviews/B-03.md</c> m-1 broke):
/// </para>
/// <list type="bullet">
///   <item><description>every field and property type, including private ones;</description></item>
///   <item><description>every method and constructor return type and parameter type;</description></item>
///   <item><description>every <b>local variable</b> declared in a method body;</description></item>
///   <item><description>
///     every floating-point <b>IL instruction</b>: a <c>float64</c> constant (<c>ldc.r8</c>), a
///     conversion to or from floating point (<c>conv.r8</c>, <c>conv.r4</c>, <c>conv.r.un</c>),
///     and the floating-point array and indirect loads and stores;
///   </description></item>
///   <item><description>
///     every <b>member an instruction names</b> whose signature mentions a floating-point type -
///     which is how <c>(double)someDecimal</c> is caught even when the compiler optimises the
///     local away, because the cast is a call to <c>System.Decimal::op_Explicit</c> returning
///     <c>float64</c>.
///   </description></item>
/// </list>
/// <para>
/// <b>What it cannot see,</b> written down because an unstated limit is a false claim: a floating
/// point value that reaches this code as <c>object</c> and is only ever unboxed by a called
/// method; floating-point arithmetic performed inside an assembly that is not part of the scanned
/// population; and a value produced by a method whose own signature is <c>decimal</c> but whose
/// body - in another assembly - used <c>double</c>. The first is not expressible without
/// data-flow analysis; the second and third are covered by scanning that assembly too, which is
/// why the population is every project under <c>src/</c> rather than a chosen subset.
/// </para>
/// </remarks>
internal static class FloatingPointRule
{
    public const string Id = "F1";

    public const string Name =
        "No floating-point type in a signature, local, instruction or called member";

    /// <summary>The banned types. <see cref="Half"/> is included: it is floating point with fewer digits, not more.</summary>
    public static readonly ImmutableHashSet<string> BannedTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.Double",
        "System.Single",
        "System.Half");

    /// <summary>
    /// Every IL instruction that can only exist because floating point is in play.
    /// </summary>
    /// <remarks>
    /// Arithmetic opcodes (<c>mul</c>, <c>div</c>) are deliberately absent: they are the same
    /// opcode for every numeric type, so banning them would ban <c>decimal</c> arithmetic too. The
    /// constants and the conversions are the instructions that are floating-point by definition,
    /// and no floating-point value can enter a method without one of them or without a member
    /// reference whose signature names the type.
    /// </remarks>
    public static readonly ImmutableHashSet<string> BannedOpCodes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ldc.r4",
        "ldc.r8",
        "conv.r4",
        "conv.r8",
        "conv.r.un",
        "ldind.r4",
        "ldind.r8",
        "stind.r4",
        "stind.r8",
        "ldelem.r4",
        "ldelem.r8",
        "stelem.r4",
        "stelem.r8");

    /// <summary>
    /// Assemblies exempt from this rule. Empty, and asserted empty by a test, so that granting an
    /// exemption is a visible diff in this file rather than a quiet local decision.
    /// </summary>
    public static readonly ImmutableHashSet<string> ExemptAssemblies =
        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);

    public static RuleOutcome Check(IEnumerable<ScannedType> types)
    {
        ScannedType[] subjects = [.. types.Where(static type => !ExemptAssemblies.Contains(type.AssemblyName))];

        return RuleOutcome.From(
            Id,
            Name,
            "types",
            subjects.Length,
            subjects.SelectMany(ViolationsIn));
    }

    private static IEnumerable<RuleViolation> ViolationsIn(ScannedType type)
    {
        foreach (ScannedField field in type.Fields.Where(static f => f.Type.MentionsAny(BannedTypes)))
        {
            yield return new RuleViolation(
                $"{type.FullName}.{field.Name}",
                ViolationSite.Field,
                $"field of type {field.Type.Display}");
        }

        foreach (ScannedProperty property in type.Properties.Where(static p => p.Type.MentionsAny(BannedTypes)))
        {
            yield return new RuleViolation(
                $"{type.FullName}.{property.Name}",
                ViolationSite.Property,
                $"property of type {property.Type.Display}");
        }

        foreach (ScannedMethod method in type.Methods)
        {
            foreach (RuleViolation violation in ViolationsIn(type, method))
            {
                yield return violation;
            }
        }
    }

    private static IEnumerable<RuleViolation> ViolationsIn(ScannedType type, ScannedMethod method)
    {
        string subject = $"{type.FullName}.{method.Name}";

        if (method.ReturnType.MentionsAny(BannedTypes))
        {
            yield return new RuleViolation(subject, ViolationSite.Signature, $"returns {method.ReturnType.Display}");
        }

        foreach (TypeUse parameter in method.Parameters.Where(static p => p.MentionsAny(BannedTypes)))
        {
            yield return new RuleViolation(subject, ViolationSite.Signature, $"parameter of type {parameter.Display}");
        }

        foreach (TypeUse local in method.Body.Locals.Where(static l => l.MentionsAny(BannedTypes)))
        {
            yield return new RuleViolation(subject, ViolationSite.Local, $"local of type {local.Display}");
        }

        foreach (string opCode in method.Body.OpCodes.Where(BannedOpCodes.Contains).Distinct(StringComparer.Ordinal))
        {
            yield return new RuleViolation(subject, ViolationSite.Instruction, $"floating-point instruction '{opCode}'");
        }

        foreach (MemberUse member in method.Body.MemberReferences)
        {
            if (member.Signature.MentionsAny(BannedTypes)
                || member.GenericArguments.Any(static argument => argument.MentionsAny(BannedTypes)))
            {
                yield return new RuleViolation(
                    subject,
                    ViolationSite.MemberReference,
                    $"{member.OpCode} {member} with signature {member.Signature.Display}");
            }
        }
    }
}
