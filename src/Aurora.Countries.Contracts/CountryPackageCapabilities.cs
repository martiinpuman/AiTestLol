using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Aurora.Countries.Contracts;

/// <summary>
/// The map from a declared <see cref="CountryPackageCapability"/> to the interface a package must
/// implement to fill it.
/// </summary>
/// <remarks>
/// Built by reading <see cref="ExtensionPointAttribute"/> off the interfaces in this assembly, so
/// the map cannot drift from the interfaces. Initialisation fails loudly if a capability has no
/// interface or more than one — that is a mistake in core, made before any package exists, and it
/// should stop the host rather than surface later as a package that cannot be installed.
/// </remarks>
public static class CountryPackageCapabilities
{
    private static readonly ReadOnlyDictionary<CountryPackageCapability, Type> ByCapability = Build();

    /// <summary>Every capability, in declaration order.</summary>
    public static IReadOnlyList<CountryPackageCapability> All { get; } =
        Enum.GetValues<CountryPackageCapability>();

    /// <summary>
    /// The interface a package implements to fill <paramref name="capability"/>.
    /// </summary>
    /// <remarks>
    /// For a generic extension point this is the open generic definition — a package supplies one
    /// closed implementation per document type it profiles.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="capability"/> is not a declared capability.
    /// </exception>
    public static Type ContractTypeFor(CountryPackageCapability capability) =>
        ByCapability.TryGetValue(capability, out Type? contract)
            ? contract
            : throw new ArgumentOutOfRangeException(
                nameof(capability),
                capability,
                "Not a declared Country Package capability.");

    /// <summary>
    /// The capability <paramref name="contractType"/> fills, or <see langword="null"/> if it is not
    /// an extension-point interface. A closed generic such as
    /// <c>IEInvoicingProfile&lt;CommercialInvoice&gt;</c> resolves through its definition.
    /// </summary>
    public static CountryPackageCapability? CapabilityOf(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        Type lookup = contractType.IsConstructedGenericType
            ? contractType.GetGenericTypeDefinition()
            : contractType;

        return lookup.GetCustomAttribute<ExtensionPointAttribute>()?.Capability;
    }

    private static ReadOnlyDictionary<CountryPackageCapability, Type> Build()
    {
        Dictionary<CountryPackageCapability, List<Type>> found = [];

        foreach (Type candidate in typeof(CountryPackageCapabilities).Assembly.GetExportedTypes())
        {
            ExtensionPointAttribute? marker = candidate.GetCustomAttribute<ExtensionPointAttribute>();
            if (marker is null)
            {
                continue;
            }

            if (!found.TryGetValue(marker.Capability, out List<Type>? contracts))
            {
                contracts = [];
                found[marker.Capability] = contracts;
            }

            contracts.Add(candidate);
        }

        List<string> problems = [];
        foreach (CountryPackageCapability capability in Enum.GetValues<CountryPackageCapability>())
        {
            int count = found.TryGetValue(capability, out List<Type>? contracts) ? contracts.Count : 0;
            if (count != 1)
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{capability} is implemented by {count} interface(s): " +
                    $"[{string.Join(", ", contracts?.Select(c => c.Name) ?? [])}]"));
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Every CountryPackageCapability must be marked on exactly one interface with " +
                "[ExtensionPoint]. " + string.Join("; ", problems));
        }

        return new ReadOnlyDictionary<CountryPackageCapability, Type>(
            found.ToDictionary(entry => entry.Key, entry => entry.Value[0]));
    }
}
