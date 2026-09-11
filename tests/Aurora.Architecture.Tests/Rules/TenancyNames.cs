using System;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// A <c>DbContext</c> that is not a tenant context, identified by the exact (full name, assembly)
/// pair - the two identities a rule may key on because the code it judges cannot mint them
/// (ADR-0032 §2, §4.3).
/// </summary>
internal sealed record NonTenantContext(string FullName, string AssemblyName)
{
    public bool Matches(ScannedType type) =>
        string.Equals(type.FullName, FullName, StringComparison.Ordinal)
        && string.Equals(type.AssemblyName, AssemblyName, StringComparison.Ordinal);
}

/// <summary>
/// How the tenancy rules recognise the types ADR-0007 governs, and why each is matched the way it
/// is.
/// </summary>
/// <remarks>
/// <para>
/// <b>The principle (ADR-0032 §2):</b> a rule may only key on an identity the violating code cannot
/// mint for itself. An exact assembly name and a full type name qualify - changing either is a diff
/// a reviewer sees. A method name, an assembly-name prefix and a simple type name do not, and this
/// task's re-review walked past one of each.
/// </para>
/// <para>
/// <b>Two matching strategies for subjects, deliberately.</b> <c>DbContext</c> and
/// <c>IHttpContextAccessor</c> exist today with known metadata names, so they are matched on the
/// full name. <c>TenantScope</c> and <c>ITenantDbContextFactory&lt;&gt;</c> arrive with B-06 and
/// their namespace is a decision that task has not made yet; ADR-0007 §3.4 says
/// <c>Aurora.Platform.Tenancy.Contracts</c>, but a rule that guessed a namespace and guessed wrong
/// would match nothing and report no violations - in green, forever. They are therefore matched on
/// the <b>simple name</b>, which the ADR does fix. That is acceptable for a <i>subject</i>: a
/// too-wide subject match produces a false positive, which fails loudly and is fixed (ADR-0032
/// §4.2; §6 item 4 owns tightening it once B-06 fixes the namespaces).
/// </para>
/// <para>
/// <b>Exemptions are exact.</b> The same width is fatal in an exemption, because a too-wide
/// exemption fails silently: matched by simple name, any <c>CatalogDbContext</c> anywhere escaped
/// T1, T2 and the inertness guard (re-review m-1). The catalog context is therefore exempt only as
/// the exact (full name, assembly) pair B-05 declares, and T13 reports both a look-alike and the
/// pair's absence from its own assembly, so a rename fails loudly instead of exempting nothing.
/// </para>
/// <para>
/// <b>Allow-lists are exact assembly names.</b> A prefix let a new assembly authorise itself by
/// choosing a name - <c>Aurora.Platform.TenancyBypass</c> passed both allow-lists (re-review m-2).
/// An exact allow-list is extended only by editing it, which is a diff a reviewer sees, and T14
/// checks that every entry names an assembly that exists.
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

    /// <summary>
    /// The assembly that implements the tenant context factories, applies the ADR-0007 §4.3
    /// identity check, and is the one place the <c>AddDbContext</c> family may be called
    /// (ADR-0007 §12.3, ADR-0032 §4.1, §4.4). Exact name; B-05 creates the project.
    /// </summary>
    public const string TenancyAssemblyName = "Aurora.Platform.Tenancy";

    /// <summary>
    /// The catalog context's full name, where B-05 declares it
    /// (<c>src/platform/Aurora.Platform.Tenancy/Catalog/CatalogDbContext.cs</c>).
    /// </summary>
    public const string CatalogDbContext = "Aurora.Platform.Tenancy.Catalog.CatalogDbContext";

    /// <summary>
    /// The one shared database's context, registered conventionally (ADR-0003 rule 3) and
    /// therefore the one <c>DbContext</c> the tenancy rules exempt - as this exact pair and nothing
    /// looser (ADR-0032 §4.3).
    /// </summary>
    /// <remarks>
    /// If B-05 lands the context at another name, T1 fires on its public constructor and T13 reports
    /// its assembly as declaring no catalog context; whoever integrates B-05 changes this pair, a
    /// reviewable diff. Any other type called <c>CatalogDbContext</c> - a product catalog in some
    /// module, say - is a tenant context like every other <c>DbContext</c>, and T13 says so.
    /// </remarks>
    public static readonly NonTenantContext CatalogContext = new(CatalogDbContext, TenancyAssemblyName);

    /// <summary>
    /// Every <c>DbContext</c> that is <b>not</b> a tenant context: the catalog, and nothing else.
    /// </summary>
    /// <remarks>
    /// This is the one way a <c>DbContext</c> escapes the tenancy rules, so it is a list of exact
    /// pairs, kept to one entry, and asserted by a test. Adding to it is a reviewable diff
    /// (ADR-0032 §9 names the case: a second shared store).
    /// </remarks>
    public static readonly ImmutableArray<NonTenantContext> NonTenantContexts = [CatalogContext];

    /// <summary>Is this type one of the exempt pairs?</summary>
    public static bool IsNonTenantContext(ScannedType type) => NonTenantContexts.Any(pair => pair.Matches(type));

    /// <summary>
    /// Every tenant <c>DbContext</c> in the population: every type whose base chain reaches
    /// <c>DbContext</c> and that is not an exempt pair.
    /// </summary>
    public static ImmutableArray<ScannedType> TenantContextsIn(TypeIndex index) =>
    [
        .. index.All.Where(type => index.DerivesFrom(type, DbContext) && !IsNonTenantContext(type)),
    ];

    /// <summary>Does this type use name a tenant access proof - a scope, a handle, or their base?</summary>
    public static bool MentionsTenantAccess(TypeUse use) =>
        use.Names.Any(static name => TenantAccessSimpleNames.Contains(TypeIndex.SimpleNameOf(name)));

    /// <summary>Does this type use name ADR-0027's DDL-path handle?</summary>
    public static bool MentionsTenantDatabaseHandle(TypeUse use) =>
        use.Names.Any(static name =>
            string.Equals(TypeIndex.SimpleNameOf(name), TenantDatabaseHandleSimpleName, StringComparison.Ordinal));
}
