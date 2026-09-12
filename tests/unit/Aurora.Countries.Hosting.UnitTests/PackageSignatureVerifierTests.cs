using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Hosting.UnitTests;

/// <summary>
/// ECDSA P-256 signature verification, and what happens when it fails (ADR-0008 §9.3).
/// </summary>
/// <remarks>
/// Keys are generated per test and never leave the process, so nothing here is a secret and nothing
/// is committed. The release pipeline holds the real private key; this suite only proves the
/// verifier does what the pipeline's signature is for.
/// </remarks>
public sealed class PackageSignatureVerifierTests
{
    /// <summary>
    /// The happy path, and the only one that ends with a package being loadable in production: a
    /// signature over the assembly and the manifest, verified against a pinned key.
    /// </summary>
    [Fact]
    public void A_package_signed_by_a_trusted_key_is_admitted_at_that_key_s_level()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(key);

        PackageTrust trust = Ok(Verify(package, trusted));

        trust.Level.ShouldBe(PackageTrustLevel.FirstParty);
        trust.KeyThumbprint.ShouldBe(trusted.Thumbprint);
    }

    /// <summary>
    /// The signature covers the assembly's bytes. A package changed after signing is a different
    /// package, and nothing about it loads.
    /// </summary>
    [Fact]
    public void A_package_changed_after_it_was_signed_is_refused()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(key);
        package.TamperWithTheAssembly();

        Result<PackageTrust> trust = Verify(package, trusted);

        trust.IsFailure.ShouldBeTrue();
        trust.Error.Code.ShouldBe(HostingErrors.UntrustedCode);
        trust.Error.Description.ShouldContain("changed after it was signed");
    }

    /// <summary>
    /// Signed by a real key, just not one this platform trusts — the partner-package case before an
    /// operator has added their key.
    /// </summary>
    [Fact]
    public void A_package_signed_by_a_key_the_platform_does_not_trust_is_refused()
    {
        using TestTrustStore store = new();
        (ECDsa stranger, _) = store.Add(PackageTrustLevel.FirstParty);
        (_, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(stranger);

        Verify(package, trusted).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_package_with_no_signature_is_refused_when_unsigned_packages_are_not_allowed()
    {
        using TestTrustStore store = new();
        (_, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();

        Result<PackageTrust> trust = Verify(package, trusted);

        trust.IsFailure.ShouldBeTrue();
        trust.Error.Description.ShouldContain("Unsigned packages are refused");
    }

    /// <summary>
    /// The Development convenience. It exists so a package can be run from a build output without a
    /// signing key, and the host refuses to start with it set anywhere else — which
    /// <see cref="CountryPackageHostOptionsTests"/> asserts.
    /// </summary>
    [Fact]
    public void An_unsigned_package_is_admitted_in_development_at_no_trust_at_all()
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();

        PackageTrust trust = Ok(Verify(package, trusted: null, allowUnsigned: true));

        trust.Level.ShouldBe(PackageTrustLevel.Unsigned);
        trust.KeyThumbprint.ShouldBeNull();
    }

    /// <summary>
    /// An empty signature file is a broken build, not an unsigned package, so it is refused even in
    /// Development rather than quietly treated as the absence of a signature.
    /// </summary>
    [Fact]
    public void An_empty_signature_file_is_refused_even_where_unsigned_is_allowed()
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.WriteSignature([]);

        Result<PackageTrust> trust = Verify(package, trusted: null, allowUnsigned: true);

        trust.IsFailure.ShouldBeTrue();
        trust.Error.Description.ShouldContain("broken build");
    }

    [Fact]
    public void A_signature_that_is_not_a_signature_is_refused()
    {
        using TestTrustStore store = new();
        (_, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.WriteSignature(Encoding.UTF8.GetBytes("not a signature"));

        Verify(package, trusted).IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// The manifest is inside the package, so what it claims about its own trust proves nothing.
    /// This is the shape an attempt to pass a package off as first-party takes, and the refusal
    /// names both the claim and what was actually established.
    /// </summary>
    [Fact]
    public void A_package_claiming_more_trust_than_it_proved_is_refused_naming_both()
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();
        PackageMetadata metadata = Ok(PackageMetadataReader.Read(package.Directory));

        PackageMetadata overclaiming = new(
            metadata.AssemblyPath,
            ManifestClaiming(PackageTrustLevel.FirstParty),
            metadata.ManifestBytes,
            metadata.ReferencedAssemblies);

        CountryPackageHostOptions options = Ok(CountryPackageHostOptions.Create(
            package.Root,
            CountryPackageHostOptions.DevelopmentEnvironmentName,
            allowUnsigned: true,
            trustedKeys: [],
            routesTenants: false));

        Result<PackageTrust> trust = new PackageSignatureVerifier(options).Verify(overclaiming);

        trust.IsFailure.ShouldBeTrue();
        trust.Error.Description.ShouldContain(nameof(PackageTrustLevel.FirstParty));
        trust.Error.Description.ShouldContain("has not proved");
    }

    /// <summary>
    /// A trust store holding a key that did not sign this package does not change the answer: the
    /// key that verified is the one recorded, which is the first thing asked after a key is
    /// compromised.
    /// </summary>
    [Fact]
    public void The_key_that_verified_is_the_one_recorded()
    {
        using TestTrustStore store = new();
        (ECDsa signer, TrustedPackageKey asFirstParty) = store.Add(PackageTrustLevel.FirstParty);
        (_, TrustedPackageKey bystander) = store.Add(PackageTrustLevel.Partner);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(signer);

        CountryPackageHostOptions options = Ok(CountryPackageHostOptions.Create(
            package.Root,
            "Production",
            allowUnsigned: false,
            trustedKeys: [bystander, asFirstParty],
            routesTenants: false));

        PackageTrust trust = Ok(new PackageSignatureVerifier(options).Verify(
            Ok(PackageMetadataReader.Read(package.Directory))));

        trust.Level.ShouldBe(PackageTrustLevel.FirstParty);
        trust.KeyThumbprint.ShouldBe(asFirstParty.Thumbprint);
        trust.ToString().ShouldContain(asFirstParty.Thumbprint);
    }

    /// <summary>
    /// The signature binds the assembly to <i>its own</i> manifest, so a signed assembly cannot be
    /// paired with somebody else's manifest — which is what would let a package claim a different
    /// id, key or schema than the one that was reviewed.
    /// </summary>
    [Fact]
    public void The_signed_content_covers_both_the_assembly_and_the_manifest()
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();

        byte[] withOwnManifest = PackageSignature.ContentToSign(
            package.AssemblyPath,
            package.ManifestBytes());
        byte[] withAnother = PackageSignature.ContentToSign(
            package.AssemblyPath,
            Encoding.UTF8.GetBytes("{\"id\":\"aurora.country.elsewhere\"}"));

        withOwnManifest.ShouldNotBe(withAnother);
        withOwnManifest.Length.ShouldBe(32 + package.ManifestBytes().Length);
    }

    private static Result<PackageTrust> Verify(
        PackageOnDisk package,
        TrustedPackageKey? trusted,
        bool allowUnsigned = false)
    {
        List<TrustedPackageKey> keys = trusted is null ? [] : [trusted];

        CountryPackageHostOptions options = Ok(CountryPackageHostOptions.Create(
            package.Root,
            allowUnsigned ? CountryPackageHostOptions.DevelopmentEnvironmentName : "Production",
            allowUnsigned,
            keys,
            routesTenants: false));

        return new PackageSignatureVerifier(options)
            .Verify(Ok(PackageMetadataReader.Read(package.Directory)));
    }

    private static CountryPackageManifest ManifestClaiming(PackageTrustLevel trust)
    {
        string json = $$"""
            {
              "id": "aurora.country.testland",
              "key": "tl",
              "displayName": "Testland",
              "version": "1.4.0",
              "coreContractRange": "[1.0.0, 2.0.0)",
              "jurisdiction": { "countryCode": "NZ", "defaultCurrency": "NZD", "locales": ["en-NZ"] },
              "capabilities": ["TaxRuleSet"],
              "schema": "pkg_tl",
              "localeOnly": false,
              "publisher": "Aurora",
              "trust": "{{trust}}"
            }
            """;

        return Ok(CountryPackageManifestJson.Read(Encoding.UTF8.GetBytes(json)));
    }

    private static T Ok<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Expected success, got: {result.Error}");
}
