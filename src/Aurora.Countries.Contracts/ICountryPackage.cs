using System;

namespace Aurora.Countries.Contracts;

/// <summary>
/// The single type a Country Package assembly exports to the host: what it is, and what it can do.
/// </summary>
/// <remarks>
/// <para>
/// The host finds exactly one public implementation of this interface in a package assembly,
/// constructs it through its parameterless constructor, and asks it for its extensions. That is the
/// whole entry point. A package is not handed a service provider, a <c>DbContext</c>, a connection
/// string or a tenant: everything it needs to do its work arrives as an argument to an extension
/// point method, so a package cannot reach a tenant the caller did not open (ADR-0008 §9.4 is
/// honest that this is a design convention and not a sandbox — package code runs in-process with
/// full trust, and the real control is that v1 loads first-party packages only).
/// </para>
/// <para>
/// Construction must do nothing but build objects. It happens during install, inside the install
/// saga's transaction, with no tenant context available.
/// </para>
/// </remarks>
public interface ICountryPackage
{
    /// <summary>
    /// What this package says about itself — and must equal its embedded
    /// <c>package.manifest.json</c>, which the package contract test asserts.
    /// </summary>
    CountryPackageManifest Manifest { get; }

    /// <summary>
    /// The implementation of <paramref name="contractType"/> this package provides, or
    /// <see langword="null"/> if it provides none.
    /// </summary>
    /// <remarks>
    /// Typed as <see cref="object"/> because one extension point is generic in the document it
    /// profiles, so the set of contract types a package can fill is not closed at compile time. Use
    /// <see cref="CountryPackageExtensions.GetExtension{TContract}"/> rather than calling this
    /// directly; it does the cast, and the cast is safe precisely because the host resolves this
    /// assembly from the default load context for both sides (ADR-0008 §9.2).
    /// </remarks>
    /// <param name="contractType">
    /// An extension-point interface — closed, if the extension point is generic.
    /// </param>
    object? GetExtension(Type contractType);
}
