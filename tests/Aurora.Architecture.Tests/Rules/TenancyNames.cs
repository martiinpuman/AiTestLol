using System;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// How the tenancy rules recognise the types ADR-0007 governs, and why each is matched the way it
/// is.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two matching strategies, deliberately.</b> <c>DbContext</c> and <c>IHttpContextAccessor</c>
/// exist today with known metadata names, so they are matched on the full name. <c>TenantScope</c>
/// and <c>ITenantDbContextFactory&lt;&gt;</c> arrive with B-06 and their namespace is a decision
/// that task has not made yet; ADR-0007 §3.4 says <c>Aurora.Platform.Tenancy.Contracts</c>, but a
/// rule that guessed a namespace and guessed wrong would match nothing and report no violations -
/// in green, forever. They are therefore matched on the <b>simple name</b>, which the ADR does fix.
/// </para>
/// <para>
/// The cost of simple-name matching is a false positive if some unrelated type is ever called
/// <c>TenantScope</c>. In a codebase whose central invariant is "one tenant scope", that is a
/// collision worth failing on.
/// </para>
/// </remarks>
internal static class TenancyNames
{
    /// <summary>The EF Core base class every module context derives from.</summary>
    public const string DbContext = "Microsoft.EntityFrameworkCore.DbContext";

    /// <summary>ADR-0007 §3.4. Matched by simple name - see the remarks on this class.</summary>
    public const string TenantScopeSimpleName = "TenantScope";

    /// <summary>ADR-0007 §4.1. The backtick-one suffix is metadata's spelling of one type parameter.</summary>
    public const string TenantDbContextFactorySimpleName = "ITenantDbContextFactory`1";

    /// <summary>The assembly allowed to implement <c>ITenantDbContextFactory&lt;&gt;</c> (ADR-0007 §12.3).</summary>
    public const string TenancyAssemblyPrefix = "Aurora.Platform.Tenancy";

    /// <summary>
    /// Contexts that are <b>not</b> tenant contexts: the catalog is the one shared database, and
    /// ADR-0003 rule 3 registers it conventionally.
    /// </summary>
    /// <remarks>
    /// This list is the one way a <c>DbContext</c> escapes the tenancy rules, so it is matched by
    /// simple name, kept to one entry, and asserted by a test. Adding to it is a reviewable diff.
    /// </remarks>
    public static readonly ImmutableHashSet<string> NonTenantContextSimpleNames =
        ImmutableHashSet.Create(StringComparer.Ordinal, "CatalogDbContext");

    /// <summary>Every tenant <c>DbContext</c> in the population.</summary>
    public static ImmutableArray<ScannedType> TenantContextsIn(TypeIndex index) =>
    [
        .. index.All.Where(type =>
            index.DerivesFrom(type, DbContext)
            && !NonTenantContextSimpleNames.Contains(TypeIndex.SimpleNameOf(type.FullName))),
    ];

    /// <summary>Does this type use name a <c>TenantScope</c>?</summary>
    public static bool MentionsTenantScope(TypeUse use) =>
        use.Names.Any(static name =>
            string.Equals(TypeIndex.SimpleNameOf(name), TenantScopeSimpleName, StringComparison.Ordinal));
}
