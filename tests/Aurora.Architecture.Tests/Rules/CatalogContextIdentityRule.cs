using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rule <b>T13</b> - the catalog exemption is one exact (full name, assembly) pair, and
/// nothing else carries its simple name (ADR-0032 §4.3).
/// </summary>
/// <remarks>
/// <para>
/// The catalog context is the one <c>DbContext</c> the tenancy rules do not govern, so the identity
/// of that exemption is load-bearing for T1, T2 and the inertness guard. Matched by simple name it
/// was a costume anyone could wear: a public-constructor context called <c>CatalogDbContext</c> in
/// any namespace escaped all three (re-review m-1). Matched by the exact pair, a look-alike is a
/// tenant context - and a moved or renamed catalog context exempts nothing. This rule is what makes
/// both of those loud instead of silent.
/// </para>
/// <para>
/// <b>What the mechanism inspects:</b> every production type whose base chain reaches
/// <c>DbContext</c>. Two violations: (a) a type carrying the simple name <c>CatalogDbContext</c>
/// that is not an exempt pair - wrong namespace, or the right full name in the wrong assembly -
/// which is a tenant context the exemption must not be mistaken for; (b) an exempt pair's
/// assembly being in the population while declaring no type at the pair's full name, which is what
/// a rename looks like from here. (b) needs the assembly to exist, so the rule is inert until B-05
/// and <c>RuleInventoryTests</c> holds the expiry.
/// </para>
/// </remarks>
internal static class CatalogContextIdentityRule
{
    public const string Id = "T13";

    public const string Name =
        "Catalog context exempt as one exact (full name, assembly) pair; nothing else carries its simple name";

    public static RuleOutcome Check(TypeIndex index)
    {
        ScannedType[] contexts = [.. index.All.Where(type => index.DerivesFrom(type, TenancyNames.DbContext))];

        return RuleOutcome.From(
            Id,
            Name,
            "types deriving from DbContext",
            contexts.Length,
            [.. LookAlikes(contexts), .. PairsMissingFromTheirOwnAssembly(index)]);
    }

    private static IEnumerable<RuleViolation> LookAlikes(IEnumerable<ScannedType> contexts) =>
        from context in contexts
        from pair in TenancyNames.NonTenantContexts
        where string.Equals(
                TypeIndex.SimpleNameOf(context.FullName),
                TypeIndex.SimpleNameOf(pair.FullName),
                StringComparison.Ordinal)
            && !pair.Matches(context)
        select new RuleViolation(
            context.FullName,
            ViolationSite.TypeShape,
            $"carries the simple name of the exempt {pair.FullName} but is declared in {context.AssemblyName}, "
            + $"not as the exact pair in {pair.AssemblyName}; it is a tenant context and the exemption does not "
            + "apply to it (ADR-0032 §4.3)");

    private static IEnumerable<RuleViolation> PairsMissingFromTheirOwnAssembly(TypeIndex index) =>
        from pair in TenancyNames.NonTenantContexts
        where index.All.Any(type => string.Equals(type.AssemblyName, pair.AssemblyName, StringComparison.Ordinal))
            && !index.All.Any(pair.Matches)
        select new RuleViolation(
            pair.AssemblyName,
            ViolationSite.Population,
            $"is in the population but declares no type at {pair.FullName}; the catalog context was renamed "
            + "or moved, and every tenancy rule's exemption now exempts nothing (ADR-0032 §4.3)");
}
