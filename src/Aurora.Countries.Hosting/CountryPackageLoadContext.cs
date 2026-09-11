using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Aurora.Countries.Hosting;

/// <summary>
/// The load context one version of one Country Package runs in (ADR-0008 §9.2).
/// </summary>
/// <remarks>
/// <para>
/// Collectible, so deactivating a package can actually unload its code; one per
/// (package, version), so two packages can carry different versions of the same private dependency
/// without either one winning.
/// </para>
/// <para>
/// The rule that makes the whole thing work is in <see cref="Load"/>: the contract assembly and the
/// tier-0 assemblies it is expressed in are resolved from the <b>default</b> context, never from the
/// package's own directory. If a package loaded its own copy, the <c>ITaxRuleProvider</c> it
/// implements would be a different <see cref="Type"/> from the one the host asked for, and every
/// cast would fail with a message that reads like nonsense — two types with the same full name, one
/// not assignable to the other. This is the classic plugin bug and it is one line of code to cause.
/// </para>
/// <para>
/// An <see cref="AssemblyLoadContext"/> is <b>not</b> a security boundary (ADR-0008 §9.4). Package
/// code that runs here runs in-process with full trust: it can read any file this process can read
/// and open any socket. What this class provides is version isolation and unloadability. The
/// controls that actually decide whether hostile code runs are upstream of it — the signature, the
/// reference rule, and v1's "first-party packages only".
/// </para>
/// </remarks>
internal sealed class CountryPackageLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver? _resolver;
    private readonly string _directory;

    internal CountryPackageLoadContext(string name, string assemblyPath)
        : base(name, isCollectible: true)
    {
        _directory =
            Path.GetDirectoryName(assemblyPath)
            ?? throw new ArgumentException(
                $"'{assemblyPath}' has no directory to resolve dependencies from.",
                nameof(assemblyPath));

        _resolver = TryCreateResolver(assemblyPath);
    }

    /// <inheritdoc/>
    /// <exception cref="PackageReferenceRefusedException">
    /// The package asked for an Aurora assembly it may not reference. The reference check at
    /// metadata time already refuses such a package, so reaching here means the reference was made
    /// at runtime — by name, through reflection — and the answer is the same one.
    /// </exception>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? name = assemblyName.Name;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (PackageAssemblyReferenceRule.AllowedAuroraAssemblies.Contains(name))
        {
            // Null means "let the default context answer". That is the point: one contract assembly,
            // one Type identity, shared by the host and every package.
            return null;
        }

        // Case-insensitive, like the runtime's own binding: an ordinal check let
        // "AURORA.Countries.Hosting" fall through to the probe below, miss, and be answered by the
        // default context with the host's real assembly.
        if (name.StartsWith(PackageAssemblyReferenceRule.AuroraPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new PackageReferenceRefusedException(Name ?? "<unnamed>", name);
        }

        string? resolved = _resolver?.ResolveAssemblyToPath(assemblyName);
        if (resolved is not null)
        {
            return LoadFromAssemblyPath(resolved);
        }

        string probe = Path.Combine(_directory, name + ".dll");
        return File.Exists(probe) ? LoadFromAssemblyPath(probe) : null;
    }

    /// <inheritdoc/>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? resolved = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
        return resolved is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(resolved);
    }

    private static AssemblyDependencyResolver? TryCreateResolver(string assemblyPath)
    {
        try
        {
            return new AssemblyDependencyResolver(assemblyPath);
        }
        catch (InvalidOperationException)
        {
            // No .deps.json, or one this runtime cannot read. Probing the package directory still
            // works for the single-assembly packages v1 ships; a package with private dependencies
            // needs its .deps.json, and will fail to find them loudly rather than silently binding
            // to the host's copies.
            return null;
        }
    }
}

/// <summary>
/// A package asked for an Aurora assembly it is not allowed to reference (ADR-0008 §3.1).
/// </summary>
public sealed class PackageReferenceRefusedException : Exception
{
    /// <summary>The refusal, naming the package and the assembly it reached for.</summary>
    public PackageReferenceRefusedException(string loadContextName, string assemblyName)
        : base($"The Country Package loaded in '{loadContextName}' asked for '{assemblyName}'. A " +
               $"package may reference only {string.Join(", ", PackageAssemblyReferenceRule.AllowedAuroraAssemblies)} " +
               $"— core's internals are not part of the contract and are not reachable from a package.")
    {
        LoadContextName = loadContextName;
        AssemblyName = assemblyName;
    }

    /// <summary>Which package's load context asked.</summary>
    public string LoadContextName { get; } = string.Empty;

    /// <summary>Which assembly it asked for.</summary>
    public string AssemblyName { get; } = string.Empty;
}
