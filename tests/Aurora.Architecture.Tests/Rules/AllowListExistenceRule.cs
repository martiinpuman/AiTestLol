using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rule <b>T14</b> - every allow-list entry names an assembly that exists in the
/// production population (ADR-0032 §4.4).
/// </summary>
/// <remarks>
/// <para>
/// An allow-list entry that names no assembly is not a harmless leftover. T3's and T6's exemptions
/// are then exemptions of nothing, and after a rename the list did not follow, the renamed assembly
/// is the one place the rule now forbids. The change that caused it - a rename, a move, a typo in a
/// new entry - is on some branch, and this rule is what fails on that branch.
/// </para>
/// <para>
/// <b>What the mechanism inspects:</b> every entry of every allow-list the tenancy rules carry,
/// against the set of assembly names in the population. The entries are gathered from the rules
/// themselves, so a new allow-listed rule that is not added to <see cref="Entries"/> is an omission
/// in one place, and the test that pins <see cref="Entries"/> is where it shows.
/// </para>
/// <para>
/// <b>State today:</b> the one allow-listed assembly, <c>Aurora.Platform.Tenancy</c>, arrives with
/// B-05, so over production this rule reports it missing. It is recorded as inert until then and
/// <c>RuleInventoryTests</c> holds the expiry; ADR-0032 §5 lists it as live because it was written
/// with B-05 in view.
/// </para>
/// </remarks>
internal static class AllowListExistenceRule
{
    public const string Id = "T14";

    public const string Name = "Every allow-list entry names an assembly that exists in the population";

    /// <summary>Every allow-list entry the tenancy rules carry, with the rule it belongs to.</summary>
    public static ImmutableArray<(string RuleId, string AssemblyName)> Entries =>
    [
        .. TenantDbContextFactoryRule.AllowedAssemblies
            .OrderBy(static name => name, StringComparer.Ordinal)
            .Select(static name => (TenantDbContextFactoryRule.Id, name)),
        .. TenantDatabaseHandleRule.AllowedAssemblies
            .OrderBy(static name => name, StringComparer.Ordinal)
            .Select(static name => (TenantDatabaseHandleRule.Id, name)),
    ];

    public static RuleOutcome Check(IEnumerable<string> assemblyNames)
    {
        ImmutableHashSet<string> present = assemblyNames.ToImmutableHashSet(StringComparer.Ordinal);

        return RuleOutcome.From(
            Id,
            Name,
            "allow-list entries",
            Entries.Length,
            from entry in Entries
            where !present.Contains(entry.AssemblyName)
            select new RuleViolation(
                $"{entry.RuleId} allow-list: {entry.AssemblyName}",
                ViolationSite.Population,
                $"names no assembly in the population, so {entry.RuleId}'s exemption exempts nothing; the "
                + "assembly it meant has been renamed, moved, misspelled or not created yet (ADR-0032 §4.4)"));
    }
}
