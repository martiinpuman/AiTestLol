using System.Text;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Contracts.UnitTests;

/// <summary>
/// Reading a manifest is reading untrusted input off disk. Every rule here refuses something a
/// package could plausibly ship, and names the field it refused.
/// </summary>
public sealed class CountryPackageManifestJsonTests
{
    /// <summary>The manifest ADR-0008 §3.2 prints, so the contract reads the example it documents.</summary>
    private const string AdrExample = """
        {
          "id": "aurora.country.nz",
          "key": "nz",
          "displayName": "New Zealand",
          "version": "1.4.0",
          "coreContractRange": "[2.0.0, 3.0.0)",
          "jurisdiction": { "countryCode": "NZ", "defaultCurrency": "NZD", "locales": ["en-NZ", "mi-NZ"] },
          "capabilities": [
            "ChartOfAccountsTemplate", "TaxRuleSet", "StatutoryReport", "EInvoicingProfile",
            "PaymentFileFormat", "StatementImportFormat", "InterchangeFormat",
            "IdentifierValidator", "LocalePack", "RetentionPolicy"
          ],
          "schema": "pkg_nz",
          "dependsOn": [ { "id": "aurora.region.anz", "range": "[1.0.0, 2.0.0)" } ],
          "conflictsWith": [],
          "localeOnly": false,
          "publisher": "Aurora",
          "trust": "FirstParty"
        }
        """;

    /// <summary>A package that ships a language and no fiscal rules at all.</summary>
    private const string LocaleOnlyExample = """
        {
          "id": "aurora.locale.mi",
          "key": "mi",
          "displayName": "te reo Maori",
          "version": "1.0.0",
          "coreContractRange": "[1.0.0, 2.0.0)",
          "jurisdiction": { "countryCode": "NZ", "defaultCurrency": "NZD", "locales": ["mi-NZ"] },
          "capabilities": ["LocalePack"],
          "schema": "pkg_mi",
          "localeOnly": true,
          "publisher": "Aurora",
          "trust": "FirstParty"
        }
        """;

    [Fact]
    public void The_manifest_the_adr_documents_is_read_in_full()
    {
        CountryPackageManifest manifest = Read(AdrExample);

        manifest.Id.Value.ShouldBe("aurora.country.nz");
        manifest.Key.Value.ShouldBe("nz");
        manifest.SchemaName.ShouldBe("pkg_nz");
        manifest.DisplayName.ShouldBe("New Zealand");
        manifest.Version.Value.ShouldBe("1.4.0");
        manifest.CoreContractRange.ShouldBe("[2.0.0, 3.0.0)");
        manifest.Jurisdiction.CountryCode.ShouldBe("NZ");
        manifest.Jurisdiction.DefaultCurrencyCode.ShouldBe("NZD");
        manifest.Jurisdiction.Locales.Count.ShouldBe(2);
        manifest.Capabilities.Count.ShouldBe(CountryPackageCapabilities.All.Count);
        manifest.DependsOn.Count.ShouldBe(1);
        manifest.DependsOn[0].VersionRange.ShouldBe("[1.0.0, 2.0.0)");
        manifest.ConflictsWith.ShouldBeEmpty();
        manifest.LocaleOnly.ShouldBeFalse();
        manifest.DeclaredTrust.ShouldBe(PackageTrustLevel.FirstParty);
    }

    /// <summary>
    /// A manifest read twice is the same manifest. The package contract test compares the embedded
    /// manifest with the one a package's code returns, and a type without value equality would make
    /// that comparison always fail — or, worse, always pass for the wrong reason.
    /// </summary>
    [Fact]
    public void A_manifest_read_twice_equals_itself()
    {
        Read(AdrExample).ShouldBe(Read(AdrExample));
        Read(AdrExample).GetHashCode().ShouldBe(Read(AdrExample).GetHashCode());
    }

    [Fact]
    public void A_schema_that_is_not_the_key_is_refused_naming_both()
    {
        Error error = Refused(AdrExample.Replace("\"pkg_nz\"", "\"sales\"", System.StringComparison.Ordinal));

        error.Description.ShouldContain("pkg_nz");
        error.Description.ShouldContain("sales");
    }

