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

        outcome.SubjectsExamined.ShouldBe(
            4,
            "four fixture types derive from DbContext and none is the exempt (full name, assembly) "
            + "pair, so all four are tenant contexts");
    }

    [Fact]
    public void T1_fires_on_a_context_merely_named_CatalogDbContext_outside_the_catalog_namespace()
    {
        // The re-review's m-1 attack: an exemption matched by simple name let any CatalogDbContext
        // anywhere escape T1, T2 and the inertness guard. The fixture catalog context is exactly
        // that shape - the name, in the wrong namespace - and under the production exemption it
        // must be reported like any other tenant context with a public constructor.
        RuleOutcome outcome = TenantDbContextConstructorRule.Check(Fixtures);

        RuleAssert.Reports(outcome, nameof(CatalogDbContext), ViolationSite.Signature);
    }

    [Fact]
    public void T1_fires_on_a_module_context_whose_base_type_lives_in_the_tenancy_assembly()
    {
        // B-06's shape, and the only T1 test that fails when the base walk gives up at an assembly
        // boundary: every compiled fixture reaches DbContext without crossing one. Under that
        // fault T1 examines the tenancy base alone, reports nothing about the module context with
        // the public constructor, and the rest of the suite stays green (B-04 re-review, m-3).
        RuleOutcome outcome = TenantDbContextConstructorRule.Check(
            TypeIndex.Of(CrossAssemblyFixture.ModuleContextAndItsTenancyBase()));

        outcome.SubjectsExamined.ShouldBe(
            2,
            "both the module context and its tenancy-assembly base are tenant contexts: " + outcome.Describe());
        RuleAssert.Reports(outcome, CrossAssemblyFixture.ModuleContextFullName, ViolationSite.Signature);
    }

    [Fact]
    public void T1_stays_silent_on_the_catalog_context_as_the_exact_full_name_and_assembly_pair()
    {
        // The exemption path: the stand-in compiled at B-05's full name, relabelled into B-05's
        // assembly - the one pair ADR-0032 §4.3 exempts - beside the four fixture contexts.
        RuleOutcome outcome = TenantDbContextConstructorRule.Check(
            TypeIndex.Of([.. FixtureAssembly.AllViolations, .. CatalogFixture.TheExemptPair()]));

        outcome.Violations
            .Where(v => v.Subject.Contains(TenancyNames.CatalogDbContext))
            .ShouldBeEmpty("the catalog is the one shared database and is registered conventionally");

        outcome.SubjectsExamined.ShouldBe(4, "the exempt pair is not a tenant context; the four fixture contexts are");
    }

    [Fact]
    public void T1_fires_on_the_catalog_context_at_the_right_full_name_but_in_the_wrong_assembly()
    {
        // Half an identity is no identity: the stand-in as compiled has the exempt full name in
        // this test assembly, and is a tenant context with a public constructor.
        RuleOutcome outcome = TenantDbContextConstructorRule.Check(
            TypeIndex.Of([.. FixtureAssembly.AllViolations, .. CatalogFixture.TheRightNameInTheWrongAssembly()]));

        RuleAssert.Reports(outcome, TenancyNames.CatalogDbContext, ViolationSite.Signature);
        outcome.SubjectsExamined.ShouldBe(5, "five contexts, none of them the exempt pair");
    }

    [Fact]
    public void The_only_context_exempt_from_the_tenancy_rules_is_the_catalog_as_one_exact_pair()
    {
        TenancyNames.NonTenantContexts.ShouldBe(
            [new NonTenantContext("Aurora.Platform.Tenancy.Catalog.CatalogDbContext", "Aurora.Platform.Tenancy")]);
    }

    // ---- T2: the AddDbContext family only in Aurora.Platform.Tenancy, only for the catalog ---

    [Fact]
    public void T2_no_production_code_calls_the_AddDbContext_family()
    {
        // Inert today: no AddDbContext* call exists in production. B-05 brings the catalog's, inside
        // Aurora.Platform.Tenancy, and RuleInventoryTests holds the expiry.
        RuleAssert.Holds(TenantDbContextRegistrationRule.Check(Production), minimumSubjects: 0);
    }

    [Fact]
    public void T2_fires_on_every_call_outside_the_tenancy_assembly_whatever_it_registers()
    {
        // The fixture assembly is not Aurora.Platform.Tenancy, so the site is wrong for all four
        // calls and all four are reported: two tenant contexts, the catalog itself at its exact
        // full name (right argument, wrong site), and the helper's body.
        RuleOutcome outcome = TenantDbContextRegistrationRule.Check(Fixtures);

        RuleAssert.Reports(outcome, nameof(ContainerRegistrations.RegisterTenantContext), ViolationSite.MemberReference);
        RuleAssert.Reports(outcome, nameof(ContainerRegistrations.RegisterTenantContextFactory), ViolationSite.MemberReference);
        RuleAssert.Reports(outcome, nameof(ContainerRegistrations.RegisterCatalog), ViolationSite.MemberReference);
        outcome.SubjectsExamined.ShouldBe(4, "four AddDbContext* calls: three direct and the helper's body");
        outcome.Violations.Length.ShouldBe(4, outcome.Describe());
    }

    [Fact]
    public void T2_fires_on_the_helper_body_whose_argument_is_a_type_parameter()
    {
        // Re-review H-2, ADR-0032 §4.1.2 (b). Keyed on the argument, the old rule saw !!0, matched
        // nothing, and still counted the call as examined; keyed on the site, the body is reported
        // wherever it lives. The helper's *call site* is outside this population by design: its
        // member name is not AddDbContext*, and ADR-0032 §4.1.1 assigns it to the container-surface
        // rule, which is a separate task.
        RuleOutcome outcome = TenantDbContextRegistrationRule.Check(Fixtures);

        RuleViolation helper = RuleAssert.Reports(
            outcome,
            nameof(ContainerRegistrations.AddTenantDbContext),
            ViolationSite.MemberReference);

        helper.Detail.ShouldContain("!!0", Case.Sensitive, "the report must show the argument the rule could not resolve");
    }

    [Fact]
    public void T2_stays_silent_only_on_the_catalog_registration_and_only_inside_the_tenancy_assembly()
    {
        // The same four calls relabelled into Aurora.Platform.Tenancy: the site is right, so the
        // argument is judged. The catalog at its exact full name passes; two tenant contexts and a
        // type parameter do not.
        RuleOutcome outcome = TenantDbContextRegistrationRule.Check(
            TypeIndex.Of(CatalogFixture.RegistrationsInsideTheTenancyAssembly()));

        outcome.Violations
            .Where(v => v.Subject.Contains(nameof(ContainerRegistrations.RegisterCatalog)))
            .ShouldBeEmpty(outcome.Describe());
        RuleAssert.Reports(outcome, nameof(ContainerRegistrations.RegisterTenantContext), ViolationSite.MemberReference);
        RuleAssert.Reports(outcome, nameof(ContainerRegistrations.RegisterTenantContextFactory), ViolationSite.MemberReference);
        RuleAssert.Reports(outcome, nameof(ContainerRegistrations.AddTenantDbContext), ViolationSite.MemberReference);
        outcome.SubjectsExamined.ShouldBe(4, "the same four calls, now at the permitted site");
        outcome.Violations.Length.ShouldBe(3, outcome.Describe());
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

    [Fact]
    public void T3_fires_on_the_DDL_path_factory_too_which_ADR_0027_puts_under_the_same_rule()
    {
        RuleOutcome outcome = TenantDbContextFactoryRule.Check(FixtureAssembly.AllViolations);

        RuleAssert.Reports(outcome, nameof(MigrationFactoryImplementedInTheWrongAssembly), ViolationSite.TypeShape);
    }

    [Fact]
    public void T3_stays_silent_inside_the_tenancy_assembly()
    {
        // The exemption path: the same implementation relabelled into the permitted assembly.
        // Without this the allow-list would ship untested. Its prefix matching is a known, open gap
        // (re-review m-2; TenancyNames.TenancyAssemblyPrefix says why it is still a prefix).
        RuleOutcome outcome = TenantDbContextFactoryRule.Check(
            FixtureAssembly.ViolationInAssembly(nameof(FactoryImplementedInTheWrongAssembly), TenancyNames.TenancyAssemblyPrefix));

        outcome.Violations.ShouldBeEmpty(outcome.Describe());
        outcome.SubjectsExamined.ShouldBe(1);
    }

    // ---- T6: TenantDatabaseHandle confined to a named allow-list (ADR-0027 §1) ---------------

    [Fact]
    public void T6_no_production_assembly_outside_the_allow_list_names_TenantDatabaseHandle()
    {
        RuleAssert.Holds(TenantDatabaseHandleRule.Check(SolutionLayout.ProductionTypes), minimumSubjects: 10);
    }

    [Fact]
    public void T6_fires_on_a_module_that_names_the_handle_only_inside_a_method_body()
    {
        RuleOutcome outcome = TenantDatabaseHandleRule.Check(FixtureAssembly.AllViolations);

        // (string) -> bool for a signature: reporting it proves the body was read.
        RuleAssert.Reports(
            outcome,
            nameof(ModuleReachingForTheDdlPath),
            ViolationSite.Local,
            ViolationSite.MemberReference);
    }

    [Fact]
    public void T6_stays_silent_inside_an_allow_listed_assembly()
    {
        // The fixture assembly is not Aurora.Platform.Tenancy, so the allow-list is exercised by
        // passing the prefix that does match it. Without this the exemption path ships untested.
        RuleOutcome outcome = TenantDatabaseHandleRule.Check(
            FixtureAssembly.AllViolations,
            ["Aurora.Architecture.Tests"]);

        outcome.Violations.ShouldBeEmpty(outcome.Describe());
        outcome.SubjectsExamined.ShouldBe(0, "every fixture type is inside the allow-listed assembly");
    }

    [Fact]
    public void T6_allows_only_the_tenancy_assembly_prefix_today()
    {
        // A prefix, and a known, open gap: Aurora.Platform.TenancyBypass would pass it (re-review
        // m-2). TenancyNames.TenancyAssemblyPrefix says why it is still a prefix.
        TenantDatabaseHandleRule.AllowedAssemblyPrefixes.ShouldBe(["Aurora.Platform.Tenancy"]);
        TenantDatabaseHandleRule.IsAllowed("Aurora.Platform.Tenancy").ShouldBeTrue();
        TenantDatabaseHandleRule.IsAllowed("Aurora.Modules.Sales.Application").ShouldBeFalse();
        TenantDatabaseHandleRule.IsAllowed("Aurora.Web").ShouldBeFalse();
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
    public void T5_fires_on_a_singleton_holding_the_DDL_path_handle_as_well_as_a_scope()
    {
        // ADR-0027 gives the tenant proof two forms. A rule matching only "TenantScope" would have
        // stopped covering half of them the day that ADR was accepted.
        RuleOutcome outcome = TenantScopeSingletonRule.Check(Fixtures);

        RuleAssert.Reports(outcome, nameof(SingletonCacheHoldingAHandle), ViolationSite.Field);
    }

    [Fact]
    public void T5_fires_on_a_hosted_service_holding_a_scope_whatever_its_registration_is_called()
    {
        // Re-review H-3 (A4). AddHostedService contains no "Singleton", so a rule keyed on that word
        // never put the worker in its singleton set. A hosted service is a singleton by shape - the
        // exact framework interface - and the shape is what the rule reads.
        RuleOutcome outcome = TenantScopeSingletonRule.Check(Fixtures);

        RuleAssert.Reports(outcome, nameof(HostedServiceHoldingAScope), ViolationSite.Field);
        TenantScopeSingletonRule.HostedServiceTypeNames(Fixtures)
            .ShouldContain(typeof(HostedServiceHoldingAScope).FullName!);
    }

    [Fact]
    public void T5_fires_on_a_BackgroundService_holding_the_DDL_path_handle()
    {
        // B-08's shape: a worker deriving from the framework base, one hop from the name the rule
        // matches, holding the handle for its whole life.
        RuleOutcome outcome = TenantScopeSingletonRule.Check(Fixtures);

        RuleAssert.Reports(outcome, nameof(BackgroundWorkerHoldingAHandle), ViolationSite.Field);
        TenantScopeSingletonRule.HostedServiceTypeNames(Fixtures)
            .ShouldContain(typeof(BackgroundWorkerHoldingAHandle).FullName!);
    }

    [Fact]
    public void T5_says_how_many_of_the_types_it_examined_have_singleton_lifetime()
    {
        // First review n-7: the count named a population it did not measure. Two registered
        // singletons and two hosted services are the fixture's singleton-lifetime types.
        RuleOutcome outcome = TenantScopeSingletonRule.Check(Fixtures);

        outcome.SubjectKind.ShouldBe("types, of which 4 have singleton lifetime");
        outcome.SubjectsExamined.ShouldBe(FixtureAssembly.AllViolations.Length);
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
