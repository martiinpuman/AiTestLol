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

        string? curve = CurveOidOf(key);
        if (!string.Equals(curve, PackageSignature.CurveOid, StringComparison.Ordinal))
        {
            return HostingErrors.HostConfiguration(
                $"The trusted key pinned as '{expectedThumbprint}' is a {key.KeySize}-bit key on curve " +
                $"'{curve ?? "<unnamed>"}'; package signing is ECDSA on P-256 " +
                $"({PackageSignature.CurveOid}) and nothing else. Accepting a second curve would mean " +
                $"accepting the weakest one on the list, and a key size alone does not name the curve.");
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

    /// <summary>
    /// The OID of the named curve <paramref name="key"/> is on, or <see langword="null"/> for a
    /// curve given by explicit parameters — which is refused too: the one accepted curve has a name.
    /// </summary>
    private static string? CurveOidOf(ECDsa key)
    {
        ECCurve curve = key.ExportParameters(includePrivateParameters: false).Curve;
        return curve.IsNamed ? curve.Oid.Value : null;
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
/// How the host is configured to find and trust Country Packages, and whether it routes tenants.
/// </summary>
/// <remarks>
/// <para>
/// Built through <see cref="Create"/>, which refuses a configuration that cannot be right — most
/// importantly, one that allows unsigned packages outside Development. ADR-0008 §9.3 asks for that
/// to be asserted rather than commented, "because a flag that only a comment prevents from reaching
/// production will reach production".
/// </para>
/// <para>
/// <see cref="RoutesTenants"/> is the admission floor of ADR-0033 §5.2. A loaded package runs
/// inside the process, and the process is the tenancy trust boundary (ADR-0033 §5.1): there is no
/// in-process privilege boundary in .NET against loaded managed code, so a package a tenant-routing
/// process loads can reach every tenant that process can. The only control is deciding what is
/// allowed to execute at all, and for a tenant-routing host that is <b>first-party packages
/// only</b> — where "package" is the manifest-bearing assembly the signature covers; a dependency
/// it ships beside itself is loaded on the strength of that admission, unsigned (see
/// <see cref="CountryPackageLoader"/>). This type says which kind of host it is;
/// <see cref="CountryPackageLoader"/> enforces the floor at load, and <see cref="Create"/> refuses
/// a configuration that contradicts it.
/// </para>
/// </remarks>
public sealed class CountryPackageHostOptions
{
    /// <summary>The environment name under which unsigned packages may be allowed, and no other.</summary>
    public const string DevelopmentEnvironmentName = "Development";

    /// <summary>
    /// The least trust a package's signature must establish before a tenant-routing host loads it
    /// (ADR-0033 §5.2).
    /// </summary>
    public const PackageTrustLevel TenantRoutingAdmissionFloor = PackageTrustLevel.FirstParty;

    private CountryPackageHostOptions(
        string packagesDirectory,
        string environmentName,
        bool allowUnsigned,
        IReadOnlyList<TrustedPackageKey> trustedKeys,
        bool routesTenants)
    {
        PackagesDirectory = packagesDirectory;
        EnvironmentName = environmentName;
        AllowUnsigned = allowUnsigned;
        TrustedKeys = trustedKeys;
        RoutesTenants = routesTenants;
    }

    /// <summary>Where packages are discovered, one directory per package version.</summary>
    public string PackagesDirectory { get; }

    /// <summary>The environment this host is running as.</summary>
    public string EnvironmentName { get; }

    /// <summary>Whether a package with no valid signature may be loaded.</summary>
    public bool AllowUnsigned { get; }

    /// <summary>The keys the platform trusts, and what each establishes.</summary>
    public IReadOnlyList<TrustedPackageKey> TrustedKeys { get; }

    /// <summary>
    /// Whether this host can route to tenant databases. When it can, a loaded package is inside the
    /// tenancy trust boundary, and only a package whose signature establishes
    /// <see cref="PackageTrustLevel.FirstParty"/> is loaded (ADR-0033 §5.2).
    /// </summary>
    public bool RoutesTenants { get; }

    /// <summary>
    /// The least trust a package's signature must establish before this host loads it, over and
    /// above the signature rules of ADR-0008 §9.3: <see cref="PackageTrustLevel.FirstParty"/> on a
    /// host that routes tenants, and no additional floor (<see cref="PackageTrustLevel.Unsigned"/>)
    /// on one that does not.
    /// </summary>
    /// <remarks>
    /// The floor decides what is <i>loaded</i>. A package below it is still inspected and listed —
    /// ADR-0033 §5.2 narrows where a <see cref="PackageTrustLevel.Partner"/> key takes effect; it
    /// does not remove the level.
    /// </remarks>
    public PackageTrustLevel AdmissionFloor =>
        RoutesTenants ? TenantRoutingAdmissionFloor : PackageTrustLevel.Unsigned;

    /// <summary>Whether this host is running as Development.</summary>
    public bool IsDevelopment =>
        string.Equals(EnvironmentName, DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a host configuration, refusing one that would load code it cannot vouch for.
    /// </summary>
    /// <param name="packagesDirectory">Where packages are discovered.</param>
    /// <param name="environmentName">The environment the host runs as.</param>
    /// <param name="allowUnsigned">Whether packages with no valid signature may be loaded.</param>
    /// <param name="trustedKeys">The keys the platform trusts.</param>
    /// <param name="routesTenants">
    /// Whether this host can route to tenant databases. There is no default: a host that does not
    /// say what it is cannot be given the right floor, and the safe answer differs by host.
    /// </param>
    public static Result<CountryPackageHostOptions> Create(
        string? packagesDirectory,
        string? environmentName,
        bool allowUnsigned,
        IReadOnlyList<TrustedPackageKey>? trustedKeys,
        bool routesTenants)
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

        // Checked before the environment rule, and without regard to it: a tenant-routing host has
        // no environment in which a package below the floor is outside the tenancy trust boundary.
        if (routesTenants && allowUnsigned)
        {
            return HostingErrors.HostConfiguration(
                $"Packages:AllowUnsigned is set on a host that routes tenants (environment " +
                $"'{environmentName}'). A tenant-routing process loads only packages whose " +
                $"manifest-bearing assembly is signed at {TenantRoutingAdmissionFloor}: a loaded package runs inside the " +
                $"process, and the process is the tenancy trust boundary, so it can reach every " +
                $"tenant this host can. The floor is not a setting — the host refuses to start " +
                $"rather than run with the flag, in Development as anywhere else (ADR-0033 §5.2).");
        }

        if (allowUnsigned && !isDevelopment)
        {
            return HostingErrors.HostConfiguration(
                $"Packages:AllowUnsigned is set in the '{environmentName}' environment. Loading " +
                $"unsigned code is a Development convenience and the host will not honour the flag " +
                $"anywhere else — it refuses to start rather than run with it (ADR-0008 §9.3).");
        }

        PackageTrustLevel floor = routesTenants ? TenantRoutingAdmissionFloor : PackageTrustLevel.Unsigned;

        if (!allowUnsigned && !keys.Any(key => key.Level >= floor))
        {
            return HostingErrors.HostConfiguration(
                routesTenants
                    ? $"This host routes tenants and loads only packages whose signature establishes " +
                      $"{floor}, but none of the {keys.Count} trusted key(s) configured establishes " +
                      $"it, so no package could ever be loaded. That is a configuration mistake, not " +
                      $"a lockdown (ADR-0033 §5.2)."
                    : "No trusted package keys are configured and unsigned packages are not allowed, " +
                      "so no package could ever be loaded. That is a configuration mistake, not a " +
                      "lockdown.");
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
                [.. keys],
                routesTenants));
    }
}
