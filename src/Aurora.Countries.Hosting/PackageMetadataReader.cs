using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;

namespace Aurora.Countries.Hosting;

/// <summary>
/// Reads everything the host needs to decide about a package <b>without running any of its code</b>
/// (ADR-0008 §3.2, §9.2).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MetadataLoadContext"/> reads an assembly the way a decompiler does: types, references
/// and embedded resources, with no type initialisers, no module initialisers and no entry point. So
/// compatibility, trust and the referenced-assembly rule are all settled before the package is given
/// a thread — which matters because once it does have one it has the whole process (ADR-0008 §9.4).
/// </para>
/// <para>
/// The package assembly is found by asking which assembly in the directory carries the manifest
/// resource, rather than by a naming convention. A convention is a rule a package can get wrong;
/// carrying the manifest is what makes an assembly the package.
/// </para>
/// </remarks>
public sealed class PackageMetadataReader
{
    /// <summary>
    /// Reads the manifest and the referenced assemblies of the package in
    /// <paramref name="directory"/>.
    /// </summary>
    public Result<PackageMetadata> Read(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            return HostingErrors.Malformed($"'{directory}' is not a directory.");
        }

        string[] candidates = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);
        if (candidates.Length == 0)
        {
            return HostingErrors.Malformed($"'{directory}' holds no assembly.");
        }

        PathAssemblyResolver resolver = new([.. candidates, .. FrameworkAndContractAssemblies()]);
        using MetadataLoadContext metadata = new(resolver);

        List<string> carryingManifest = [];
        List<PackageMetadata> read = [];
        List<Error> failures = [];

        foreach (string candidate in candidates)
        {
            Assembly assembly;
            try
            {
                assembly = metadata.LoadFromAssemblyPath(candidate);
            }
            catch (BadImageFormatException)
            {
                // A native library shipped beside the managed ones. Not a package assembly.
                continue;
            }

            byte[]? manifestBytes = ReadManifestResource(assembly);
            if (manifestBytes is null)
            {
                continue;
            }

            carryingManifest.Add(candidate);

            Result<CountryPackageManifest> manifest = CountryPackageManifestJson.Read(manifestBytes);
            if (manifest.IsFailure)
            {
                failures.Add(manifest.Error);
                continue;
            }

            read.Add(new PackageMetadata(
                candidate,
                manifest.Value,
                manifestBytes,
                [.. assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty)]));
        }

        if (carryingManifest.Count == 0)
        {
            return HostingErrors.Malformed(
                $"No assembly in '{directory}' embeds a '{CountryPackageManifestJson.ResourceName}' " +
                $"resource, so none of them is a Country Package. {candidates.Length} assembly/" +
                $"assemblies were inspected.");
        }

        if (carryingManifest.Count > 1)
        {
            return HostingErrors.Malformed(
                $"'{directory}' holds {carryingManifest.Count} assemblies embedding a manifest " +
                $"([{string.Join(", ", carryingManifest.Select(Path.GetFileName))}]). A package " +
                $"directory holds exactly one package.");
        }

        return failures.Count > 0 ? failures[0] : Result.Success(read[0]);
    }

    private static byte[]? ReadManifestResource(Assembly assembly)
    {
        if (!assembly.GetManifestResourceNames()
                .Contains(CountryPackageManifestJson.ResourceName, StringComparer.Ordinal))
        {
            return null;
        }

        using Stream? resource = assembly.GetManifestResourceStream(CountryPackageManifestJson.ResourceName);
        if (resource is null)
        {
            return null;
        }

        using MemoryStream buffer = new();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// The assemblies a package's metadata may reference: the framework it runs on, and the core
    /// assemblies it is allowed to see.
    /// </summary>
    /// <remarks>
    /// Taken from this process's own framework directory, so metadata is resolved against the
    /// framework the package will actually run on rather than whatever reference assemblies happen
    /// to be on the machine.
    /// </remarks>
    private static IEnumerable<string> FrameworkAndContractAssemblies()
    {
        string frameworkDirectory =
            Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException(
                "The runtime assembly has no location, so metadata cannot be resolved against the " +
                "framework. A single-file or bundled host needs a different resolver.");

        foreach (string assembly in Directory.GetFiles(frameworkDirectory, "*.dll"))
        {
            yield return assembly;
        }

        foreach (Assembly core in new[] { typeof(CoreContract).Assembly, typeof(Money).Assembly })
        {
            if (!string.IsNullOrEmpty(core.Location))
            {
                yield return core.Location;
            }
        }
    }
}

/// <summary>What metadata-only inspection of a package assembly found.</summary>
public sealed class PackageMetadata
{
    internal PackageMetadata(
        string assemblyPath,
        CountryPackageManifest manifest,
        byte[] manifestBytes,
        IReadOnlyList<string> referencedAssemblies)
    {
        AssemblyPath = assemblyPath;
        Manifest = manifest;
        ManifestBytes = manifestBytes;
        ReferencedAssemblies = referencedAssemblies;
    }

    /// <summary>The package assembly — the one carrying the manifest resource.</summary>
    public string AssemblyPath { get; }

    /// <summary>The manifest, parsed and validated.</summary>
    public CountryPackageManifest Manifest { get; }

    /// <summary>
    /// The manifest's exact bytes. The signature covers these, so they are kept rather than
    /// re-serialised: a re-serialised manifest is a different byte sequence and would never verify.
    /// </summary>
    public byte[] ManifestBytes { get; }

    /// <summary>Every assembly the package assembly references, by simple name.</summary>
    public IReadOnlyList<string> ReferencedAssemblies { get; }

    /// <summary>
    /// The references this package is not allowed to hold (<see cref="PackageAssemblyReferenceRule"/>).
    /// Empty when it is within its rights.
    /// </summary>
    public IReadOnlyList<string> ForbiddenReferences =>
        [.. ReferencedAssemblies.Where(reference =>
            !string.IsNullOrEmpty(reference) && !PackageAssemblyReferenceRule.IsAllowed(reference))];
}
