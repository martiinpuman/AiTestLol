using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Fixtures;
using Aurora.Architecture.Tests.Fixtures.Violations;
using Aurora.Architecture.Tests.Rules;
using Aurora.Architecture.Tests.Solution;
using Shouldly;
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// The five tenancy fitness rules ADR-0007 §12.3 requires: T1, T2, T3, T4 and T5.
/// </summary>
/// <remarks>
/// These are the rules that keep one tenant's data out of another tenant's request. Four of them
/// have nothing to bite on yet - <c>TenantScope</c>, <c>ITenantDbContextFactory&lt;&gt;</c> and the
/// first tenant <c>DbContext</c> arrive with B-05 and B-06 - so each is proven against a
/// deliberately-violating fixture instead of against an empty population, and
/// <c>RuleInventoryTests</c> records which are inert today.
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class TenancyRuleTests
{
    private static TypeIndex Production => TypeIndex.Of(SolutionLayout.ProductionTypes);

    private static TypeIndex Fixtures => TypeIndex.Of(FixtureAssembly.AllViolations);

    // ---- T1: no public or protected constructor on a tenant DbContext -----------------------

    [Fact]
    public void T1_no_tenant_DbContext_in_production_has_a_public_or_protected_constructor()
    {
        // Inert today: no tenant DbContext exists yet (B-05 brings the catalog context, B-06 the
        // first tenant one). RuleInventoryTests asserts that this is still true, so the day a
        // context appears the inventory turns red and this floor is raised deliberately.
        RuleAssert.Holds(TenantDbContextConstructorRule.Check(Production), minimumSubjects: 0);
    }

    [Fact]
    public void T1_fires_on_a_public_constructor_and_on_a_protected_one()
    {
        RuleOutcome outcome = TenantDbContextConstructorRule.Check(Fixtures);

        RuleAssert.Reports(outcome, nameof(TenantDbContextWithAPublicConstructor), ViolationSite.Signature);
        RuleAssert.Reports(outcome, nameof(TenantDbContextWithAProtectedConstructor), ViolationSite.Signature);
    }

    [Fact]
    public void T1_stays_silent_on_an_internal_constructor_two_inheritance_steps_from_DbContext()
    {
        RuleOutcome outcome = TenantDbContextConstructorRule.Check(Fixtures);

        // Silence here is only meaningful because the rule did recognise this type as a tenant
        // context: it is two steps from DbContext, and the count below proves the walk reached it.
        outcome.Violations
            .Where(v => v.Subject.Contains(nameof(TenantDbContextWithAnInternalConstructor)))
            .ShouldBeEmpty(outcome.Describe());

        outcome.SubjectsExamined.ShouldBe(3, "three fixture contexts are tenant contexts; CatalogDbContext is not");
    }

    [Fact]
    public void T1_stays_silent_on_the_catalog_context_which_is_the_one_allowed_exception()
    {
        RuleOutcome outcome = TenantDbContextConstructorRule.Check(Fixtures);

        outcome.Violations
            .Where(v => v.Subject.Contains(nameof(CatalogDbContext)))
            .ShouldBeEmpty("the catalog is the one shared database and is registered conventionally");
    }

    [Fact]
    public void The_only_context_exempt_from_the_tenancy_rules_is_the_catalog()
    {
        TenancyNames.NonTenantContextSimpleNames.ShouldBe(["CatalogDbContext"]);
    }

    // ---- T2: no AddDbContext registration of a tenant context -------------------------------

    [Fact]
    public void T2_no_production_code_registers_a_tenant_DbContext()
    {
        RuleAssert.Holds(TenantDbContextRegistrationRule.Check(Production), minimumSubjects: 0);
    }

    [Fact]
    public void T2_fires_on_AddDbContext_and_on_AddDbContextFactory()
    {
        RuleOutcome outcome = TenantDbContextRegistrationRule.Check(Fixtures);

        RuleAssert.Reports(outcome, nameof(ContainerRegistrations.RegisterTenantContext), ViolationSite.MemberReference);
        RuleAssert.Reports(
            outcome,
            nameof(ContainerRegistrations.RegisterTenantContextFactory),
            ViolationSite.MemberReference);
    }

    [Fact]
    public void T2_stays_silent_on_the_conventional_catalog_registration()
    {
        RuleOutcome outcome = TenantDbContextRegistrationRule.Check(Fixtures);

        outcome.Violations
            .Where(v => v.Subject.Contains(nameof(ContainerRegistrations.RegisterCatalog)))
            .ShouldBeEmpty(outcome.Describe());

        outcome.SubjectsExamined.ShouldBe(3, "the fixture makes three AddDbContext* calls, one of them legitimate");
    }

    // ---- T3: ITenantDbContextFactory<> only in Aurora.Platform.Tenancy ----------------------

    [Fact]
    public void T3_nothing_in_production_implements_ITenantDbContextFactory()
    {
        RuleAssert.Holds(TenantDbContextFactoryRule.Check(SolutionLayout.ProductionTypes), minimumSubjects: 10);
    }

    [Fact]
    public void T3_fires_on_an_implementation_outside_the_tenancy_assembly()
    {
        RuleOutcome outcome = TenantDbContextFactoryRule.Check(FixtureAssembly.AllViolations);

        RuleAssert.Reports(outcome, nameof(FactoryImplementedInTheWrongAssembly), ViolationSite.TypeShape);
    }

    // ---- T4: IHttpContextAccessor only in the tenant-resolution middleware ------------------

    [Fact]
    public void T4_no_production_code_names_IHttpContextAccessor()
    {
        RuleAssert.Holds(HttpContextAccessorRule.Check(SolutionLayout.ProductionTypes), minimumSubjects: 10);
    }

    [Fact]
    public void T4_fires_on_a_constructor_injection_and_on_a_service_locator_inside_a_method_body()
    {
        RuleOutcome outcome = HttpContextAccessorRule.Check(FixtureAssembly.AllViolations);

        RuleAssert.Reports(outcome, nameof(ComponentReadingTheHttpContext), ViolationSite.Field, ViolationSite.Signature);

        // The service-locator fixture's signature is (IServiceProvider) -> string?. Reporting it at
        // Local or MemberReference is the proof that the rule reads method bodies, not just
        // signatures - the same reach F1 needs, for the same reason.
        RuleAssert.Reports(
            outcome,
            nameof(ServiceLocatorReachingForTheHttpContext),
            ViolationSite.Local,
            ViolationSite.MemberReference);
    }

    [Fact]
    public void T4_exempts_a_type_on_the_allow_list_and_only_that_type()
    {
        // Proves the allow-list is an allow-list. The production list is empty until the middleware
        // exists, so without this the exemption path would ship untested.
        ImmutableHashSet<string> allowed = ImmutableHashSet.Create(
            typeof(ComponentReadingTheHttpContext).FullName!);

        RuleOutcome outcome = HttpContextAccessorRule.Check(FixtureAssembly.AllViolations, allowed);

        outcome.Violations
            .Where(v => v.Subject.Contains(nameof(ComponentReadingTheHttpContext)))
            .ShouldBeEmpty("the allow-listed type is exempt");

        RuleAssert.Reports(
            outcome,
            nameof(ServiceLocatorReachingForTheHttpContext),
            ViolationSite.Local,
            ViolationSite.MemberReference);
    }

    [Fact]
    public void T4_allows_no_type_yet_because_the_resolution_middleware_does_not_exist()
    {
        HttpContextAccessorRule.AllowedTypes.ShouldBeEmpty();
    }

    // ---- T5: no TenantScope on a singleton --------------------------------------------------

    [Fact]
    public void T5_nothing_in_production_parks_a_TenantScope_on_a_singleton()
    {
        RuleAssert.Holds(TenantScopeSingletonRule.Check(Production), minimumSubjects: 10);
    }

    [Fact]
    public void T5_fires_on_a_static_field_holding_a_scope()
    {
        RuleOutcome outcome = TenantScopeSingletonRule.Check(Fixtures);

        RuleAssert.Reports(outcome, nameof(AmbientScopeHolder), ViolationSite.Field, ViolationSite.Property);
    }

    [Fact]
    public void T5_fires_on_a_container_singleton_holding_a_scope()
    {
        RuleOutcome outcome = TenantScopeSingletonRule.Check(Fixtures);

        RuleAssert.Reports(outcome, nameof(SingletonCacheHoldingAScope), ViolationSite.Field);
    }

    [Fact]
    public void T5_stays_silent_on_a_scoped_registration_of_a_type_that_holds_a_scope()
    {
        RuleOutcome outcome = TenantScopeSingletonRule.Check(Fixtures);

        // Holding a scope is correct for a per-request type. A rule that reported this would be
        // banning the design ADR-0007 §4.5 prescribes, and would be turned off within a week.
        outcome.Violations
            .Where(v => v.Subject.Contains(nameof(ScopedHandlerHoldingAScope)))
            .ShouldBeEmpty(outcome.Describe());

        TenantScopeSingletonRule.SingletonRegisteredTypeNames(FixtureAssembly.AllViolations)
            .ShouldContain(typeof(SingletonCacheHoldingAScope).FullName!);
    }
}
