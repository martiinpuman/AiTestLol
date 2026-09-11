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
/// <b>What the mechanism inspects:</b> every type mention in every production assembly whose exact
/// name is not on the allow-list, at every site <see cref="TypeReferences"/> reaches - so naming
/// it in a signature, a local or an instruction all count, which is what "in any signature or body"
/// in ADR-0027 §1 means. The handle is matched by simple name, like the rest of the tenancy family,
/// because B-06 has not chosen the namespace yet.
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
    /// The assemblies allowed to name it, by exact name (ADR-0032 §4.4). ADR-0027 §1 names
    /// "Aurora.Platform.Tenancy, the provisioning/migration runner and their test assemblies".
    /// </summary>
    /// <remarks>
    /// Exact names, not prefixes: a prefix let <c>Aurora.Platform.TenancyBypass</c> authorise itself
    /// by its name (re-review m-2). Nothing is pre-entered - not B-07's saga, not B-08's runner, not
    /// the contracts assembly ADR-0027 §1 declares the handle in - because a guessed name is an entry
    /// nobody can verify. <b>The task that makes an assembly name the handle adds its exact name here
    /// in its own diff</b>; T6 fires on it until someone does, which is the reviewable event an
    /// allow-list exists to force, and T14 fails if an entry names an assembly that does not exist.
    /// Test assemblies need no entry: the population is the projects under <c>src/</c>.
    /// </remarks>
    public static readonly ImmutableHashSet<string> AllowedAssemblies =
        ImmutableHashSet.Create(StringComparer.Ordinal, TenancyNames.TenancyAssemblyName);

    public static bool IsAllowed(string assemblyName) => AllowedAssemblies.Contains(assemblyName);

    public static RuleOutcome Check(IEnumerable<ScannedType> types) => Check(types, AllowedAssemblies);

    public static RuleOutcome Check(IEnumerable<ScannedType> types, ImmutableHashSet<string> allowedAssemblies)
    {
        ScannedType[] subjects = [.. types.Where(type => !allowedAssemblies.Contains(type.AssemblyName))];

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
