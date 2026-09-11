using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts;

/// <summary>
/// The errors a malformed Country Package manifest produces.
/// </summary>
/// <remarks>
/// Every one of these is a <b>rejection</b> rather than an exception: reading a manifest is reading
/// untrusted input from a file on disk, and a host that scans a packages directory must be able to
/// report "this package is unusable, here is why" for one package and carry on with the rest.
/// </remarks>
public static class PackageManifestErrors
{
    /// <summary>The error code every manifest-shape rejection carries.</summary>
    public const string InvalidCode = "country_package.manifest.invalid";

    /// <summary>
    /// A manifest field is missing or malformed. <paramref name="field"/> names the field in
    /// manifest terms, so the message points at the line to fix rather than at a C# member.
    /// </summary>
    public static Error Invalid(string field, string description) =>
        Error.Rejected(InvalidCode, $"{field}: {description}");
}
