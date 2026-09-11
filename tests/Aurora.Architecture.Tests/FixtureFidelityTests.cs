using System;
using System.Linq;
using Aurora.Architecture.Tests.Fixtures;
using Aurora.Architecture.Tests.Fixtures.Violations;
using Aurora.Architecture.Tests.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// What the deliberately-violating fixtures actually are, asserted rather than described.
/// </summary>
/// <remarks>
/// <para>
/// The project file says the EF Core package reference and the ASP.NET framework reference exist so
/// the tenancy fixtures use the real framework types. B-01 shipped a <c>.csproj</c> comment
/// asserting behaviour nothing produced; this class is the mechanism that keeps that comment
/// honest, and the README's claim about stand-ins with it.
/// </para>
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class FixtureFidelityTests
{
    [Fact]
    public void The_DbContext_fixtures_derive_from_the_real_EF_Core_DbContext()
    {
        typeof(TenantDbContextWithAPublicConstructor).BaseType.ShouldBe(typeof(DbContext));
        typeof(CatalogDbContext).BaseType.ShouldBe(typeof(DbContext));

        // And the name the rules match on is the one this real type carries.
        typeof(DbContext).FullName.ShouldBe(Rules.TenancyNames.DbContext);
    }

    [Fact]
    public void The_hosted_service_fixtures_use_the_real_hosting_types_the_rule_matches_on()
    {
        // T5 keys on the exact framework names, not on how a hosted service is registered. These
        // are the names it matches, and the fixtures carry them because they use the real types.
        typeof(IHostedService).IsAssignableFrom(typeof(HostedServiceHoldingAScope)).ShouldBeTrue();
        typeof(BackgroundWorkerHoldingAHandle).BaseType.ShouldBe(typeof(BackgroundService));

        typeof(IHostedService).FullName.ShouldBe(Rules.TenancyNames.HostedService);
        typeof(BackgroundService).FullName.ShouldBe(Rules.TenancyNames.BackgroundService);
    }

    [Fact]
    public void The_http_context_fixtures_use_the_real_ASP_NET_Core_accessor()
    {
        typeof(ComponentReadingTheHttpContext)
            .GetConstructors()
            .SelectMany(static constructor => constructor.GetParameters())
            .Select(static parameter => parameter.ParameterType)
            .ShouldContain(typeof(IHttpContextAccessor));

        typeof(IHttpContextAccessor).FullName.ShouldBe(Rules.HttpContextAccessorRule.BannedType);
    }

    [Fact]
    public void The_catalog_stand_in_is_compiled_at_the_exact_full_name_the_exemption_names()
    {
        // ADR-0032 §4.3's exemption is a (full name, assembly) pair. The full-name half is compiled
        // here so that a registration naming it and a base chain reaching it are real compiler
        // output; CatalogFixture relabels the assembly half. If B-05 declares the catalog context at
        // another name this test cannot see it - nothing here can see that assembly - but T1 fires
        // on its public constructor and the inventory's inertness guard turns red, and
        // TenancyNames.CatalogDbContext is what gets corrected.
        typeof(global::Aurora.Platform.Tenancy.Catalog.CatalogDbContext).FullName
            .ShouldBe(Rules.TenancyNames.CatalogDbContext);
        typeof(global::Aurora.Platform.Tenancy.Catalog.CatalogDbContext).Assembly
            .ShouldBe(typeof(FixtureFidelityTests).Assembly);
        typeof(global::Aurora.Platform.Tenancy.Catalog.CatalogDbContext).BaseType.ShouldBe(typeof(DbContext));
    }

    [Fact]
    public void The_tenancy_stand_ins_carry_the_simple_names_the_rules_match_on()
    {
        // TenantScope and ITenantDbContextFactory<> arrive with B-06. Until then the rules match on
        // the simple name the ADR fixes, and these stand-ins are what proves that matching works.
        typeof(TenantScope).Name.ShouldBe(Rules.TenancyNames.TenantScopeSimpleName);
        typeof(ITenantDbContextFactory<>).Name.ShouldBe(Rules.TenancyNames.TenantDbContextFactorySimpleName);
    }

    [Fact]
    public void The_tenancy_stand_ins_are_declared_in_this_test_assembly_and_nowhere_else()
    {
        // The day B-06 lands a real TenantScope, these stand-ins become confusing rather than
        // useful. This test does not fail then - nothing here can see that assembly - so the README
        // carries the instruction to delete them, and RuleInventoryTests is what turns red.
        typeof(TenantScope).Assembly.ShouldBe(typeof(FixtureFidelityTests).Assembly);
        typeof(ITenantDbContextFactory<>).Assembly.ShouldBe(typeof(FixtureFidelityTests).Assembly);
    }

    [Fact]
    public void Every_violating_fixture_type_lives_under_the_violations_namespace()
    {
        // The production population excludes this assembly wholesale, but the fixture *tests* select
        // by namespace, and a fixture outside it would be silently unused - a violating fixture
        // nothing runs against is the same as no fixture at all.
        typeof(TenantScope).Namespace.ShouldBe(FixtureAssembly.ViolationsNamespace);
        typeof(MoneyMathHiddenInsideAMethodBody).Namespace.ShouldBe(FixtureAssembly.ViolationsNamespace);
    }

    [Fact]
    public void The_scanner_sees_the_fixture_assembly_as_one_assembly_with_the_expected_name()
    {
        ScannedAssembly self = FixtureAssembly.Self;

        self.Name.ShouldBe("Aurora.Architecture.Tests");
        self.Types.Select(static type => type.FullName)
            .ShouldContain(typeof(MoneyMathHiddenInsideAMethodBody).FullName!, StringComparer.Ordinal);
    }
}
