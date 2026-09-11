using System.Linq;
using System.Security.Cryptography;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Hosting.UnitTests;

/// <summary>
/// Reading a package's manifest and references with <c>MetadataLoadContext</c> — metadata only, no
/// code executed (ADR-0008 §3.2).
/// </summary>
public sealed class PackageMetadataReaderTests
{
    /// <summary>
    /// The acceptance criterion: the manifest comes out of the assembly without the assembly being
    /// run. The proof that nothing ran is structural — <c>MetadataLoadContext</c> has no execution
    /// engine — and observable here: the package type's constructor throws if its manifest is
    /// missing, and the type is never constructed on this path.
    /// </summary>
    [Fact]
    public void The_manifest_is_read_out_of_the_assembly_without_running_it()
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();

        PackageMetadata metadata = Ok(PackageMetadataReader.Read(package.Directory));

        metadata.Manifest.Id.Value.ShouldBe("aurora.country.testland");
        metadata.Manifest.Key.Value.ShouldBe("tl");
        metadata.Manifest.SchemaName.ShouldBe("pkg_tl");
        metadata.Manifest.Version.Value.ShouldBe("1.4.0");
        metadata.Manifest.Capabilities.ShouldBe(
            [CountryPackageCapability.TaxRuleSet, CountryPackageCapability.IdentifierValidator],
            ignoreOrder: true);
        metadata.AssemblyPath.ShouldEndWith(PackageOnDisk.PackageAssemblyFileName);
    }

    /// <summary>
    /// The manifest bytes are kept exactly as embedded, because the signature covers them. A
    /// re-serialised manifest is a different byte sequence and would never verify.
    /// </summary>
    [Fact]
    public void The_manifest_bytes_are_kept_exactly_as_embedded()
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();

        PackageMetadata metadata = Ok(PackageMetadataReader.Read(package.Directory));

        metadata.ManifestBytes.Length.ShouldBeGreaterThan(0);
        CountryPackageManifestJson.Read(metadata.ManifestBytes).IsSuccess.ShouldBeTrue();
        metadata.ManifestBytes.ShouldBe(package.ManifestBytes());
    }

    /// <summary>
    /// ADR-0008 §3.1 allows a package to reference the contract and the two tier-0 assemblies it is
    /// expressed in, and nothing else of ours. The check reads the references from metadata, so it
    /// covers a package this repository did not build — which a build-time fitness test cannot.
    /// </summary>
    [Fact]
    public void The_assemblies_a_package_references_are_read_and_checked()
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();

        PackageMetadata metadata = Ok(PackageMetadataReader.Read(package.Directory));

        metadata.ReferencedAssemblies.ShouldContain(CoreContract.AssemblyName);
        metadata.ReferencedAssemblies.ShouldContain("Aurora.SharedKernel");
        metadata.ForbiddenReferences.ShouldBeEmpty();

        metadata.ReferencedAssemblies
            .Where(reference => reference.StartsWith(
                PackageAssemblyReferenceRule.AuroraPrefix,
                System.StringComparison.Ordinal))
            .ShouldAllBe(reference => PackageAssemblyReferenceRule.AllowedAuroraAssemblies.Contains(reference));
    }

    [Fact]
    public void A_directory_with_no_assembly_is_not_a_package()
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.RemoveTheAssembly();

        Result<PackageMetadata> metadata = PackageMetadataReader.Read(package.Directory);

        metadata.IsFailure.ShouldBeTrue();
        metadata.Error.Code.ShouldBe(HostingErrors.MalformedPackageCode);
    }

    /// <summary>
    /// The package assembly is found by asking which assembly carries the manifest, not by a naming
    /// convention — so two of them is genuinely ambiguous, and refused rather than guessed at.
    /// </summary>
    [Fact]
    public void A_directory_holding_two_packages_is_refused()
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.AddSecondPackageAssembly();

        Result<PackageMetadata> metadata = PackageMetadataReader.Read(package.Directory);

        metadata.IsFailure.ShouldBeTrue();
        metadata.Error.Description.ShouldContain("exactly one");
    }

    [Fact]
    public void A_directory_that_does_not_exist_is_refused() =>
        PackageMetadataReader.Read(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-package-dir"))
            .IsFailure.ShouldBeTrue();

    /// <summary>
    /// A file that is not a managed assembly at all — a native library shipped beside the managed
    /// ones — is stepped over rather than failing the read.
    /// </summary>
    [Fact]
    public void A_native_library_beside_the_package_is_stepped_over()
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();
        System.IO.File.WriteAllBytes(
            System.IO.Path.Combine(package.Directory, "libnative.dll"),
            RandomNumberGenerator.GetBytes(512));

        Ok(PackageMetadataReader.Read(package.Directory)).Manifest.Key.Value.ShouldBe("tl");
    }

    private static T Ok<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new System.InvalidOperationException($"Expected success, got: {result.Error}");
}
