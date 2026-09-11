using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rule <b>T6</b> - <c>TenantDatabaseHandle</c> is named only by the assemblies on a
/// declared allow-list (ADR-0027 §1).
/// </summary>
/// <remarks>
/// <para>
/// ADR-0027 splits the tenant proof in two: <c>TenantScope</c> for the application path, which is
/// identity-checked, pooled and schema-version gated, and <c>TenantDatabaseHandle</c> for the DDL
/// path, which is none of those things because it exists to repair tenants the gate has locked out.
/// A handle is a direct <c>aurora_migrator</c> connection with no skew check: exactly the authority
/// a module must never hold. The ADR asks for "a named allow-list, asserted, with a deliberately
/// violating fixture", and this is it.
/// </para>
/// <para>
/// <b>What the mechanism inspects:</b> every type mention in every production assembly that is
/// neither an allow-listed assembly nor a dotted segment below one, at every site
/// <see cref="TypeReferences"/> reaches - so naming it in a signature, a local or an instruction
/// all count, which is what "in any signature or body" in ADR-0027 §1 means.
/// The handle is matched by simple name, like the rest of the tenancy family, because B-06 has not
/// chosen the namespace yet.
/// </para>
/// <para>
/// <b>What it cannot see:</b> a handle obtained and used entirely inside an allow-listed assembly on
/// a non-allow-listed caller's behalf. That is a design question about what the tenancy assembly
/// exposes, not something a reference rule can answer.
/// </para>
/// </remarks>
internal static class TenantDatabaseHandleRule
{
    public const string Id = "T6";

    public const string Name = "TenantDatabaseHandle named only by the assemblies on the allow-list";

    /// <summary>
    /// The assemblies allowed to name it. ADR-0027 §1 names "Aurora.Platform.Tenancy, the
    /// provisioning/migration runner and their test assemblies".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prefixes matched segment-bounded (<see cref="TenancyNames.IsWithin"/>): the named assembly
    /// or a dotted segment below it, so <c>Aurora.Platform.Tenancy.Contracts</c> - where ADR-0027 §1
    /// declares the handle, which therefore names it - is inside, and
    /// <c>Aurora.Platform.TenancyBypass</c>, the re-review's executed m-2 bypass, is not. B-07's saga
    /// and B-08's runner are scheduled into the Platform/Tenancy tree (<c>docs/BACKLOG.md</c>) but
    /// have no project yet; <b>if either lands under a different assembly name, add it here rather
    /// than widening the rule</b>. Test assemblies need no entry: the population is the projects
    /// under <c>src/</c>.
    /// </para>
    /// <para>
    /// <b>Interim, pending ADR-0032 §4.4, and not the decision it will settle.</b> The ADR replaces
    /// prefixes with exact assembly names; its first exact list omitted the contracts assembly and
    /// is being revised. The segment-bounded form is strictly better than a plain prefix under
    /// either outcome, but any <c>Aurora.Platform.Tenancy.Anything</c> still authorises itself, and
    /// only the exact set closes that. README §6 records it.
    /// </para>
    /// </remarks>
    public static readonly ImmutableArray<string> AllowedAssemblyPrefixes = [TenancyNames.TenancyAssemblyPrefix];

    public static bool IsAllowed(string assemblyName) =>
        AllowedAssemblyPrefixes.Any(prefix => TenancyNames.IsWithin(assemblyName, prefix));

    public static RuleOutcome Check(IEnumerable<ScannedType> types) => Check(types, AllowedAssemblyPrefixes);

    public static RuleOutcome Check(IEnumerable<ScannedType> types, ImmutableArray<string> allowedPrefixes)
    {
        ScannedType[] subjects =
        [
            .. types.Where(type => !allowedPrefixes.Any(prefix => TenancyNames.IsWithin(type.AssemblyName, prefix))),
        ];

        return RuleOutcome.From(
            Id,
            Name,
            "types outside the allow-listed assemblies",
            subjects.Length,
            subjects
                .SelectMany(TypeReferences.In)
                .Where(static reference => TenancyNames.MentionsTenantDatabaseHandle(reference.Use))
                .Select(static reference => new RuleViolation(
                    reference.Subject,
                    reference.Site,
                    $"names TenantDatabaseHandle as {reference.Context}; the DDL path is a direct "
                    + "aurora_migrator connection with no schema-version gate, and only the tenancy "
                    + "assembly and the provisioning/migration runner may hold one (ADR-0027 §1)")));
    }
}
