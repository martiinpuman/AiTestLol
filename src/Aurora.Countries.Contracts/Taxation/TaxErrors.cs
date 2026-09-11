using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Taxation;

/// <summary>The errors a package's tax contribution produces when core cannot use it.</summary>
public static class TaxErrors
{
    /// <summary>A tax value supplied by a package is malformed or out of bounds.</summary>
    public const string InvalidCode = "country_package.tax.invalid";

    /// <summary>No rule covers the code and date asked about.</summary>
    public const string NotCoveredCode = "country_package.tax.not_covered";

    /// <summary>A tax value a package supplied cannot be used.</summary>
    public static Error Invalid(string field, string description) =>
        Error.Rejected(InvalidCode, $"{field}: {description}");

    /// <summary>
    /// The package has no rule for this code on this date — which is a refusal to guess, not a
    /// missing feature. Guessing here posts the wrong tax.
    /// </summary>
    public static Error NotCovered(string description) => Error.NotFound(NotCoveredCode, description);
}
