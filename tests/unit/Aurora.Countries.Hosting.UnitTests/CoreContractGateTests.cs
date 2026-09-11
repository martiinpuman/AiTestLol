using System;
using System.Text;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Hosting.UnitTests;

/// <summary>
/// The gate Odoo does not have (ADR-0008 §5.1 step 3, §5.2), and the acceptance criterion for B-12:
/// install is refused against an out-of-range <c>coreContractRange</c>, with both versions named.
/// </summary>
/// <remarks>
/// Odoo's core and localization modules are versioned semi-independently with no automated
/// compatibility check, and the result is <i>"Error while loading the localization. You should
/// probably update your localization app first"</i> — thrown at runtime, naming neither version.
/// Naming both is not politeness; it is the difference between a five-minute fix and the support
/// thread that made Odoo's a recurring issue.
/// </remarks>
public sealed class CoreContractGateTests
{
    [Theory]
    [InlineData("[1.0.0, 2.0.0)", "1.0.0")]
    [InlineData("[1.0.0, 2.0.0)", "1.9.3")]
    [InlineData("[1.2.0, 3.0.0)", "2.5.1")]
    public void A_package_whose_range_admits_the_running_core_contract_passes(string range, string core) =>
        CoreContractGate.Check(Manifest(range), core).IsSuccess.ShouldBeTrue();

    /// <summary>
    /// A range with no upper bound admits every future MAJOR core contract — including the one that
    /// removes a member this package was built against, which is exactly the failure the gate
    /// exists to stop. A range with no lower bound admits versions that never had the members it
    /// needs. Both are refused even when they admit the version running today, and the refusal says
    /// which end is open and what a bounded range looks like.
    /// </summary>
    [Theory]
    [InlineData("1.0.0", "the upper end")]
    [InlineData("[1.0.0, )", "the upper end")]
    [InlineData("(, 2.0.0)", "the lower end")]
    [InlineData("(, )", "both ends")]
    public void A_range_open_at_either_end_is_refused_even_when_it_admits_the_running_core_contract(
        string range,
        string openEnd)
    {
        Result check = CoreContractGate.Check(Manifest(range), "1.4.0");

        check.IsFailure.ShouldBeTrue();
        check.Error.Code.ShouldBe(HostingErrors.IncompatibleCoreContractCode);
        check.Error.Description.ShouldContain(range);
        check.Error.Description.ShouldContain(openEnd);
        check.Error.Description.ShouldContain("[1.0.0, 2.0.0)");
    }

    /// <summary>
    /// The fleet compatibility report of ADR-0008 §5.2 goes through the remedy path, so a catalogue
    /// version with an open range must not be named as the one that would work: installing it would
    /// be refused by the same rule.
    /// </summary>
    [Fact]
    public void A_catalogue_version_with_an_open_range_is_not_offered_as_the_one_that_would_work()
    {
        Result check = CoreContractGate.Check(
            Manifest("[2.0.0, 3.0.0)", version: "2.0.0"),
            "1.0.0",
            [Manifest("1.0.0", version: "1.4.0")]);

        check.IsFailure.ShouldBeTrue();
        check.Error.Description.ShouldContain("needs a release that does");
    }

    [Theory]
    [InlineData("[2.0.0, 3.0.0)", "1.0.0")]
    [InlineData("[1.0.0, 2.0.0)", "2.0.0")]
    [InlineData("[1.0.0, 2.0.0)", "0.9.0")]
    public void An_install_against_an_out_of_range_core_contract_is_refused_naming_both_versions(
        string range,
        string core)
    {
        Result check = CoreContractGate.Check(Manifest(range), core);

        check.IsFailure.ShouldBeTrue();
        check.Error.Code.ShouldBe(HostingErrors.IncompatibleCoreContractCode);

        // Both versions, by name. This is the assertion the whole gate exists for.
        check.Error.Description.ShouldContain(range);
        check.Error.Description.ShouldContain(core);
        check.Error.Description.ShouldContain("aurora.country.testland");
        check.Error.Description.ShouldContain("1.4.0");
    }

