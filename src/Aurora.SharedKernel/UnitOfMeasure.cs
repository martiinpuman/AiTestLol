using System;

namespace Aurora.SharedKernel;

/// <summary>
/// The unit a quantity is counted in: pieces, kilograms, metres, hours — a UN/ECE Recommendation
/// 20 common code.
/// </summary>
/// <remarks>
/// <para>
/// Recommendation 20 is not an arbitrary constraint imported for tidiness. Every electronic
/// invoice line carries a unit code from it, so a unit that cannot be written as a Rec 20 code
/// cannot be invoiced electronically — the shape this type enforces is a business constraint that
/// would otherwise be discovered at the point of sending an invoice to a customer.
/// </para>
/// <para>
/// <b>This type validates shape, not membership</b>, exactly as <see cref="Currency"/> does. It
/// asserts that a code is one to three ASCII letters or digits; it holds no list of the units that
/// exist. That list — code, name, and whether the unit is divisible or only countable — is
/// reference data owned by the item and unit-of-measure master, not by a tier-0 value object, and
/// hard-coding it here would put data that changes on its own schedule into an assembly that can
/// only change by redeploy.
/// </para>
/// <para>
/// There is deliberately no conversion between units here. Turning 2 cartons into 24 pieces needs
/// a conversion factor that belongs to an item, in the same way that turning NZD into AUD needs a
/// rate that belongs to a document. A value object that invented either would be guessing.
/// </para>
/// </remarks>
public readonly record struct UnitOfMeasure
{
    /// <summary>The longest a UN/ECE Recommendation 20 common code is.</summary>
    public const int MaxCodeLength = 3;

    private readonly string? _code;

    private UnitOfMeasure(string code) => _code = code;

    /// <summary>Creates a unit of measure from its UN/ECE Recommendation 20 common code.</summary>
    /// <param name="code">
    /// One to <see cref="MaxCodeLength"/> ASCII letters or digits, such as <c>H87</c> (piece),
    /// <c>KGM</c> (kilogram) or <c>HUR</c> (hour). Case is normalized to upper case so that casing
    /// can never split one unit into two.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> is empty, longer than <see cref="MaxCodeLength"/>, or holds
    /// anything but ASCII letters and digits.
    /// </exception>
    public static UnitOfMeasure Of(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (code.Length is 0 or > MaxCodeLength || !IsAllAsciiLettersOrDigits(code))
        {
            throw new ArgumentException(
                $"'{code}' is not a UN/ECE Recommendation 20 unit code: expected 1 to " +
                $"{MaxCodeLength} letters or digits.",
                nameof(code));
        }

        return new UnitOfMeasure(code.ToUpperInvariant());
    }

    /// <summary>
    /// Whether this value names a unit. <see langword="false"/> only for the struct default.
    /// </summary>
    public bool IsSpecified => _code is not null;

    /// <summary>The Recommendation 20 code, in upper case.</summary>
    /// <exception cref="InvalidOperationException">The unit is unspecified.</exception>
    public string Code => _code ?? throw Unspecified();

    /// <summary>
    /// The code, or a placeholder when unspecified. Never throws, because a diagnostic rendering
    /// that throws turns a logged failure into a second, unrelated one.
    /// </summary>
    public override string ToString() => _code ?? "<unspecified unit>";

    private static bool IsAllAsciiLettersOrDigits(string code)
    {
        foreach (char character in code)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static InvalidOperationException Unspecified() =>
        new("The unit of measure is unspecified. A UnitOfMeasure obtained from `default` names no " +
            "unit; build one with UnitOfMeasure.Of(code).");
}