    /// <summary>
    /// An unknown field means the package was built against a contract this host does not have.
    /// Ignoring it is how a package half-installs with a rule nobody applied.
    /// </summary>
    [Fact]
    public void A_field_this_contract_version_does_not_know_is_refused_and_named()
    {
        Error error = Refused(AdrExample.Replace(
            "\"localeOnly\": false",
            "\"localeOnly\": false, \"eInvoicingMandateFrom\": \"2027-01-01\"",
            System.StringComparison.Ordinal));

        error.Description.ShouldContain("eInvoicingMandateFrom");
    }

    [Fact]
    public void A_capability_this_contract_version_does_not_declare_is_refused_and_named()
    {
        Error error = Refused(AdrExample.Replace(
            "\"RetentionPolicy\"",
            "\"PayrollRuleSet\"",
            System.StringComparison.Ordinal));

        error.Description.ShouldContain("PayrollRuleSet");
    }

    [Fact]
    public void A_capability_declared_twice_is_refused()
    {
        Refused(AdrExample.Replace(
            "\"LocalePack\", \"RetentionPolicy\"",
            "\"LocalePack\", \"LocalePack\", \"RetentionPolicy\"",
            System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"coreContractRange\": \"[2.0.0, 3.0.0)\",", "")]
    [InlineData("\"publisher\": \"Aurora\",", "")]
    [InlineData("\"trust\": \"FirstParty\"", "\"trust\": \"Trusted\"")]
    [InlineData("\"countryCode\": \"NZ\"", "\"countryCode\": \"ZZ\"")]
    [InlineData("\"defaultCurrency\": \"NZD\"", "\"defaultCurrency\": \"NZDD\"")]
    [InlineData("\"locales\": [\"en-NZ\", \"mi-NZ\"]", "\"locales\": []")]
    [InlineData("\"locales\": [\"en-NZ\", \"mi-NZ\"]", "\"locales\": [\"not-a-locale-at-all\"]")]
    [InlineData("\"version\": \"1.4.0\",", "\"version\": \"1.4\",")]
    public void A_manifest_missing_or_malformed_in_one_field_is_refused(string find, string replace) =>
        Refused(AdrExample.Replace(find, replace, System.StringComparison.Ordinal));

    /// <summary>
    /// A locale-only package is how a language reaches the product without a jurisdiction's tax
    /// rules coming with it, so the claim is exact rather than approximate.
    /// </summary>
    [Fact]
    public void A_locale_only_package_may_declare_nothing_but_a_locale_pack()
    {
        string localeOnly = AdrExample
            .Replace("\"localeOnly\": false", "\"localeOnly\": true", System.StringComparison.Ordinal);

        Refused(localeOnly).Description.ShouldContain("LocalePack");

        CountryPackageManifest pack = Read(LocaleOnlyExample);
        pack.LocaleOnly.ShouldBeTrue();
        pack.Capabilities.ShouldBe([CountryPackageCapability.LocalePack]);
    }

    [Fact]
    public void A_package_may_not_depend_on_itself()
    {
        Refused(AdrExample.Replace(
            "\"id\": \"aurora.region.anz\"",
            "\"id\": \"aurora.country.nz\"",
            System.StringComparison.Ordinal));
    }

    /// <summary>An install that must both have and not have a package can never succeed.</summary>
    [Fact]
    public void A_package_may_not_both_depend_on_and_conflict_with_the_same_package()
    {
        Refused(AdrExample.Replace(
            "\"conflictsWith\": []",
            "\"conflictsWith\": [\"aurora.region.anz\"]",
            System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{\"id\": ")]
    public void Anything_that_is_not_a_manifest_object_is_refused(string json) =>
        CountryPackageManifestJson.Read(Encoding.UTF8.GetBytes(json)).IsFailure.ShouldBeTrue();

    private static CountryPackageManifest Read(string json) =>
        ContractTestValues.Ok(CountryPackageManifestJson.Read(Encoding.UTF8.GetBytes(json)));

    private static Error Refused(string json)
    {
        Result<CountryPackageManifest> manifest =
            CountryPackageManifestJson.Read(Encoding.UTF8.GetBytes(json));

        manifest.IsFailure.ShouldBeTrue("the manifest should have been refused but was accepted");
        return manifest.Error;
    }
}
