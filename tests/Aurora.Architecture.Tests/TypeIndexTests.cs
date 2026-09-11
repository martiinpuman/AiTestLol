using System;
using System.Linq;
using Aurora.Architecture.Tests.Fixtures;
using Aurora.Architecture.Tests.Metadata;
using Aurora.Architecture.Tests.Rules;
using Shouldly;
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// The base-type walk every tenancy rule stands on, proven at the one hop the compiled fixtures
/// never take: from a type in one assembly to its base in another.
/// </summary>
/// <remarks>
/// A walk that stopped at an assembly boundary would still pass every compiled-fixture test,
/// because those fixtures reach <c>DbContext</c> without crossing one. It would also leave T1, T2
/// and T5 blind to every module context and every inherited holder in the solution, in green
/// (<c>docs/reviews/B-04-rereview.md</c> m-3). These tests are what turns red.
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class TypeIndexTests
{
    [Fact]
    public void DerivesFrom_walks_the_base_chain_across_an_assembly_boundary()
    {
        TypeIndex index = TypeIndex.Of(CrossAssemblyFixture.ModuleContextAndItsTenancyBase());
        ScannedType moduleContext = index.Find(CrossAssemblyFixture.ModuleContextFullName)!;
        ScannedType tenancyBase = index.Find(CrossAssemblyFixture.TenancyBaseFullName)!;

        moduleContext.AssemblyName.ShouldNotBe(
            tenancyBase.AssemblyName,
            "the fixture is only a test of the boundary if there is one");

        index.DerivesFrom(moduleContext, TenancyNames.DbContext).ShouldBeTrue(
            "SalesDbContext -> ModuleDbContext (another assembly) -> DbContext must resolve the middle hop");
    }

    [Fact]
    public void MembersOf_walks_the_base_chain_across_an_assembly_boundary()
    {
        TypeIndex index = TypeIndex.Of(CrossAssemblyFixture.HolderDerivingFromABaseInAnotherAssembly());
        ScannedType derived = index.Find("Aurora.Modules.Sales.Infrastructure.SalesScopeCache")!;

        // The derived type declares no field; the only one it can report is inherited across the
        // boundary.
        index.MembersOf(derived)
            .Where(member => TenancyNames.MentionsTenantAccess(member.Type))
            .Select(member => member.Owner)
            .ShouldBe(["Aurora.Platform.Tenancy.ScopeCacheBase"]);
    }

    [Fact]
    public void A_tenant_context_is_recognised_through_a_base_type_in_another_assembly()
    {
        // The shared recognition T1 and T2 both call. If the walk stops at the boundary this
        // returns only the base, the module context is no tenant context, and both rules fall
        // silent on it.
        TypeIndex index = TypeIndex.Of(CrossAssemblyFixture.ModuleContextAndItsTenancyBase());

        TenancyNames.TenantContextsIn(index)
            .Select(static type => type.FullName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ShouldBe(
            [
                CrossAssemblyFixture.ModuleContextFullName,
                CrossAssemblyFixture.TenancyBaseFullName,
            ]);
    }
}
