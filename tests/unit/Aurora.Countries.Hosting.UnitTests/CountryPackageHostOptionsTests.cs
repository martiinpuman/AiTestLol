using System;
using System.Security.Cryptography;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Hosting.UnitTests;

/// <summary>
/// The configuration test ADR-0008 §9.3 asks for by name, "because a flag that only a comment
/// prevents from reaching production will reach production".
/// </summary>
public sealed class CountryPackageHostOptionsTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("production")]
    public void Allowing_unsigned_packages_outside_development_is_refused(string environment)
    {
        Result<CountryPackageHostOptions> options = CountryPackageHostOptions.Create(
            "/srv/aurora/packages",
            environment,
            allowUnsigned: true,
            trustedKeys: []);

        options.IsFailure.ShouldBeTrue();
        options.Error.Code.ShouldBe(HostingErrors.HostConfigurationCode);
        options.Error.Description.ShouldContain(environment);
        options.Error.Description.ShouldContain("AllowUnsigned");
    }

    [Fact]
    public void Allowing_unsigned_packages_in_development_is_the_one_case_that_is_honoured()
    {
        CountryPackageHostOptions options = Ok(CountryPackageHostOptions.Create(
            "/srv/aurora/packages",
            CountryPackageHostOptions.DevelopmentEnvironmentName,
            allowUnsigned: true,
            trustedKeys: []));

        options.AllowUnsigned.ShouldBeTrue();
        options.IsDevelopment.ShouldBeTrue();
    }

    /// <summary>
    /// No keys and no unsigned packages means nothing could ever load. That is a configuration
    /// mistake rather than a lockdown, and a host that started anyway would look healthy and install
    /// nothing.
    /// </summary>
    [Fact]
    public void A_host_that_could_never_load_anything_is_refused()
    {
        CountryPackageHostOptions.Create("/srv/aurora/packages", "Production", false, [])
            .IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "Production")]
    [InlineData("", "Production")]
    [InlineData("/srv/aurora/packages", null)]
    [InlineData("/srv/aurora/packages", " ")]
    public void A_host_that_does_not_know_where_or_what_it_is_is_refused(string? directory, string? environment) =>
        CountryPackageHostOptions.Create(directory, environment, false, []).IsFailure.ShouldBeTrue();

    [Fact]
    public void The_same_key_configured_twice_is_refused()
    {
        using TestTrustStore store = new();
        (_, TrustedPackageKey key) = store.Add(PackageTrustLevel.FirstParty);

        Result<CountryPackageHostOptions> options = CountryPackageHostOptions.Create(
            "/srv/aurora/packages",
            "Production",
            allowUnsigned: false,
            trustedKeys: [key, key]);

        options.IsFailure.ShouldBeTrue();
        options.Error.Description.ShouldContain(key.Thumbprint);
    }

    /// <summary>
    /// The pin is the point: configuration carries the key and the thumbprint it must have, so
    /// replacing the key material is a deliberate two-part edit rather than one environment variable.
    /// </summary>
    [Fact]
    public void A_key_whose_thumbprint_is_not_the_one_pinned_is_refused()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Result<TrustedPackageKey> trusted = TrustedPackageKey.Create(
            "0000000000000000000000000000000000000000000000000000000000000000",
            PackageTrustLevel.FirstParty,
            key.ExportSubjectPublicKeyInfo());

        trusted.IsFailure.ShouldBeTrue();
        trusted.Error.Description.ShouldContain("actually has thumbprint");
    }

    /// <summary>
    /// One curve. Accepting a second would mean accepting the weakest one on the list, and an
    /// attacker picks the list.
    /// </summary>
    [Fact]
    public void A_key_on_another_curve_is_refused()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        string thumbprint = Convert.ToHexStringLower(SHA256.HashData(publicKey));

        Result<TrustedPackageKey> trusted =
            TrustedPackageKey.Create(thumbprint, PackageTrustLevel.FirstParty, publicKey);

        trusted.IsFailure.ShouldBeTrue();
        trusted.Error.Description.ShouldContain("P-256");
    }

    /// <summary>
    /// The bit length is not the curve. secp256k1 is a 256-bit curve too, and a check on
    /// <c>KeySize</c> alone accepted a key on it; the curve's OID is what says P-256.
    /// </summary>
    [Fact]
    public void A_256_bit_key_on_a_curve_other_than_P_256_is_refused()
    {
        using ECDsa key = ECDsa.Create(ECCurve.CreateFromValue(Secp256k1Oid));
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();

        key.KeySize.ShouldBe(
            PackageSignature.KeySizeInBits,
            "this case only bites if the key size alone cannot tell the curves apart");

        Result<TrustedPackageKey> trusted = TrustedPackageKey.Create(
            Convert.ToHexStringLower(SHA256.HashData(publicKey)),
            PackageTrustLevel.FirstParty,
            publicKey);

        trusted.IsFailure.ShouldBeTrue();
        trusted.Error.Description.ShouldContain("P-256");
        trusted.Error.Description.ShouldContain(Secp256k1Oid);
    }

    private const string Secp256k1Oid = "1.3.132.0.10";

    [Fact]
    public void A_key_that_is_not_a_key_is_refused() =>
        TrustedPackageKey.Create("abcd", PackageTrustLevel.FirstParty, [1, 2, 3])
            .IsFailure.ShouldBeTrue();

    /// <summary>
    /// <see cref="PackageTrustLevel.Unsigned"/> is the absence of a signature, so a key that
    /// establishes it is a contradiction.
    /// </summary>
    [Fact]
    public void A_key_cannot_establish_that_a_package_is_unsigned()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();

        TrustedPackageKey.Create(
                Convert.ToHexStringLower(SHA256.HashData(publicKey)),
                PackageTrustLevel.Unsigned,
                publicKey)
            .IsFailure.ShouldBeTrue();
    }

    private static T Ok<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Expected success, got: {result.Error}");
}