    /// <summary>
    /// The sentence an operator actually needs: not only that this version does not work, but which
    /// one does.
    /// </summary>
    [Fact]
    public void When_the_catalogue_holds_a_version_that_would_work_the_refusal_names_it()
    {
        Result check = CoreContractGate.Check(
            Manifest("[2.0.0, 3.0.0)", version: "2.0.0"),
            "1.0.0",
            [Manifest("[1.0.0, 2.0.0)", version: "1.4.0"), Manifest("[3.0.0, 4.0.0)", version: "3.0.0")]);

        check.IsFailure.ShouldBeTrue();
        check.Error.Description.ShouldContain("Install version(s) [1.4.0]");
    }

    [Fact]
    public void When_no_version_in_the_catalogue_would_work_the_refusal_says_so()
    {
        Result check = CoreContractGate.Check(
            Manifest("[2.0.0, 3.0.0)", version: "2.0.0"),
            "1.0.0",
            [Manifest("[3.0.0, 4.0.0)", version: "3.0.0")]);

        check.IsFailure.ShouldBeTrue();
        check.Error.Description.ShouldContain("needs a release that does");
    }

    [Fact]
    public void A_range_that_is_not_a_range_is_refused_as_the_package_s_problem()
    {
        Result check = CoreContractGate.Check(Manifest("whatever core we find"), "1.0.0");

        check.IsFailure.ShouldBeTrue();
        check.Error.Code.ShouldBe(HostingErrors.IncompatibleCoreContractCode);
        check.Error.Description.ShouldContain("not a NuGet version range");
    }

    /// <summary>
    /// A core contract version that does not parse is core's build problem, not the package's, and
    /// the two are different errors because they are fixed by different people.
    /// </summary>
    [Fact]
    public void A_core_contract_version_that_is_not_a_version_is_refused_as_core_s_problem()
    {
        Result check = CoreContractGate.Check(Manifest("[1.0.0, 2.0.0)"), "not-a-version");

        check.IsFailure.ShouldBeTrue();
        check.Error.Code.ShouldBe(HostingErrors.HostConfigurationCode);
        check.Error.Description.ShouldContain(CoreContract.AssemblyName);
    }

    /// <summary>
    /// The gate takes the version as a parameter so the release-build fleet compatibility report of
    /// ADR-0008 §5.2 can ask about a target version this process is not running — without a second
    /// implementation of the rule, which is how the two would drift apart.
    /// </summary>
    [Fact]
    public void The_gate_answers_for_a_target_core_contract_version_not_only_the_running_one()
    {
        CountryPackageManifest manifest = Manifest("[1.0.0, 2.0.0)");

        CoreContractGate.Check(manifest, CoreContract.Version).IsSuccess.ShouldBeTrue();
        CoreContractGate.Check(manifest, "2.0.0").IsFailure.ShouldBeTrue();
    }

    private static CountryPackageManifest Manifest(string coreContractRange, string version = "1.4.0")
    {
        string json = $$"""
            {
              "id": "aurora.country.testland",
              "key": "tl",
              "displayName": "Testland",
              "version": "{{version}}",
              "coreContractRange": "{{coreContractRange}}",
              "jurisdiction": { "countryCode": "NZ", "defaultCurrency": "NZD", "locales": ["en-NZ"] },
              "capabilities": ["TaxRuleSet"],
              "schema": "pkg_tl",
              "localeOnly": false,
              "publisher": "Aurora",
              "trust": "FirstParty"
            }
            """;

        Result<CountryPackageManifest> manifest =
            CountryPackageManifestJson.Read(Encoding.UTF8.GetBytes(json));

        return manifest.IsSuccess
            ? manifest.Value
            : throw new InvalidOperationException($"The test manifest is invalid: {manifest.Error}");
    }
}
