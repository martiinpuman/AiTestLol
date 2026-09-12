using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Aurora.Countries.Contracts;
using Aurora.SharedKernel;

namespace Aurora.Countries.Hosting;

/// <summary>
/// Turns a package directory into a running <see cref="ICountryPackage"/> — or refuses to, naming
/// which check said no (ADR-0008 §5.1 steps 2 and 3, §9).
/// </summary>
/// <remarks>
/// <para>
/// The order is the point. Metadata, signature, the admission floor and compatibility are all
/// settled while the package is still an inert file; only then is a load context created. A package
/// that fails any of them has never executed a line, so there is nothing to undo.
/// <c>PackageAdmissionFloorTests</c> holds <see cref="Load"/> to that by observing the hostile
/// fixture's module initialiser unset after a refusal (ADR-0033 §5.6 D1).
/// </para>
/// <para>
/// <b>What the floor is not.</b> It decides what executes; it does not confine what an admitted
/// package can do. A loaded package runs with the full permissions of the process and can reach
/// any tenant this process can (ADR-0033 §5.1, §5.4). Nothing in this class claims otherwise.
/// </para>
/// </remarks>
public sealed class CountryPackageLoader
{
    private readonly PackageSignatureVerifier _verifier;
    private readonly PackageTrustLevel _admissionFloor;
    private readonly string _coreContractVersion;

    /// <summary>A loader over <paramref name="options"/>, against the running core contract.</summary>
    public CountryPackageLoader(CountryPackageHostOptions options)
        : this(options, CoreContract.Version)
    {
    }

    /// <summary>
    /// A loader that checks against a stated core contract version rather than the running one.
    /// </summary>
    /// <remarks>
    /// The release-build fleet compatibility report of ADR-0008 §5.2 asks whether every package in
    /// the fleet works against the <i>target</i> core contract version — a version the process
    /// producing the report is not running. Taking it as a parameter is what lets that report exist
    /// without a second implementation of the gate.
    /// </remarks>
    public CountryPackageLoader(CountryPackageHostOptions options, string coreContractVersion)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(coreContractVersion);

