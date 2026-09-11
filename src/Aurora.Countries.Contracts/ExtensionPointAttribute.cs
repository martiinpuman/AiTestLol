using System;

namespace Aurora.Countries.Contracts;

/// <summary>
/// Marks an interface as the contract for one <see cref="CountryPackageCapability"/>.
/// </summary>
/// <remarks>
/// The attribute is the map, rather than a hand-written table somewhere else: a table can fall out
/// of step with the interfaces it names and nothing notices until a package declares a capability
/// that resolves to the wrong type. <see cref="CountryPackageCapabilities"/> builds its lookup by
/// reading these attributes and refuses to initialise unless every capability has exactly one.
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class ExtensionPointAttribute : Attribute
{
    /// <summary>Marks this interface as the contract for <paramref name="capability"/>.</summary>
    public ExtensionPointAttribute(CountryPackageCapability capability) => Capability = capability;

    /// <summary>The capability this interface implements.</summary>
    public CountryPackageCapability Capability { get; }
}
