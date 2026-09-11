using System.Linq;
using Aurora.Architecture.Tests.Solution;
using Shouldly;
using Xunit;

namespace Aurora.Architecture.Tests;

/// <summary>
/// The naming convention every layering rule is expressed over
/// (<c>solution-layout.md</c> §1: "the <c>Modules.</c> and <c>Platform.</c> namespace segments are
/// load-bearing").
/// </summary>
/// <remarks>
/// Without these tests the convention could drift and the rules would keep passing: an
/// unrecognised name classifies as <see cref="ProjectKind.Unknown"/>, and a solution of unknowns
/// is a solution no layering rule constrains. The last test here is the guard against exactly that.
/// </remarks>
[Trait("Category", "Architecture")]
public sealed class ProjectIdentityTests
{
    [Theory]
    [InlineData("Aurora.SharedKernel", ProjectKind.Kernel, "Aurora.SharedKernel", ModuleLayer.Whole)]
    [InlineData("Aurora.Documents.Canonical", ProjectKind.Kernel, "Aurora.Documents.Canonical", ModuleLayer.Whole)]
    [InlineData("Aurora.Countries.Contracts", ProjectKind.Kernel, "Aurora.Countries.Contracts", ModuleLayer.Whole)]
    [InlineData("Aurora.Countries.Hosting", ProjectKind.Platform, "Countries.Hosting", ModuleLayer.Whole)]
    [InlineData("Aurora.Countries.NewZealand", ProjectKind.CountryPackage, "NewZealand", ModuleLayer.Whole)]
    [InlineData("Aurora.Platform.Tenancy", ProjectKind.Platform, "Tenancy", ModuleLayer.Whole)]
    [InlineData("Aurora.Platform.Tenancy.Contracts", ProjectKind.Platform, "Tenancy", ModuleLayer.Contracts)]
    [InlineData("Aurora.Modules.Sales.Contracts", ProjectKind.Module, "Sales", ModuleLayer.Contracts)]
    [InlineData("Aurora.Modules.Sales.Domain", ProjectKind.Module, "Sales", ModuleLayer.Domain)]
    [InlineData("Aurora.Modules.Sales.Application", ProjectKind.Module, "Sales", ModuleLayer.Application)]
    [InlineData("Aurora.Modules.Sales.Infrastructure", ProjectKind.Module, "Sales", ModuleLayer.Infrastructure)]
    [InlineData("Aurora.Web", ProjectKind.Host, "Aurora.Web", ModuleLayer.Whole)]
    [InlineData("Aurora.Worker", ProjectKind.Host, "Aurora.Worker", ModuleLayer.Whole)]
    [InlineData("Aurora.Composition", ProjectKind.Composition, "Aurora.Composition", ModuleLayer.Whole)]
    [InlineData("Aurora.Modules.Sales", ProjectKind.Unknown, "Aurora.Modules.Sales", ModuleLayer.Whole)]
    [InlineData("Contoso.Whatever", ProjectKind.Unknown, "Contoso.Whatever", ModuleLayer.Whole)]
    public void A_project_name_classifies_as(string name, ProjectKind kind, string module, ModuleLayer layer)
    {
        ProjectIdentity.Of(name).ShouldBe(new ProjectIdentity(kind, module, layer));
    }

    [Fact]
    public void No_project_in_the_solution_classifies_as_unknown()
    {
        // An Unknown project is one no layering rule can constrain, and a reference *to* one is
        // reported by ProjectLayeringRule. A reference *from* one would be unconstrained, which is
        // why this test exists separately from the rules.
        SolutionLayout.AllProjects
            .Where(static project => ProjectIdentity.Of(project).Kind == ProjectKind.Unknown)
            .Select(static project => project.RelativePath)
            .ShouldBeEmpty(
                "a project whose name fits none of solution-layout.md §1's conventions is exempt "
                + "from every layering rule; rename it or extend ProjectIdentity");
    }

    [Fact]
    public void Every_project_under_tests_classifies_as_a_test_project()
    {
        SolutionLayout.TestProjects
            .Where(static project => ProjectIdentity.Of(project).Kind != ProjectKind.Test)
            .Select(static project => project.RelativePath)
            .ShouldBeEmpty();
    }
}
