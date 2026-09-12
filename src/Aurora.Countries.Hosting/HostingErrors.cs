using Aurora.SharedKernel;

namespace Aurora.Countries.Hosting;

/// <summary>
/// Everything that can stop a Country Package being admitted, each with its own code.
/// </summary>
/// <remarks>
/// Distinct codes rather than one "package is bad" error, because these reach an operator. Odoo's
/// version-drift failure is a named, recurring support issue precisely because its message does not
/// say which of these happened or to what; an error that names the package, the versions and the
/// reason is the difference between a five-minute fix and a forum thread.
/// </remarks>
public static class HostingErrors
{
    /// <summary>The package's <c>coreContractRange</c> does not admit the running core contract.</summary>
    public const string IncompatibleCoreContractCode = "country_package.core_contract.incompatible";

    /// <summary>The package directory is not shaped like a package.</summary>
    public const string MalformedPackageCode = "country_package.malformed";

    /// <summary>The package's signature is missing, malformed, or verifies against no trusted key.</summary>
    public const string UntrustedCode = "country_package.untrusted";

    /// <summary>The package references a core assembly it is not allowed to.</summary>
    public const string ForbiddenReferenceCode = "country_package.forbidden_reference";

    /// <summary>
    /// The package's signature established less trust than this host's admission floor requires
    /// (ADR-0033 §5.2): it is listed, and not loaded.
    /// </summary>
    public const string BelowAdmissionFloorCode = "country_package.below_admission_floor";

    /// <summary>The package's code did not load, or did not expose an <c>ICountryPackage</c>.</summary>
    public const string LoadFailedCode = "country_package.load_failed";

    /// <summary>The host's own package configuration is not usable.</summary>
    public const string HostConfigurationCode = "country_package.host_configuration";

    /// <summary>The package's <c>coreContractRange</c> does not admit the running core contract.</summary>
    public static Error IncompatibleCoreContract(string description) =>
        Error.Rejected(IncompatibleCoreContractCode, description);

    /// <summary>The package directory is not shaped like a package.</summary>
    public static Error Malformed(string description) =>
        Error.Rejected(MalformedPackageCode, description);

    /// <summary>The package's signature is missing, malformed, or verifies against no trusted key.</summary>
    public static Error Untrusted(string description) => Error.NotPermitted(UntrustedCode, description);

    /// <summary>The package references a core assembly it is not allowed to.</summary>
    public static Error ForbiddenReference(string description) =>
        Error.NotPermitted(ForbiddenReferenceCode, description);

    /// <summary>
    /// The package's signature established less trust than this host's admission floor requires
    /// (ADR-0033 §5.2): it is listed, and not loaded.
    /// </summary>
    public static Error BelowAdmissionFloor(string description) =>
        Error.NotPermitted(BelowAdmissionFloorCode, description);

    /// <summary>The package's code did not load, or did not expose an <c>ICountryPackage</c>.</summary>
    public static Error LoadFailed(string description) => Error.Rejected(LoadFailedCode, description);

    /// <summary>The host's own package configuration is not usable.</summary>
    public static Error HostConfiguration(string description) =>
        Error.Rejected(HostConfigurationCode, description);
}
