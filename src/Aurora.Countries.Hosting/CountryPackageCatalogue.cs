using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;

namespace Aurora.Countries.Hosting;

/// <summary>Why one directory under the packages root is not an available package.</summary>
/// <param name="Directory">The directory.</param>
/// <param name="Reason">What the host found wrong with it.</param>
public sealed record PackageRejection(string Directory, Error Reason);

/// <summary>
/// What packages this deployment has available, read at startup (ADR-0008 §3.3).
/// </summary>
/// <remarks>
/// <para>
/// Discovery reads metadata and verifies signatures. It loads nothing into an executable context:
/// a package is only loaded when a tenant actually has it installed, so a deployment carrying ten
/// packages runs the code of none of them until somebody installs one.
/// </para>
/// <para>
/// A directory the host cannot use is <b>recorded</b>, not skipped silently. A package that quietly
/// fails to appear is the kind of thing nobody notices until a tenant asks why their GST return is
/// missing, so <see cref="Rejected"/> is part of the result and
/// <see cref="DirectoriesInspected"/> says how many directories were looked at — a catalogue with
/// nothing in it and nothing rejected has found an empty directory, which is a different problem
/// from a package that failed to verify.
/// </para>
/// </remarks>
public sealed class CountryPackageCatalogue
{
    private CountryPackageCatalogue(
        int directoriesInspected,
        IReadOnlyList<InspectedPackage> available,
        IReadOnlyList<PackageRejection> rejected)
    {
        DirectoriesInspected = directoriesInspected;
        Available = available;
        Rejected = rejected;
    }

    /// <summary>How many candidate directories were inspected.</summary>
    public int DirectoriesInspected { get; }

    /// <summary>The packages this deployment can install.</summary>
    public IReadOnlyList<InspectedPackage> Available { get; }

    /// <summary>The directories it cannot, and why.</summary>
    public IReadOnlyList<PackageRejection> Rejected { get; }

    /// <summary>
    /// Reads every package under <c>&lt;packages root&gt;/&lt;id&gt;/&lt;version&gt;/</c>.
    /// </summary>
    public static Result<CountryPackageCatalogue> Scan(CountryPackageHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Directory.Exists(options.PackagesDirectory))
        {
            return HostingErrors.HostConfiguration(
                $"The packages directory '{options.PackagesDirectory}' does not exist. A deployment " +
                $"with no packages is legitimate, but the directory it would find them in is not " +
                $"optional — its absence is a packaging mistake, not an empty catalogue.");
        }

        CountryPackageLoader loader = new(options);
        List<InspectedPackage> available = [];
        List<PackageRejection> rejected = [];
        int inspected = 0;

        foreach (string packageDirectory in Directory.EnumerateDirectories(options.PackagesDirectory))
        {
            foreach (string versionDirectory in Directory.EnumerateDirectories(packageDirectory))
            {
                inspected++;

                Result<InspectedPackage> package = loader.Inspect(versionDirectory);
                if (package.IsFailure)
                {
                    rejected.Add(new PackageRejection(versionDirectory, package.Error));
                    continue;
                }

                available.Add(package.Value);
            }
        }

        return Result.Success(new CountryPackageCatalogue(inspected, available, rejected));
    }

    /// <summary>Every available version of <paramref name="id"/>, in catalogue order.</summary>
    public IReadOnlyList<CountryPackageManifest> VersionsOf(PackageId id) =>
        [.. Available.Where(package => package.Manifest.Id == id).Select(package => package.Manifest)];

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Available.Count} package(s) available, {Rejected.Count} rejected, " +
        $"{DirectoriesInspected} directory/directories inspected";
}
