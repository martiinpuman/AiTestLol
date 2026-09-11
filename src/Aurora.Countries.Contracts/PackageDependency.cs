using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts;

/// <summary>
/// One entry of a manifest's <c>dependsOn</c>: another package that must be installed, and the
/// version range this package works with (ADR-0008 §3.2).
/// </summary>
/// <param name="Id">The package depended upon, such as <c>aurora.region.anz</c>.</param>
/// <param name="VersionRange">
/// A NuGet version range such as <c>[1.0.0, 2.0.0)</c>. Held as text here and parsed by the host,
/// for the reason given on <see cref="PackageVersion"/>: every package carries this assembly's
/// dependencies, so this assembly carries none.
/// </param>
public readonly record struct PackageDependency(PackageId Id, string VersionRange)
{
    /// <summary>Reads a dependency entry, rejecting a malformed id or an empty range.</summary>
    public static Result<PackageDependency> Create(string? id, string? versionRange)
    {
        Result<PackageId> packageId = PackageId.Create(id);
        if (packageId.IsFailure)
        {
            return packageId.Error;
        }

        return string.IsNullOrWhiteSpace(versionRange)
            ? PackageManifestErrors.Invalid(
                "dependsOn.range",
                $"'{packageId.Value}' is depended on without a version range. A dependency with no " +
                $"range is the drift this contract exists to prevent: state one, such as " +
                $"'[1.0.0, 2.0.0)'.")
            : Result.Success(new PackageDependency(packageId.Value, versionRange));
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Id} {VersionRange}";
}
