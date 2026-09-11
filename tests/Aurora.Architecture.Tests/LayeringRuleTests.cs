using System.Linq;
using Aurora.Architecture.Tests.Fixtures;
using Aurora.Architecture.Tests.Rules;
using Aurora.Architecture.Tests.Solution;
using Shouldly;
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// Fitness rules L1-L5 and M1: who may reference whom (<c>solution-layout.md</c> §2,
/// <c>modules.md</c> §6, ADR-0006).
/// </summary>
/// <remarks>
/// These rules read <b>declared</b> project references, not emitted assembly references, because a
/// reference that is declared but unused emits nothing - <c>docs/reviews/B-03.md</c> m-3.
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class LayeringRuleTests
{
    [Fact]
    public void Every_project_reference_in_the_solution_is_one_the_layer_table_permits()
    {
        RuleAssert.Holds(ProjectLayeringRule.Check(SolutionLayout.ProductionProjects), minimumSubjects: 3);
    }

    [Fact]
    public void Every_cross_module_reference_is_in_the_matrix()
    {
        // Inert today: no module project exists yet, so there are no cross-module edges to check.
        // RuleInventoryTests records that, and the fixtures below prove the rule fires.
        RuleAssert.Holds(ModuleDependencyRule.Check(SolutionLayout.ProductionProjects), minimumSubjects: 0);
    }

    [Fact]
    public void The_kernel_and_every_domain_assembly_depend_on_the_BCL_and_the_kernel_only()
    {
        RuleAssert.Holds(
            DomainPurityRule.Check(SolutionLayout.ProductionProjects, SolutionLayout.ProductionAssemblies),
            minimumSubjects: 1);
    }

    [Theory]
    [InlineData("Aurora.SharedKernel", true)]
    [InlineData("Aurora.Sales.Domain", true)]
    [InlineData("Aurora.Documents.Canonical", false)]
    [InlineData("Aurora.Countries.Contracts", false)]
    [InlineData("Aurora.Sales.Application", false)]
    [InlineData("Aurora.Web", false)]
    public void L1_governs_the_kernel_and_every_domain_assembly_and_no_other(string assembly, bool inScope)
    {
        // The two other tier-0 assemblies are deliberately outside L1: testing-strategy.md §5.1 does
        // not name them, and ADR-0008 §3.1 requires Aurora.Countries.Contracts to carry an
        // approved-API snapshot test, which needs an analyzer package reference. Their constraint is
        // the one modules.md §3 states - tier 0 references tier 0 - enforced by ProjectLayeringRule.
        DomainPurityRule.IsInScope(assembly).ShouldBe(inScope);
    }

    // ---- L5: a host may not reference a .Domain or an .Infrastructure -----------------------

    [Fact]
    public void L5_fires_when_a_host_references_a_module_domain()
    {
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Web", projectReferences: ["Aurora.Modules.Sales.Domain"]);

        RuleAssert.Reports(
            ProjectLayeringRule.Check(tree.All),
            "Aurora.Modules.Sales.Domain",
            ViolationSite.Declaration);
    }

    [Fact]
    public void L5_stays_silent_when_a_host_reaches_a_module_through_Composition_and_its_contracts()
    {
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Worker", projectReferences: ["Aurora.Composition", "Aurora.Modules.Sales.Contracts"]);

        RuleAssert.Holds(ProjectLayeringRule.Check(tree.All), minimumSubjects: 2);
    }

    // ---- L4: an .Infrastructure is referenced only by Composition ---------------------------

    [Fact]
    public void L4_fires_when_a_module_references_another_modules_infrastructure()
    {
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Modules.Reporting.Application", projectReferences: ["Aurora.Modules.Sales.Infrastructure"]);

        RuleAssert.Reports(
            ProjectLayeringRule.Check(tree.All),
            "Aurora.Modules.Sales.Infrastructure",
            ViolationSite.Declaration);
    }

    [Fact]
    public void L4_stays_silent_for_the_composition_root_which_is_the_one_project_allowed_to_see_them()
    {
        using var tree = new FixtureProjectTree();
        tree.Add(
            "Aurora.Composition",
            projectReferences: ["Aurora.Modules.Sales.Infrastructure", "Aurora.Modules.Ledger.Infrastructure"]);

        RuleAssert.Holds(ProjectLayeringRule.Check(tree.All), minimumSubjects: 2);
    }

    // ---- L2: a module's .Contracts references tier 0 only -----------------------------------

    [Fact]
    public void L2_fires_when_a_contracts_project_references_anything_but_the_kernel()
    {
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Modules.Sales.Contracts", projectReferences: ["Aurora.Platform.Tenancy.Contracts"]);

        RuleAssert.Reports(
            ProjectLayeringRule.Check(tree.All),
            "Aurora.Platform.Tenancy.Contracts",
            ViolationSite.Declaration);
    }

    [Fact]
    public void L2_stays_silent_on_the_two_kernel_assemblies_a_contracts_project_may_reference()
    {
        using var tree = new FixtureProjectTree();
        tree.Add(
            "Aurora.Modules.Sales.Contracts",
            projectReferences: ["Aurora.SharedKernel", "Aurora.Documents.Canonical"]);

        RuleAssert.Holds(ProjectLayeringRule.Check(tree.All), minimumSubjects: 2);
    }

    // ---- L1: a .Domain references the kernel only, and declares no package -------------------

    [Fact]
    public void L1_fires_when_a_domain_project_references_its_own_application()
    {
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Modules.Sales.Domain", projectReferences: ["Aurora.Modules.Sales.Application"]);

        RuleAssert.Reports(
            ProjectLayeringRule.Check(tree.All),
            "Aurora.Modules.Sales.Application",
            ViolationSite.Declaration);
    }

    [Fact]
    public void L1_fires_on_a_package_reference_a_domain_project_declares_but_never_uses()
    {
        // The m-3 case exactly. The compiled half of L1 cannot see this - an unused package emits no
        // assembly reference - so a rule with only that half would call this project clean.
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Modules.Sales.Domain", packageReferences: ["Microsoft.EntityFrameworkCore"]);

        RuleOutcome outcome = DomainPurityRule.CheckDeclarations(tree.All);

        RuleAssert.Reports(outcome, "Aurora.Modules.Sales.Domain", ViolationSite.Declaration);
        outcome.Violations.Count(static v => v.Detail.Contains("packages.lock.json"))
            .ShouldBe(1, "the lock file is read as well as the project file: " + outcome.Describe());
    }

    [Fact]
    public void L1_fires_on_a_framework_reference_too()
    {
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Modules.Sales.Domain", frameworkReferences: ["Microsoft.AspNetCore.App"]);

        RuleAssert.Reports(DomainPurityRule.CheckDeclarations(tree.All), "Sales.Domain", ViolationSite.Declaration);
    }

    [Fact]
    public void L1_fires_when_a_domain_project_has_no_lock_file_to_measure()
    {
        // Without this, "declares no package" would be indistinguishable from "could not tell".
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Modules.Sales.Domain", withLockFile: false);

        RuleAssert.Reports(DomainPurityRule.CheckDeclarations(tree.All), "Sales.Domain", ViolationSite.Declaration)
            .Detail.ShouldContain("no committed packages.lock.json");
    }

    [Fact]
    public void L1_stays_silent_on_a_domain_project_that_references_only_the_kernel()
    {
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Modules.Sales.Domain", projectReferences: ["Aurora.SharedKernel"]);

        RuleAssert.Holds(ProjectLayeringRule.Check(tree.All), minimumSubjects: 1);
        RuleAssert.Holds(DomainPurityRule.CheckDeclarations(tree.All), minimumSubjects: 1);
    }

    [Fact]
    public void L1_fires_on_an_assembly_that_references_something_other_than_the_BCL()
    {
        // The fixture is this test assembly: it references xunit, Shouldly and EF Core, which is
        // exactly what a domain assembly must not do. Handing it to the compiled half of the rule
        // proves the AssemblyRef mechanism fires on real compiler output.
        RuleOutcome outcome = DomainPurityRule.CheckCompiled([FixtureAssembly.Self]);

        RuleAssert.Reports(outcome, "Aurora.Architecture.Tests", ViolationSite.Declaration);
        outcome.Violations.Select(static v => v.Detail).ShouldContain(static d => d.Contains("Shouldly"));
    }

    [Fact]
    public void L1_fires_on_a_banned_namespace_that_lives_in_a_BCL_assembly()
    {
        // System.Text.Json ships in a System.* assembly, so the BCL-name check passes it. This is
        // the case that makes the banned-namespace mechanism more than a duplicate of the other.
        RuleOutcome outcome = DomainPurityRule.CheckCompiled([FixtureAssembly.Self]);

        outcome.Violations
            .Select(static violation => violation.Detail)
            .ShouldContain(static detail => detail.Contains("System.Text.Json"));
    }

    // ---- M1: the module dependency matrix ---------------------------------------------------

    [Fact]
    public void M1_fires_on_a_reference_the_matrix_marks_events_only()
    {
        // modules.md §6: Tax -> Ledger is 'E'. The tier rule alone would allow it - Ledger is a
        // lower tier - which is why the matrix is held as data rather than derived from tiers.
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Modules.Tax.Application", projectReferences: ["Aurora.Modules.Ledger.Contracts"]);

        RuleAssert.Reports(ModuleDependencyRule.Check(tree.All), "Ledger.Contracts", ViolationSite.Declaration)
            .Detail.ShouldContain("events only");
    }

    [Fact]
    public void M1_fires_on_a_reference_the_matrix_leaves_blank()
    {
        // modules.md §6: Payments -> Products is empty, and §6's notes call it out by name.
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Modules.Payments.Application", projectReferences: ["Aurora.Modules.Products.Contracts"]);

        RuleAssert.Reports(ModuleDependencyRule.Check(tree.All), "Products.Contracts", ViolationSite.Declaration);
    }

    [Fact]
    public void M1_fires_when_an_allowed_module_is_reached_through_anything_but_its_contracts()
    {
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Modules.Sales.Application", projectReferences: ["Aurora.Modules.Ledger.Domain"]);

        RuleAssert.Reports(ModuleDependencyRule.Check(tree.All), "Ledger.Domain", ViolationSite.Declaration)
            .Detail.ShouldContain("only through its .Contracts");
    }

    [Fact]
    public void M1_fires_for_a_module_that_has_no_row_in_the_matrix()
    {
        using var tree = new FixtureProjectTree();
        tree.Add("Aurora.Modules.Manufacturing.Application", projectReferences: ["Aurora.Modules.Ledger.Contracts"]);

        RuleAssert.Reports(ModuleDependencyRule.Check(tree.All), "Ledger.Contracts", ViolationSite.Declaration)
            .Detail.ShouldContain("no row in the modules.md §6 matrix");
    }

    [Fact]
    public void M1_stays_silent_on_a_reference_the_matrix_allows()
    {
        using var tree = new FixtureProjectTree();
        tree.Add(
            "Aurora.Modules.Sales.Application",
            projectReferences: ["Aurora.Modules.Ledger.Contracts", "Aurora.Modules.Inventory.Contracts"]);

        RuleAssert.Holds(ModuleDependencyRule.Check(tree.All), minimumSubjects: 2);
    }

    [Fact]
    public void The_matrix_holds_a_row_for_every_module_modules_md_names()
    {
        // The matrix is only as good as its coverage. modules.md §6 has twelve consumer rows;
        // Platform is not a module row here because platform capabilities are matched by kind.
        ModuleMatrix.AllowedProviders.Keys.Order(System.StringComparer.Ordinal).ShouldBe(
        [
            "DocumentExchange", "Inventory", "Ledger", "Organization", "Parties", "Payments",
            "Products", "Purchasing", "Reporting", "Sales", "Tax",
        ]);
    }
}
