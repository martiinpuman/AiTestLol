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
/// extension alone. Flip <c>internal sealed class CatalogDbContext</c> to <c>public</c> and
/// <see cref="The_tenancy_assembly_publishes_only_its_DI_extension"/> fails naming it.
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

    [Fact]
    public void The_tenancy_assembly_publishes_only_its_DI_extension()
    {
        IEnumerable<string> exported = typeof(CatalogDbContext).Assembly.GetExportedTypes().Select(type => type.FullName!);

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

        typeof(CatalogDbContext).ShouldBeAssignableTo<DbContext>();
        constructors.ShouldHaveSingleItem()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ShouldBe([typeof(DbContextOptions<CatalogDbContext>)]);
    }
}
