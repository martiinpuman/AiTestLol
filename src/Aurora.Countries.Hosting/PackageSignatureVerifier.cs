using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;

namespace Aurora.Countries.Hosting;

/// <summary>What a package's signature established, and which key established it.</summary>
/// <param name="Level">The trust the signature proves.</param>
/// <param name="KeyThumbprint">
/// The key that verified, or <see langword="null"/> when nothing did. Recorded because
/// "which key admitted this package" is the first question asked after a key is compromised.
/// </param>
public readonly record struct PackageTrust(PackageTrustLevel Level, string? KeyThumbprint)
{
    /// <summary>Nothing verified.</summary>
    public static PackageTrust None { get; } = new(PackageTrustLevel.Unsigned, null);

    /// <inheritdoc/>
    public override string ToString() =>
        KeyThumbprint is null ? Level.ToString() : $"{Level} (key {KeyThumbprint})";
}

/// <summary>
/// Step 2 of the install saga: establish what a package is, cryptographically (ADR-0008 §5.1, §9.3).
/// </summary>
/// <remarks>
/// <para>
/// The manifest's <c>trust</c> field takes no part in this. A manifest is bytes inside the package,
/// so a package claiming <see cref="PackageTrustLevel.FirstParty"/> proves nothing; trust is
/// whatever a signature over a pinned key proves, and a package that claims more than it proved is
/// refused with both values named. That refusal is the interesting one: it is the shape an attempt
/// to pass off a package as first-party takes.
/// </para>
/// <para>
/// <b>What a failed verification does:</b> nothing loads. Verification runs before the assembly is
/// opened in any executable context, so a package that fails it has never been given a thread, a
/// type initialiser or a module initialiser. The install saga stops at step 2, the tenant's database
/// is untouched, and the operator gets a Problem Details response naming the package, the directory
/// and which check failed. There is no partial install to roll back, which is the whole reason
/// verification comes before schema creation rather than after it.
/// </para>
/// </remarks>
public sealed class PackageSignatureVerifier
{
    private readonly CountryPackageHostOptions _options;

    /// <summary>A verifier over the keys and flags in <paramref name="options"/>.</summary>
    public PackageSignatureVerifier(CountryPackageHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Establishes the trust of the package described by <paramref name="metadata"/>, or refuses it.
    /// </summary>
    public Result<PackageTrust> Verify(PackageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        string directory =
            Path.GetDirectoryName(metadata.AssemblyPath)
            ?? throw new InvalidOperationException(
                $"'{metadata.AssemblyPath}' has no directory, so its signature cannot be located.");

        string signaturePath = Path.Combine(directory, PackageSignature.FileName);
        PackageId id = metadata.Manifest.Id;

        if (!File.Exists(signaturePath))
        {
            return Unsigned(
                metadata,
                $"Package '{id}' {metadata.Manifest.Version} carries no " +
                $"'{PackageSignature.FileName}'.");
        }

        byte[] signature = File.ReadAllBytes(signaturePath);
        if (signature.Length == 0)
        {
            return HostingErrors.Untrusted(
                $"Package '{id}' {metadata.Manifest.Version} has an empty " +
                $"'{PackageSignature.FileName}'. An empty signature is a broken build, not an " +
                $"unsigned package, so it is refused rather than treated as one.");
        }

        byte[] content = PackageSignature.ContentToSign(metadata.AssemblyPath, metadata.ManifestBytes);
        PackageTrust established = PackageTrust.None;

        // At most one key can match: a signature is produced by one private key, and a thumbprint
        // may appear only once in the trust store, so there is no "highest level wins" rule to
        // write - and writing one would be a branch no configuration could ever reach.
        foreach (TrustedPackageKey trusted in _options.TrustedKeys)
        {
            using ECDsa key = trusted.OpenVerifier();
            if (key.VerifyData(content, signature, PackageSignature.HashAlgorithm))
            {
                established = new PackageTrust(trusted.Level, trusted.Thumbprint);
                break;
            }
        }

        if (established.Level == PackageTrustLevel.Unsigned)
        {
            return Unsigned(
                metadata,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Package '{id}' {metadata.Manifest.Version} carries a signature, but it verifies " +
                    $"against none of the {_options.TrustedKeys.Count} trusted key(s). Either the " +
                    $"package was changed after it was signed, or it was signed by a key this " +
                    $"platform does not trust."));
        }

        return established.Level < metadata.Manifest.DeclaredTrust
            ? HostingErrors.Untrusted(
                $"Package '{id}' {metadata.Manifest.Version} declares trust " +
                $"{metadata.Manifest.DeclaredTrust} but its signature only establishes " +
                $"{established.Level}. A manifest is part of the package, so what it claims about " +
                $"itself is never what admits it.")
            : Result.Success(established);
    }

    private Result<PackageTrust> Unsigned(PackageMetadata metadata, string why)
    {
        if (!_options.AllowUnsigned)
        {
            return HostingErrors.Untrusted(
                $"{why} Unsigned packages are refused. Packages:AllowUnsigned exists for local " +
                $"development and the host refuses to start with it set anywhere else.");
        }

        return metadata.Manifest.DeclaredTrust != PackageTrustLevel.Unsigned
            ? HostingErrors.Untrusted(
                $"{why} It declares trust {metadata.Manifest.DeclaredTrust}. Even with " +
                $"Packages:AllowUnsigned set, a package may not claim a trust level it has not " +
                $"proved — that claim is what an operator reads in the package list.")
            : Result.Success(PackageTrust.None);
    }
}