        _verifier = new PackageSignatureVerifier(options);
        _admissionFloor = options.AdmissionFloor;
        _coreContractVersion = coreContractVersion;
    }

    /// <summary>
    /// Inspects the package in <paramref name="packageDirectory"/> without loading its code: the
    /// manifest, the reference rule and the signature.
    /// </summary>
    /// <remarks>
    /// This is what discovery runs over every directory at startup, and what an operator sees in the
    /// package list. Nothing here executes package code.
    /// </remarks>
    public Result<InspectedPackage> Inspect(string packageDirectory)
    {
        Result<PackageMetadata> metadata = PackageMetadataReader.Read(packageDirectory);
        if (metadata.IsFailure)
        {
            return metadata.Error;
        }

        IReadOnlyList<string> forbidden = metadata.Value.ForbiddenReferences;
        if (forbidden.Count > 0)
        {
            return HostingErrors.ForbiddenReference(
                $"Package '{metadata.Value.Manifest.Id}' {metadata.Value.Manifest.Version} references " +
                $"[{string.Join(", ", forbidden)}]. A package may reference only " +
                $"[{string.Join(", ", PackageAssemblyReferenceRule.AllowedAuroraAssemblies)}]; core's " +
                $"internals are not part of the contract, and a package built against them breaks on " +
                $"the first core change that does not reach the contract.");
        }

        Result<PackageTrust> trust = _verifier.Verify(metadata.Value);
        return trust.IsFailure
            ? trust.Error
            : Result.Success(new InspectedPackage(metadata.Value, trust.Value));
    }

    /// <summary>
    /// Inspects the package, holds it to this host's admission floor, checks it against the core
    /// contract version, and loads it into its own collectible load context.
    /// </summary>
    /// <param name="packageDirectory">The package's directory.</param>
    /// <param name="otherVersionsInCatalogue">
    /// Other versions of the same package the catalogue holds, so an incompatibility can name a
    /// version that would work. Optional; the refusal names both versions either way.
    /// </param>
    /// <remarks>
    /// The admission floor is applied here and not in <see cref="Inspect"/>: ADR-0033 §5.2 narrows
    /// where a trust level takes effect, so a package below the floor is still inspected and listed
    /// for an operator to see — and is not given a thread.
    /// </remarks>
    public Result<LoadedCountryPackage> Load(
        string packageDirectory,
        IReadOnlyList<CountryPackageManifest>? otherVersionsInCatalogue = null)
    {
        Result<InspectedPackage> inspected = Inspect(packageDirectory);
        if (inspected.IsFailure)
        {
            return inspected.Error;
        }

        CountryPackageManifest manifest = inspected.Value.Manifest;

        if (inspected.Value.Trust.Level < _admissionFloor)
        {
            return HostingErrors.BelowAdmissionFloor(
                $"Package '{manifest.Id}' {manifest.Version} is signed by a key that establishes " +
                $"{inspected.Value.Trust}, and this host routes tenants: it loads only packages whose " +
                $"signature establishes {_admissionFloor}. A loaded package runs inside the process, " +
                $"which is the tenancy trust boundary, so it could reach every tenant this host can " +
                $"(ADR-0033 §5.2). The package was inspected and is listed; it was not loaded, and " +
                $"none of its code has run.");
        }

        Result compatible = CoreContractGate.Check(
            manifest,
            _coreContractVersion,
            otherVersionsInCatalogue ?? []);
        if (compatible.IsFailure)
        {
            return compatible.Error;
        }

        return Activate(inspected.Value);
    }

    // Kept out of Load so that no local of Load can hold the context alive, which is what would make
    // the unload test pass for the wrong reason - or fail for one.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Result<LoadedCountryPackage> Activate(InspectedPackage inspected)
    {
        CountryPackageManifest manifest = inspected.Manifest;
        string contextName = $"{manifest.Id}@{manifest.Version}";
        CountryPackageLoadContext context = new(contextName, inspected.AssemblyPath);

        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(inspected.AssemblyPath);

            Result<ICountryPackage> entryPoint = Construct(assembly, manifest);
            if (entryPoint.IsFailure)
            {
                context.Unload();
                return entryPoint.Error;
            }

            if (!entryPoint.Value.Manifest.Equals(manifest))
            {
                context.Unload();
                return HostingErrors.LoadFailed(
                    $"Package '{manifest.Id}' {manifest.Version} returns a manifest from its code that " +
                    $"differs from its embedded '{CountryPackageManifestJson.ResourceName}'. The two " +
                    $"describe the same package and must agree, or the manifest the host validated is " +
                    $"not the one the code behaves as.");
            }

            return Result.Success(new LoadedCountryPackage(
                context,
                entryPoint.Value,
                manifest,
                inspected.Trust,
                inspected.AssemblyPath));
        }
        catch (Exception failure) when (failure is BadImageFormatException
                                            or FileLoadException
                                            or PackageReferenceRefusedException
                                            or TypeLoadException
                                            or ReflectionTypeLoadException)
        {
            context.Unload();

            // The runtime wraps whatever the load context's Load override threw in a
            // FileLoadException, so the refusal that actually matters is one or two levels down.
            // Reporting the wrapper would hide the only sentence that says what went wrong.
            PackageReferenceRefusedException? refused = Refusal(failure);

            return refused is null
                ? HostingErrors.LoadFailed(
                    $"Package '{manifest.Id}' {manifest.Version} did not load: {failure.Message}")
                : HostingErrors.ForbiddenReference(
                    $"Package '{manifest.Id}' {manifest.Version} reached for an assembly it may not " +
                    $"reference while loading. {refused.Message}");
        }
    }

    private static PackageReferenceRefusedException? Refusal(Exception? failure)
    {
        for (Exception? candidate = failure; candidate is not null; candidate = candidate.InnerException)
        {
            if (candidate is PackageReferenceRefusedException refused)
            {
                return refused;
            }
        }

        return null;
    }

    private static Result<ICountryPackage> Construct(Assembly assembly, CountryPackageManifest manifest)
    {
        Type[] entryPoints =
        [
            .. assembly.GetExportedTypes().Where(type =>
                type is { IsClass: true, IsAbstract: false }
                && typeof(ICountryPackage).IsAssignableFrom(type)),
        ];

        if (entryPoints.Length != 1)
        {
            return HostingErrors.LoadFailed(
                $"Package '{manifest.Id}' {manifest.Version} exposes {entryPoints.Length} public " +
                $"implementations of {nameof(ICountryPackage)} " +
                $"([{string.Join(", ", entryPoints.Select(type => type.FullName))}]). Exactly one is " +
                $"the entry point.");
        }

        Type entryPoint = entryPoints[0];
        if (entryPoint.GetConstructor(Type.EmptyTypes) is null)
        {
            return HostingErrors.LoadFailed(
                $"'{entryPoint.FullName}' has no public parameterless constructor. A package is " +
                $"constructed by the host during install, with no container and no tenant, so there " +
                $"is nothing to inject into one.");
        }

        try
        {
            return Activator.CreateInstance(entryPoint) is ICountryPackage package
                ? Result.Success(package)
                : HostingErrors.LoadFailed(
                    $"'{entryPoint.FullName}' did not construct as an {nameof(ICountryPackage)}.");
        }
        catch (TargetInvocationException failure)
        {
            return HostingErrors.LoadFailed(
                $"Constructing '{entryPoint.FullName}' threw {failure.InnerException?.GetType().Name}: " +
                $"{failure.InnerException?.Message}. A package's constructor builds objects and " +
                $"nothing else — it runs inside the install saga, where there is no tenant to reach.");
        }
    }
}

