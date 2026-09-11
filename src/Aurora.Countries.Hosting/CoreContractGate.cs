using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;
using NuGet.Versioning;

namespace Aurora.Countries.Hosting;

/// <summary>
/// Step 3 of the install saga: does this package work against the core contract that is running?
/// (ADR-0008 §5.1, §5.2.)
/// </summary>
/// <remarks>
/// <para>
/// This is the gate Odoo does not have. Its core and its localization modules are versioned
/// semi-independently with no automated compatibility check, and the result is a named, recurring
/// support issue — <i>"Error while loading the localization. You should probably update your
/// localization app first"</i> — thrown at runtime, after the upgrade, naming neither version.
/// </para>
/// <para>
/// So this check runs at install and again for every tenant at every core upgrade, and its message
/// names <b>both</b> versions and what the package would need. An incompatibility that says which
/// versions disagree is a five-minute fix; one that does not is the thread that made Odoo's a
/// recurring issue.
/// </para>
/// </remarks>
public static class CoreContractGate
{
    /// <summary>
    /// Whether <paramref name="manifest"/> admits <paramref name="coreContractVersion"/>, with a
    /// failure that names both versions when it does not.
    /// </summary>
    /// <param name="manifest">The package's manifest, carrying its <c>coreContractRange</c>.</param>
    /// <param name="coreContractVersion">
    /// The running core contract version — <see cref="CoreContract.Version"/> in the host. Taken as
    /// a parameter rather than read here, because the release-build fleet compatibility report of
    /// ADR-0008 §5.2 asks this same question about a <i>target</i> version that is not the one the
    /// process is running.
    /// </param>
    public static Result Check(CountryPackageManifest manifest, string coreContractVersion) =>
        Check(manifest, coreContractVersion, availableVersions: []);

    /// <summary>
    /// The same check, additionally naming which other versions of the same package would work —
    /// which is the sentence an operator actually needs.
    /// </summary>
    /// <param name="manifest">The package's manifest.</param>
    /// <param name="coreContractVersion">The core contract version being checked against.</param>
    /// <param name="availableVersions">
    /// Other versions of the same package the catalogue holds, with each one's declared range.
    /// </param>
    public static Result Check(
        CountryPackageManifest manifest,
        string coreContractVersion,
        IReadOnlyList<CountryPackageManifest> availableVersions)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(availableVersions);

        if (!NuGetVersion.TryParse(coreContractVersion, out NuGetVersion? core))
        {
            return HostingErrors.HostConfiguration(
                $"The running core contract version '{coreContractVersion}' is not a version. It comes " +
                $"from the <Version> of {CoreContract.AssemblyName}, so this is a build problem in " +
                $"core, not a problem with package '{manifest.Id}'.");
        }

        if (!VersionRange.TryParse(manifest.CoreContractRange, out VersionRange? range))
        {
            return HostingErrors.IncompatibleCoreContract(
                $"Package '{manifest.Id}' {manifest.Version} declares coreContractRange " +
                $"'{manifest.CoreContractRange}', which is not a NuGet version range. Use a bounded " +
                $"range such as '{BoundedExample(core)}'.");
        }

        if (!IsBounded(range))
        {
            // NuGet reads a bare "1.0.0" as "1.0.0 or anything later", which here would mean
            // "compatible with every MAJOR core contract that has not been written yet" — including
            // the one that removes a member this package calls. No package can know that, so the
            // claim is refused rather than believed, whatever the running version is today.
            return HostingErrors.IncompatibleCoreContract(
                $"Package '{manifest.Id}' {manifest.Version} declares coreContractRange " +
                $"'{manifest.CoreContractRange}', which is open at {OpenEnds(range)}. A package " +
                $"cannot be compatible with a MAJOR core contract that did not exist when it was " +
                $"built, so the range must be bounded at both ends, such as '{BoundedExample(core)}'.");
        }

        if (range.Satisfies(core))
        {
            return Result.Success();
        }

        string remedy = Remedy(manifest, core, availableVersions);

        return HostingErrors.IncompatibleCoreContract(
            $"Package '{manifest.Id}' {manifest.Version} needs core contract " +
            $"{manifest.CoreContractRange}, but the running core contract version is " +
            $"{core.ToNormalizedString()}. {remedy}");
    }

    private static string Remedy(
        CountryPackageManifest manifest,
        NuGetVersion core,
        IReadOnlyList<CountryPackageManifest> availableVersions)
    {
        List<CountryPackageManifest> compatible =
        [
            .. availableVersions.Where(candidate =>
                candidate.Id == manifest.Id
                && VersionRange.TryParse(candidate.CoreContractRange, out VersionRange? candidateRange)
                && IsBounded(candidateRange)
                && candidateRange.Satisfies(core)),
        ];

        if (compatible.Count == 0)
        {
            return availableVersions.Count == 0
                ? $"No other version of '{manifest.Id}' was offered to this check, so no compatible " +
                  $"version can be named."
                : $"None of the {availableVersions.Count} version(s) of '{manifest.Id}' in the " +
                  $"catalogue works with core contract {core.ToNormalizedString()}; the package needs " +
                  $"a release that does.";
        }

        IOrderedEnumerable<string> versions = compatible
            .Select(candidate => candidate.Version.Value)
            .Order(StringComparer.Ordinal);

        return $"Install version(s) [{string.Join(", ", versions)}] of '{manifest.Id}' instead.";
    }

    private static bool IsBounded(VersionRange range) => range.HasLowerBound && range.HasUpperBound;

    private static string OpenEnds(VersionRange range) =>
        (range.HasLowerBound, range.HasUpperBound) switch
        {
            (false, false) => "both ends",
            (false, true) => "the lower end",
            _ => "the upper end",
        };

    private static string BoundedExample(NuGetVersion core) =>
        $"[{core.Major}.0.0, {core.Major + 1}.0.0)";
}
