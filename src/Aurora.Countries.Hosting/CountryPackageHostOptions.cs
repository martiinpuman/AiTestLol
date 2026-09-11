using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;

namespace Aurora.Countries.Hosting;

/// <summary>
/// A public key the platform trusts to sign Country Packages, pinned by thumbprint
/// (ADR-0008 §9.3).
/// </summary>
/// <remarks>
/// The thumbprint is supplied by configuration and checked against the key, rather than computed
/// from it and trusted. A configuration that carries only a key trusts whatever key is in it; one
/// that carries a key <i>and</i> the thumbprint it must have will not silently start trusting a
/// different key when somebody edits the wrong environment variable.
/// </remarks>
public sealed class TrustedPackageKey
{
    private readonly byte[] _subjectPublicKeyInfo;

    private TrustedPackageKey(string thumbprint, PackageTrustLevel level, byte[] subjectPublicKeyInfo)
    {
        Thumbprint = thumbprint;
        Level = level;
        _subjectPublicKeyInfo = subjectPublicKeyInfo;
    }

    /// <summary>The SHA-256 of the key's SubjectPublicKeyInfo, lowercase hex.</summary>
    public string Thumbprint { get; }

    /// <summary>What signing with this key establishes.</summary>
    public PackageTrustLevel Level { get; }

    /// <summary>
    /// Reads a trusted key, refusing one that is not ECDSA on P-256, or whose thumbprint is not the
    /// one configuration pinned.
    /// </summary>
    /// <param name="expectedThumbprint">The thumbprint configuration says this key must have.</param>
    /// <param name="level">
    /// What signing with it establishes. <see cref="PackageTrustLevel.Unsigned"/> is refused: it is
    /// the absence of a signature, so a key that establishes it is a contradiction.
    /// </param>
    /// <param name="subjectPublicKeyInfo">The public key, DER-encoded as SubjectPublicKeyInfo.</param>
    public static Result<TrustedPackageKey> Create(
        string? expectedThumbprint,
        PackageTrustLevel level,
        byte[]? subjectPublicKeyInfo)
    {
        if (string.IsNullOrWhiteSpace(expectedThumbprint))
        {
            return HostingErrors.HostConfiguration("A trusted package key must be pinned by thumbprint.");
        }

        if (subjectPublicKeyInfo is null || subjectPublicKeyInfo.Length == 0)
        {
            return HostingErrors.HostConfiguration(
                $"The trusted key pinned as '{expectedThumbprint}' carries no key material.");
        }

        if (level is not (PackageTrustLevel.FirstParty or PackageTrustLevel.Partner))
        {
            return HostingErrors.HostConfiguration(
                $"A trusted key cannot establish {level}: that is the absence of a signature, not " +
                $"something a key can prove.");
        }

        using ECDsa key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out int read);
            if (read != subjectPublicKeyInfo.Length)
            {
                return HostingErrors.HostConfiguration(
                    $"The trusted key pinned as '{expectedThumbprint}' has {subjectPublicKeyInfo.Length - read} " +
                    $"trailing byte(s) after the key.");
            }
        }
        catch (CryptographicException failure)
        {
            return HostingErrors.HostConfiguration(
                $"The trusted key pinned as '{expectedThumbprint}' is not an ECDSA public key: " +
                $"{failure.Message}");
        }

        if (key.KeySize != PackageSignature.KeySizeInBits)
        {
            return HostingErrors.HostConfiguration(
                $"The trusted key pinned as '{expectedThumbprint}' is a {key.KeySize}-bit key; " +
                $"package signing is ECDSA P-256 ({PackageSignature.KeySizeInBits}-bit) and nothing " +
                $"else. Accepting a second curve would mean accepting the weakest one on the list.");
        }

        string actual = Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo));
        if (!string.Equals(actual, expectedThumbprint.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return HostingErrors.HostConfiguration(
                $"The trusted key configured as '{expectedThumbprint}' actually has thumbprint " +
                $"'{actual}'. The pin exists so that replacing the key material has to be a " +
                $"deliberate, two-part edit.");
        }

        return Result.Success(new TrustedPackageKey(actual, level, [.. subjectPublicKeyInfo]));
    }

    /// <summary>A verifier over this key. The caller disposes it.</summary>
    internal ECDsa OpenVerifier()
    {
        ECDsa key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(_subjectPublicKeyInfo, out _);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}

