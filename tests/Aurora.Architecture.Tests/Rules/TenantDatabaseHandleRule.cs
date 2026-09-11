using System;
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
/// <b>What the mechanism inspects:</b> every type mention in every production assembly not on the
/// allow-list, at every site <see cref="TypeReferences"/> reaches - so naming it in a signature, a
/// local or an instruction all count, which is what "in any signature or body" in ADR-0027 §1 means.
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
    /// Prefixes, not exact names, because B-07's saga and B-08's runner are scheduled into the
    /// Platform/Tenancy tree (<c>docs/BACKLOG.md</c>) but have no project yet, and because the
    /// contracts assembly ADR-0027 §1 declares the handle in necessarily names it. <b>If either
    /// runner lands under a different assembly name, add it here rather than widening the rule</b> -
    /// the point of an allow-list is that extending it is a reviewable diff. Test assemblies need no
    /// entry: the population is the projects under <c>src/</c>.
    /// </para>
    /// <para>
    /// <b>Known and open (re-review m-2):</b> a prefix lets an assembly named
    /// <c>Aurora.Platform.TenancyBypass</c> authorise itself. ADR-0032 §4.4 replaces prefixes with
    /// exact names, but its first draft omitted the contracts assembly and the ADR is being revised;
    /// this list is left as it was until it settles, and README §6 records the gap.
    /// </para>
    /// </remarks>
    public static readonly ImmutableArray<string> AllowedAssemblyPrefixes = [TenancyNames.TenancyAssemblyPrefix];

    public static bool IsAllowed(string assemblyName) =>
        AllowedAssemblyPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.Ordinal));

    public static RuleOutcome Check(IEnumerable<ScannedType> types) => Check(types, AllowedAssemblyPrefixes);

    public static RuleOutcome Check(IEnumerable<ScannedType> types, ImmutableArray<string> allowedPrefixes)
    {
        ScannedType[] subjects =
        [
            .. types.Where(type => !allowedPrefixes.Any(prefix =>
                type.AssemblyName.StartsWith(prefix, StringComparison.Ordinal))),
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
