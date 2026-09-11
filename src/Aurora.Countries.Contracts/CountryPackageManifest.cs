using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts;

/// <summary>
/// What a Country Package says about itself: identity, version, the core contract range it works
/// against, its jurisdiction, what it contributes and what it owns (ADR-0008 §3.2).
/// </summary>
/// <remarks>
/// <para>
/// The manifest ships as <c>package.manifest.json</c> embedded in the package assembly, and the host
/// reads it with <c>MetadataLoadContext</c> — metadata only, no code executed — so compatibility,
/// dependencies and the signature are all decided <b>before</b> a line of package code runs.
/// </para>
/// <para>
/// Everything here is a claim by the package. Two of those claims are checked rather than believed:
/// <see cref="DeclaredTrust"/> is compared against the trust a signature actually establishes, and
/// <see cref="CoreContractRange"/> is enforced at install and again at every core upgrade. The rest
/// are validated for shape on the way in, which is why this type has no public constructor.
/// </para>
/// </remarks>
public sealed class CountryPackageManifest : IEquatable<CountryPackageManifest>
{
    private CountryPackageManifest(
        PackageId id,
        PackageKey key,
        string displayName,
        PackageVersion version,
        string coreContractRange,
        Jurisdiction jurisdiction,
        IReadOnlySet<CountryPackageCapability> capabilities,
        IReadOnlyList<PackageDependency> dependsOn,
        IReadOnlyList<PackageId> conflictsWith,
        bool localeOnly,
        string publisher,
        PackageTrustLevel declaredTrust)
    {
        Id = id;
        Key = key;
        DisplayName = displayName;
        Version = version;
        CoreContractRange = coreContractRange;
        Jurisdiction = jurisdiction;
        Capabilities = capabilities;
        DependsOn = dependsOn;
        ConflictsWith = conflictsWith;
        LocaleOnly = localeOnly;
        Publisher = publisher;
        DeclaredTrust = declaredTrust;
    }

    /// <summary>The package's stable identity, such as <c>aurora.country.nz</c>.</summary>
    public PackageId Id { get; }

    /// <summary>The package's short key, and through it the one schema it owns.</summary>
    public PackageKey Key { get; }

    /// <summary>The package's name as an operator sees it, such as <c>New Zealand</c>.</summary>
    public string DisplayName { get; }

    /// <summary>The package's own SemVer version.</summary>
    public PackageVersion Version { get; }

    /// <summary>
    /// The range of core contract versions this package works against, in NuGet range syntax and
    /// bounded at both ends, such as <c>[2.0.0, 3.0.0)</c>. The host refuses an open-ended range: a
    /// package cannot know it works against a MAJOR core contract that did not exist when it was
    /// built.
    /// </summary>
    /// <remarks>
    /// This is extension point 10 of ADR-0008 §7, and the one that makes the other nine survivable:
    /// it is checked at install and again for every tenant at every core upgrade, so core and
    /// package can never meet at runtime in a combination nobody declared to work.
    /// </remarks>
    public string CoreContractRange { get; }

    /// <summary>The jurisdiction this package speaks for.</summary>
    public Jurisdiction Jurisdiction { get; }

    /// <summary>
    /// What this package contributes. The installer resolves each one to a working implementation
    /// and refuses the package if any cannot be resolved.
    /// </summary>
    public IReadOnlySet<CountryPackageCapability> Capabilities { get; }

    /// <summary>Packages that must already be installed, and the ranges this one works with.</summary>
    public IReadOnlyList<PackageDependency> DependsOn { get; }

    /// <summary>Packages that may not be installed alongside this one.</summary>
    public IReadOnlyList<PackageId> ConflictsWith { get; }

    /// <summary>
    /// Whether this package ships locale content only — translations and formats, no fiscal logic.
    /// </summary>
    /// <remarks>
    /// Business Central ships translation through a separate XLIFF pipeline from its fiscal
    /// localization, and the research took that as evidence the two concerns are separable even
    /// though products usually fuse them. A locale-only package is how a jurisdiction gets a
    /// language without inheriting somebody's tax rules, so the check below is exact: a locale-only
    /// package declares <see cref="CountryPackageCapability.LocalePack"/> and nothing else.
    /// </remarks>
    public bool LocaleOnly { get; }

    /// <summary>Who published the package.</summary>
    public string Publisher { get; }

    /// <summary>
    /// The trust level the package <i>claims</i>. Never the trust level it has: only signature
    /// verification establishes that, and the host refuses a package claiming more than it proved.
    /// </summary>
    public PackageTrustLevel DeclaredTrust { get; }

    /// <summary>
    /// The schema this package owns in the tenant database — derived from <see cref="Key"/>, and the
    /// only schema its migrations may name (ADR-0008 §4.1 R1).
    /// </summary>
    public string SchemaName => Key.SchemaName;

    /// <summary>
    /// Builds a validated manifest. Every rule that fails names the manifest field it failed on.
    /// </summary>
    /// <remarks>
    /// Called by the host after parsing JSON, and available to a package that would rather state its
    /// manifest in code — but the two must agree, which the package contract test proves by
    /// comparing this value with the embedded resource.
    /// </remarks>
    public static Result<CountryPackageManifest> Create(
        PackageId id,
        PackageKey key,
        string? displayName,
        PackageVersion version,
        string? coreContractRange,
        string? declaredSchema,
        Jurisdiction jurisdiction,
        IReadOnlyCollection<CountryPackageCapability> capabilities,
        IReadOnlyList<PackageDependency> dependsOn,
        IReadOnlyList<PackageId> conflictsWith,
        bool localeOnly,
        string? publisher,
        PackageTrustLevel declaredTrust)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(dependsOn);
        ArgumentNullException.ThrowIfNull(conflictsWith);
        ArgumentNullException.ThrowIfNull(jurisdiction);

