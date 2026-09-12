namespace Aurora.SharedKernel.UnitTests;

/// <summary>
/// The units of measure the kernel tests use. Real UN/ECE Recommendation 20 codes, one countable
/// and two divisible, so that a test cannot accidentally assume stock is always counted in pieces.
/// </summary>
internal static class TestUnits
{
    /// <summary>Piece — the countable unit almost every trading item is sold in.</summary>
    public static UnitOfMeasure Each { get; } = UnitOfMeasure.Of("H87");

    /// <summary>Kilogram — a divisible unit.</summary>
    public static UnitOfMeasure Kilogram { get; } = UnitOfMeasure.Of("KGM");

    /// <summary>Hour — what a service line is sold in.</summary>
    public static UnitOfMeasure Hour { get; } = UnitOfMeasure.Of("HUR");
}