/// <summary>A package the host has read and vouched for, but has not run.</summary>
public sealed class InspectedPackage
{
    internal InspectedPackage(PackageMetadata metadata, PackageTrust trust)
    {
        Manifest = metadata.Manifest;
        AssemblyPath = metadata.AssemblyPath;
        ReferencedAssemblies = metadata.ReferencedAssemblies;
        Trust = trust;
    }

    /// <summary>What the package says about itself, validated.</summary>
    public CountryPackageManifest Manifest { get; }

    /// <summary>The package assembly.</summary>
    public string AssemblyPath { get; }

    /// <summary>What its signature established. Never what its manifest claimed.</summary>
    public PackageTrust Trust { get; }

    /// <summary>Every assembly it references, by simple name.</summary>
    public IReadOnlyList<string> ReferencedAssemblies { get; }
}

/// <summary>
/// A loaded Country Package, and the context it can be unloaded from.
/// </summary>
/// <remarks>
/// Disposing unloads the context, which is what makes deactivating a package a real operation rather
/// than a flag. Unloading is asynchronous by nature — the runtime collects the context once nothing
/// references anything in it — so <see cref="Dispose"/> asks, and the memory comes back at the next
/// collection that can see no live references.
/// </remarks>
public sealed class LoadedCountryPackage : IDisposable
{
    private AssemblyLoadContext? _context;

    internal LoadedCountryPackage(
        AssemblyLoadContext context,
        ICountryPackage package,
        CountryPackageManifest manifest,
        PackageTrust trust,
        string assemblyPath)
    {
        _context = context;
        Package = package;
        Manifest = manifest;
        Trust = trust;
        AssemblyPath = assemblyPath;
    }

    /// <summary>The package itself.</summary>
    public ICountryPackage Package { get; }

    /// <summary>Its manifest.</summary>
    public CountryPackageManifest Manifest { get; }

    /// <summary>What its signature established.</summary>
    public PackageTrust Trust { get; }

    /// <summary>The assembly that was loaded.</summary>
    public string AssemblyPath { get; }

    /// <summary>Whether <see cref="Dispose"/> has asked for the context to unload.</summary>
    public bool IsUnloadRequested => _context is null;

    /// <summary>
    /// A weak handle on the load context, for tests that prove the context is genuinely collectible.
    /// </summary>
    /// <remarks>
    /// Weak on purpose: a test that held a strong reference would keep alive the very thing it is
    /// asserting has gone.
    /// </remarks>
    internal WeakReference LoadContextHandle => new(_context, trackResurrection: false);

    /// <summary>Asks the runtime to unload this package's code.</summary>
    public void Dispose()
    {
        AssemblyLoadContext? context = _context;
        if (context is null)
        {
            return;
        }

        _context = null;
        context.Unload();
    }
}