        if (!id.IsSpecified)
        {
            return PackageManifestErrors.Invalid("id", "A package id is required.");
        }

        if (!key.IsSpecified)
        {
            return PackageManifestErrors.Invalid("key", "A package key is required.");
        }

        if (!version.IsSpecified)
        {
            return PackageManifestErrors.Invalid("version", "A package version is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return PackageManifestErrors.Invalid("displayName", "A display name is required.");
        }

        if (string.IsNullOrWhiteSpace(coreContractRange))
        {
            return PackageManifestErrors.Invalid(
                "coreContractRange",
                "A package must state the core contract versions it works against. A package with no " +
                "range is a package that will one day load against a core it has never been run " +
                "with, which is the failure this field exists to prevent.");
        }

        if (string.IsNullOrWhiteSpace(publisher))
        {
            return PackageManifestErrors.Invalid("publisher", "A publisher is required.");
        }

        if (!Enum.IsDefined(declaredTrust))
        {
            return PackageManifestErrors.Invalid("trust", $"'{declaredTrust}' is not a trust level.");
        }

        if (!string.Equals(declaredSchema, key.SchemaName, StringComparison.Ordinal))
        {
            return PackageManifestErrors.Invalid(
                "schema",
                $"A package with key '{key}' owns schema '{key.SchemaName}', but the manifest claims " +
                $"'{declaredSchema}'. The schema is derived from the key and the two must agree, so " +
                $"that no package can name a schema that is not its own.");
        }

        if (capabilities.Count == 0)
        {
            return PackageManifestErrors.Invalid(
                "capabilities",
                "A package that contributes nothing is not a package.");
        }

        foreach (CountryPackageCapability capability in capabilities)
        {
            if (!Enum.IsDefined(capability))
            {
                return PackageManifestErrors.Invalid(
                    "capabilities",
                    $"'{capability}' is not a capability this core contract version declares.");
            }
        }

        HashSet<CountryPackageCapability> declared = [.. capabilities];

        if (localeOnly
            && (declared.Count != 1 || !declared.Contains(CountryPackageCapability.LocalePack)))
        {
            return PackageManifestErrors.Invalid(
                "localeOnly",
                $"A locale-only package declares exactly [{CountryPackageCapability.LocalePack}]; " +
                $"this one declares [{string.Join(", ", declared.Order())}].");
        }

        Result duplicateDependency = NoDuplicates(
            dependsOn.Select(dependency => dependency.Id),
            "dependsOn",
            id);
        if (duplicateDependency.IsFailure)
        {
            return duplicateDependency.Error;
        }

        Result duplicateConflict = NoDuplicates(conflictsWith, "conflictsWith", id);
        if (duplicateConflict.IsFailure)
        {
            return duplicateConflict.Error;
        }

        PackageId[] both = [.. dependsOn.Select(dependency => dependency.Id).Intersect(conflictsWith)];
        if (both.Length > 0)
        {
            return PackageManifestErrors.Invalid(
                "conflictsWith",
                $"[{string.Join(", ", both)}] is both depended on and conflicted with. An install " +
                $"that must both have and not have a package can never succeed.");
        }

        return Result.Success(new CountryPackageManifest(
            id,
            key,
            displayName,
            version,
            coreContractRange,
            jurisdiction,
            declared,
            [.. dependsOn],
            [.. conflictsWith],
            localeOnly,
            publisher,
            declaredTrust));
    }

    /// <inheritdoc/>
    public bool Equals(CountryPackageManifest? other) =>
        other is not null
        && Id == other.Id
        && Key == other.Key
        && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
        && Version == other.Version
        && string.Equals(CoreContractRange, other.CoreContractRange, StringComparison.Ordinal)
        && Jurisdiction.Equals(other.Jurisdiction)
        && Capabilities.SetEquals(other.Capabilities)
        && DependsOn.SequenceEqual(other.DependsOn)
        && ConflictsWith.SequenceEqual(other.ConflictsWith)
        && LocaleOnly == other.LocaleOnly
        && string.Equals(Publisher, other.Publisher, StringComparison.Ordinal)
        && DeclaredTrust == other.DeclaredTrust;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as CountryPackageManifest);

    /// <inheritdoc/>
    /// <remarks>
    /// Written out rather than left to a record, because the collections above compare by value and
    /// a record would compare them by reference — which would make a manifest parsed twice unequal
    /// to itself, and quietly break the contract test that a package's stated manifest matches its
    /// embedded one.
    /// </remarks>
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Id);
        hash.Add(Version);
        hash.Add(CoreContractRange, StringComparer.Ordinal);
        hash.Add(Capabilities.Count);
        hash.Add(DependsOn.Count);
        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Id} {Version} (core contract {CoreContractRange})";

    private static Result NoDuplicates(IEnumerable<PackageId> ids, string field, PackageId self)
    {
        HashSet<PackageId> seen = [];
        foreach (PackageId id in ids)
        {
            if (id == self)
            {
                return PackageManifestErrors.Invalid(field, $"A package may not name itself ({self}).");
            }

            if (!seen.Add(id))
            {
                return PackageManifestErrors.Invalid(field, $"'{id}' is named more than once.");
            }
        }

        return Result.Success();
    }
}