/// <summary>
/// How the host is configured to find and trust Country Packages.
/// </summary>
/// <remarks>
/// Built through <see cref="Create"/>, which refuses a configuration that cannot be right — most
/// importantly, one that allows unsigned packages outside Development. ADR-0008 §9.3 asks for that
/// to be asserted rather than commented, "because a flag that only a comment prevents from reaching
/// production will reach production".
/// </remarks>
public sealed class CountryPackageHostOptions
{
    /// <summary>The environment name under which unsigned packages may be allowed, and no other.</summary>
    public const string DevelopmentEnvironmentName = "Development";

    private CountryPackageHostOptions(
        string packagesDirectory,
        string environmentName,
        bool allowUnsigned,
        IReadOnlyList<TrustedPackageKey> trustedKeys)
    {
        PackagesDirectory = packagesDirectory;
        EnvironmentName = environmentName;
        AllowUnsigned = allowUnsigned;
        TrustedKeys = trustedKeys;
    }

    /// <summary>Where packages are discovered, one directory per package version.</summary>
    public string PackagesDirectory { get; }

    /// <summary>The environment this host is running as.</summary>
    public string EnvironmentName { get; }

    /// <summary>Whether a package with no valid signature may be loaded.</summary>
    public bool AllowUnsigned { get; }

    /// <summary>The keys the platform trusts, and what each establishes.</summary>
    public IReadOnlyList<TrustedPackageKey> TrustedKeys { get; }

    /// <summary>Whether this host is running as Development.</summary>
    public bool IsDevelopment =>
        string.Equals(EnvironmentName, DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a host configuration, refusing one that would load code it cannot vouch for.
    /// </summary>
    public static Result<CountryPackageHostOptions> Create(
        string? packagesDirectory,
        string? environmentName,
        bool allowUnsigned,
        IReadOnlyList<TrustedPackageKey>? trustedKeys)
    {
        if (string.IsNullOrWhiteSpace(packagesDirectory))
        {
            return HostingErrors.HostConfiguration("The packages directory is required.");
        }

        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return HostingErrors.HostConfiguration(
                "The environment name is required: whether unsigned packages may be loaded depends " +
                "on it, so a host that does not know which environment it is cannot decide.");
        }

        IReadOnlyList<TrustedPackageKey> keys = trustedKeys ?? [];

        bool isDevelopment = string.Equals(
            environmentName,
            DevelopmentEnvironmentName,
            StringComparison.OrdinalIgnoreCase);

        if (allowUnsigned && !isDevelopment)
        {
            return HostingErrors.HostConfiguration(
                $"Packages:AllowUnsigned is set in the '{environmentName}' environment. Loading " +
                $"unsigned code is a Development convenience and the host will not honour the flag " +
                $"anywhere else — it refuses to start rather than run with it (ADR-0008 §9.3).");
        }

        if (!allowUnsigned && keys.Count == 0)
        {
            return HostingErrors.HostConfiguration(
                "No trusted package keys are configured and unsigned packages are not allowed, so no " +
                "package could ever be loaded. That is a configuration mistake, not a lockdown.");
        }

        string[] duplicates =
        [
            .. keys.GroupBy(key => key.Thumbprint, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
        ];

        return duplicates.Length > 0
            ? HostingErrors.HostConfiguration(
                $"Trusted key thumbprint(s) [{string.Join(", ", duplicates)}] are configured more " +
                $"than once, with possibly different trust levels.")
            : Result.Success(new CountryPackageHostOptions(
                packagesDirectory,
                environmentName,
                allowUnsigned,
                [.. keys]));
    }
}
