using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;

namespace Aurora.Countries.Hosting.UnitTests;

/// <summary>
/// A Country Package laid out on disk the way a deployed one is, so the host tests read and load a
/// real file rather than a stand-in.
/// </summary>
/// <remarks>
/// Every mechanism under test — a manifest embedded as an assembly resource, a detached signature
/// over the assembly's bytes, an assembly in its own load context — is about a file. A test double
/// would already be in the test's own load context, with no manifest resource and nothing to sign,
/// and would prove none of it.
/// </remarks>
internal sealed class PackageOnDisk : IDisposable
{
    /// <summary>The Testland package assembly built beside these tests, by project reference.</summary>
    internal const string PackageAssemblyFileName = "Aurora.Countries.TestPackage.dll";

    /// <summary>
    /// The hostile fixture of ADR-0033 §5.6, built beside these tests the same way. Its module
    /// initialiser records that it ran; <c>PackageAdmissionFloorTests</c> reads that record.
    /// </summary>
    internal const string HostileAssemblyFileName = "Aurora.Countries.HostilePackage.dll";

    private PackageOnDisk(string root, string versionDirectory, string assemblyFileName)
    {
        Root = root;
        Directory = versionDirectory;
        AssemblyFileName = assemblyFileName;
    }

    /// <summary>A packages root, of the shape the catalogue scans.</summary>
    internal string Root { get; }

    /// <summary>This package's own version directory.</summary>
    internal string Directory { get; }

    /// <summary>The file name of the package assembly inside it.</summary>
    internal string AssemblyFileName { get; }

    /// <summary>The package assembly inside it.</summary>
    internal string AssemblyPath => Path.Combine(Directory, AssemblyFileName);

    /// <summary>
    /// Lays the Testland package out under a fresh temporary root as
    /// <c>&lt;root&gt;/&lt;id&gt;/&lt;version&gt;/</c>.
    /// </summary>
    internal static PackageOnDisk Deploy(string id = "aurora.country.testland", string version = "1.4.0") =>
        Deploy(PackageAssemblyFileName, id, version);

    /// <summary>Lays the hostile fixture package out the same way.</summary>
    internal static PackageOnDisk DeployHostile() =>
        Deploy(HostileAssemblyFileName, "aurora.country.hostile", "1.0.0");

    private static PackageOnDisk Deploy(string assemblyFileName, string id, string version)
    {
        string root = Path.Combine(Path.GetTempPath(), "aurora-packages-" + Guid.NewGuid().ToString("N"));
        string versionDirectory = Path.Combine(root, id, version);
        System.IO.Directory.CreateDirectory(versionDirectory);

        string source = Path.Combine(AppContext.BaseDirectory, assemblyFileName);
        if (!File.Exists(source))
        {
            throw new InvalidOperationException(
                $"'{assemblyFileName}' is not beside the tests. It arrives by project reference " +
                $"from its project under tests/fixtures/.");
        }

        File.Copy(source, Path.Combine(versionDirectory, assemblyFileName));

        return new PackageOnDisk(root, versionDirectory, assemblyFileName);
    }

    /// <summary>Signs the package with <paramref name="key"/>, as the release pipeline would.</summary>
    internal void SignWith(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);

        byte[] content = PackageSignature.ContentToSign(AssemblyPath, ManifestBytes());
        File.WriteAllBytes(
            Path.Combine(Directory, PackageSignature.FileName),
            key.SignData(content, PackageSignature.HashAlgorithm));
    }

    /// <summary>Writes a signature file that is not a signature of anything.</summary>
    internal void WriteSignature(byte[] bytes) =>
        File.WriteAllBytes(Path.Combine(Directory, PackageSignature.FileName), bytes);

    /// <summary>
    /// Changes one byte of the assembly, well past the PE header, the way a tampered package would
    /// differ from the one that was signed.
    /// </summary>
    internal void TamperWithTheAssembly()
    {
        byte[] assembly = File.ReadAllBytes(AssemblyPath);
        assembly[^64] ^= 0xFF;
        File.WriteAllBytes(AssemblyPath, assembly);
    }

    /// <summary>Adds a second manifest-bearing assembly to the same directory.</summary>
    internal void AddSecondPackageAssembly() =>
        File.Copy(AssemblyPath, Path.Combine(Directory, "Aurora.Countries.TestPackage.Copy.dll"));

    /// <summary>Removes the package assembly, leaving the directory shaped wrongly.</summary>
    internal void RemoveTheAssembly() => File.Delete(AssemblyPath);

    /// <summary>The manifest bytes as the package embeds them.</summary>
    internal byte[] ManifestBytes()
    {
        Result<PackageMetadata> metadata = PackageMetadataReader.Read(Directory);

        return metadata.IsSuccess
            ? metadata.Value.ManifestBytes
            : throw new InvalidOperationException($"The deployed package is unreadable: {metadata.Error}");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A loaded assembly can hold the file on some platforms. A temporary directory that
            // outlives the test is untidy, not wrong, and failing the test over it would turn a
            // passing run into a red one for no reason anybody could act on.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A trust store built around freshly generated keys, so no secret is ever committed.</summary>
internal sealed class TestTrustStore : IDisposable
{
    private readonly List<ECDsa> _keys = [];

    /// <summary>Generates a key and returns it, trusted at <paramref name="level"/>.</summary>
    internal (ECDsa Key, TrustedPackageKey Trusted) Add(PackageTrustLevel level)
    {
        ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _keys.Add(key);

        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        string thumbprint = Convert.ToHexStringLower(SHA256.HashData(publicKey));

        Result<TrustedPackageKey> trusted = TrustedPackageKey.Create(thumbprint, level, publicKey);
        return trusted.IsSuccess
            ? (key, trusted.Value)
            : throw new InvalidOperationException($"The generated key was refused: {trusted.Error}");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (ECDsa key in _keys)
        {
            key.Dispose();
        }
    }
}
