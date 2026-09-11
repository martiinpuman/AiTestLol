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

    /// <summary>ADR-0027 §1: the DDL-path proof type, confined to a named allow-list of assemblies.</summary>
    public const string TenantDatabaseHandleSimpleName = "TenantDatabaseHandle";

    /// <summary>
    /// Every type that proves "we know which tenant we are in": ADR-0007's <c>TenantScope</c>,
    /// ADR-0027's <c>TenantDatabaseHandle</c>, and the <c>TenantAccess</c> base they share.
    /// </summary>
    /// <remarks>
    /// The base type matters to T5. A singleton field typed <c>TenantAccess</c> holds one tenant's
    /// proof exactly as a field typed <c>TenantScope</c> does, and a rule matching only the derived
    /// name would miss it - which is the shape of defect this whole project exists to catch.
    /// </remarks>
    public static readonly ImmutableHashSet<string> TenantAccessSimpleNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        TenantScopeSimpleName,
        TenantDatabaseHandleSimpleName,
        "TenantAccess");

    /// <summary>ADR-0007 §4.1. The backtick-one suffix is metadata's spelling of one type parameter.</summary>
    public const string TenantDbContextFactorySimpleName = "ITenantDbContextFactory`1";

    /// <summary>
    /// ADR-0027 §1's DDL-path sibling, which the ADR says carries "the same fitness rule as
    /// ADR-0007 §4.2".
    /// </summary>
    public const string TenantMigrationContextFactorySimpleName = "ITenantMigrationContextFactory`1";

    /// <summary>Both factory interfaces T3 confines to the tenancy assembly.</summary>
    public static readonly ImmutableHashSet<string> TenantContextFactorySimpleNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        TenantDbContextFactorySimpleName,
        TenantMigrationContextFactorySimpleName);

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

    /// <summary>Does this type use name a tenant access proof - a scope, a handle, or their base?</summary>
    public static bool MentionsTenantAccess(TypeUse use) =>
        use.Names.Any(static name => TenantAccessSimpleNames.Contains(TypeIndex.SimpleNameOf(name)));

    /// <summary>Does this type use name ADR-0027's DDL-path handle?</summary>
    public static bool MentionsTenantDatabaseHandle(TypeUse use) =>
        use.Names.Any(static name =>
            string.Equals(TypeIndex.SimpleNameOf(name), TenantDatabaseHandleSimpleName, StringComparison.Ordinal));
}
