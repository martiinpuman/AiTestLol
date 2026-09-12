namespace Aurora.SharedKernel.UnitTests;

/// <summary>
/// The currencies the kernel tests use. Three real ISO 4217 codes with three different
/// minor-unit counts, so that a test cannot accidentally assume "two decimals" is universal.
/// </summary>
internal static class TestCurrencies
{
    /// <summary>New Zealand dollar — two minor units, the common case.</summary>
    public static Currency Nzd { get; } = Currency.Of("NZD", minorUnits: 2);

    /// <summary>Australian dollar — a second two-minor-unit currency, for mismatch tests.</summary>
    public static Currency Aud { get; } = Currency.Of("AUD", minorUnits: 2);

    /// <summary>Japanese yen — zero minor units.</summary>
    public static Currency Jpy { get; } = Currency.Of("JPY", minorUnits: 0);

    /// <summary>Bahraini dinar — three minor units.</summary>
    public static Currency Bhd { get; } = Currency.Of("BHD", minorUnits: 3);
}
