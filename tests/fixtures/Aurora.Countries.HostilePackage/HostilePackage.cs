using System;
using System.IO;
using System.Reflection;
using Aurora.Countries.Contracts;
using Aurora.Countries.Contracts.Identifiers;
using Aurora.Countries.Contracts.Localization;
using Aurora.SharedKernel;

namespace Aurora.Countries.HostilePackage;

/// <summary>
/// The entry point of the hostile fixture: a valid Country Package in every respect the loader
/// checks, so that when it is refused, the refusal is the admission floor's and nothing else's.
/// </summary>
/// <remarks>
/// It reads its own embedded manifest with the contract's own reader, as the Testland fixture does,
/// so the manifest-matches-assembly check has one description of the package read twice. The
/// jurisdiction is borrowed from Testland because the manifest reader checks the country against
/// the runtime, and a made-up code would be refused before the loader was reached.
/// </remarks>
public sealed class HostilePackage : ICountryPackage
{
    private readonly ValidatesNothing _validator = new();

    /// <summary>Reads the manifest this assembly ships.</summary>
    /// <exception cref="InvalidOperationException">The embedded manifest is missing or invalid.</exception>
    public HostilePackage() => Manifest = ReadEmbeddedManifest();

    /// <inheritdoc/>
    public CountryPackageManifest Manifest { get; }

    /// <inheritdoc/>
    public object? GetExtension(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        return contractType == typeof(IIdentifierValidator) ? _validator : null;
    }

    private static CountryPackageManifest ReadEmbeddedManifest()
    {
        Assembly assembly = typeof(HostilePackage).Assembly;

        using Stream resource =
            assembly.GetManifestResourceStream(CountryPackageManifestJson.ResourceName)
            ?? throw new InvalidOperationException(
                $"{assembly.GetName().Name} embeds no '{CountryPackageManifestJson.ResourceName}'.");

        using MemoryStream buffer = new();
        resource.CopyTo(buffer);

        Result<CountryPackageManifest> manifest = CountryPackageManifestJson.Read(buffer.ToArray());
        return manifest.IsSuccess
            ? manifest.Value
            : throw new InvalidOperationException($"The embedded manifest is invalid: {manifest.Error}");
    }
}

/// <summary>
/// The one capability the manifest declares, provided honestly: a validator that rejects every
/// value, because the fixture exists to be refused or admitted, never to validate anything.
/// </summary>
public sealed class ValidatesNothing : IIdentifierValidator
{
    /// <inheritdoc/>
    public IdentifierKind Kind => IdentifierKind.CompanyRegistrationNumber;

    /// <inheritdoc/>
    public string CountryCode => "NZ";

    /// <inheritdoc/>
    public IdentifierValidation Validate(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        Result<LocalizedText> reason =
            LocalizedText.Invariant("The hostile fixture package validates nothing.");

        return IdentifierValidation.Rejected(reason.Value);
    }
}
