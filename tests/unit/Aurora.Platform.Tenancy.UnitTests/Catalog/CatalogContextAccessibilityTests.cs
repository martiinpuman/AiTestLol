using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Aurora.Platform.Tenancy.Catalog;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

/// <summary>
/// The accessibility decision of <see cref="CatalogDbContext"/>, as a test rather than a comment.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0007 §4.2 carves the catalog out of the tenant-context rules — it is registered
/// conventionally, because it is where tenants are resolved <em>from</em> and so cannot demand a
/// resolved tenant. That carve-out is about <em>construction</em>. It is not a reason to publish
/// the type, and ADR-0007 §9.4's "no request-path query fans out across tenants" is a reason not
/// to: a type nobody outside <c>Aurora.Platform.Tenancy</c> can name is a type nobody outside it
/// can write a fan-out query with.
/// </para>
/// <para>
/// So the whole catalog namespace stays internal and the assembly's public surface is the DI
/// extension alone.
/// </para>
/// <para>
/// <b>Two layers, and the first is the compiler.</b> Flipping <c>CatalogDbContext</c> alone to
/// <c>public</c> does not build: its <c>DbSet&lt;Tenant&gt;</c> and the other four are CS0053,
/// "property type is less accessible", because the entities are internal too. The step the
/// compiler <em>does</em> allow is publishing a type further down — an entity, a value object —
/// one at a time, until the context can follow. That is what
/// <see cref="The_tenancy_assembly_publishes_only_its_DI_extension"/> is for, and it fails on the
/// first such type: made <c>public</c>, <c>TenantHost</c> is reported as an unexpected export.
/// </para>
/// </remarks>
public sealed class CatalogContextAccessibilityTests
{
    /// <summary>
    /// The one type <c>Aurora.Platform.Tenancy</c> is allowed to export. Everything else a caller
    /// needs is in <c>Aurora.Platform.Tenancy.Contracts</c> (<c>solution-layout.md</c> §2: the
    /// public surface of a platform module is its Contracts assembly).
    /// </summary>
    private static readonly IReadOnlySet<string> PublishedTypes =
        new HashSet<string>(StringComparer.Ordinal) { "Aurora.Platform.Tenancy.CatalogServiceCollectionExtensions" };

    /// <summary>
    /// The one namespace exempt from the rule below, and why. <c>dotnet ef migrations add</c>
    /// scaffolds <c>public partial class</c>, and EF Core finds migrations by attribute rather than
    /// by accessibility, so hand-editing them to internal would be undone by the next scaffold. A
    /// migration is inert DDL whose signature names no registry type, so exporting one gives a
    /// caller nothing to query with — unlike <c>CatalogDbContext</c>, which is the point of the rule.
    /// </summary>
    private const string ScaffoldedMigrations = "Aurora.Platform.Tenancy.Migrations.";

    [Fact]
    public void The_tenancy_assembly_publishes_only_its_DI_extension()
    {
        IEnumerable<string> exported = typeof(CatalogDbContext).Assembly.GetExportedTypes()
            .Select(type => type.FullName!)
            .Where(name => !name.StartsWith(ScaffoldedMigrations, StringComparison.Ordinal));

        exported.OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(
                PublishedTypes.OrderBy(name => name, StringComparer.Ordinal),
                "Aurora.Platform.Tenancy exports its DI extension and nothing else; the registry, its "
                + "entities and CatalogDbContext are internal so that no module can query the tenant "
                + "registry or build a connection string (ADR-0007 §9.4, modules.md §4).");
    }

    [Fact]
    public void The_catalog_context_is_a_DbContext_that_needs_no_tenant_scope()
    {
        // The other half of the decision, and the reason the rule above is not simply "hide
        // everything": the catalog context really is constructible from options alone. That is what
        // ADR-0007 §4.2 permits for this one context and forbids for every tenant context, which
        // takes a TenantScope it cannot be given without one (B-06).
        ConstructorInfo[] constructors = typeof(CatalogDbContext).GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        typeof(DbContext).IsAssignableFrom(typeof(CatalogDbContext)).ShouldBeTrue();
        constructors.ShouldHaveSingleItem()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ShouldBe([typeof(DbContextOptions<CatalogDbContext>)]);
    }
}
