using System;
using System.Globalization;

namespace Aurora.Countries.Contracts;

/// <summary>Typed access to a package's extension points.</summary>
public static class CountryPackageExtensions
{
    /// <summary>
    /// The package's implementation of <typeparamref name="TContract"/>, or <see langword="null"/>
    /// if it provides none.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The package returned something that is not a <typeparamref name="TContract"/>. This is the
    /// shape the classic plugin bug takes — two copies of the contracts assembly, so the type the
    /// package implemented is not the type the host asked for — and it is worth its own message,
    /// because the cast failure on its own reads as nonsense.
    /// </exception>
    public static TContract? GetExtension<TContract>(this ICountryPackage package)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(package);

        object? extension = package.GetExtension(typeof(TContract));
        if (extension is null)
        {
            return null;
        }

        if (extension is TContract typed)
        {
            return typed;
        }

        throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"Package '{package.Manifest.Id}' returned a {extension.GetType().FullName} for " +
            $"{typeof(TContract).FullName}. If the two type names look identical, they are loaded " +
            $"from two different {CoreContract.AssemblyName} assemblies — the package's load context " +
            $"must resolve that assembly from the default context, not from its own directory."));
    }

    /// <summary>
    /// Whether the package declares <paramref name="capability"/> in its manifest. Declaring it is a
    /// promise; the installer separately checks that the implementation is actually there.
    /// </summary>
    public static bool Declares(this ICountryPackage package, CountryPackageCapability capability)
    {
        ArgumentNullException.ThrowIfNull(package);
        return package.Manifest.Capabilities.Contains(capability);
    }
}
