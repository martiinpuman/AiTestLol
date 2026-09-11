using System;

namespace Aurora.SharedKernel;

/// <summary>
/// An ISO 4217 currency: its three-letter alphabetic code together with the number of decimal
/// places its minor unit occupies (ADR-0021 §1).
/// </summary>
/// <remarks>
/// <para>
/// The minor-unit count travels with the code because it is what a monetary amount rounds and
/// allocates to, and because it is not the same everywhere: 0 for JPY, 2 for most currencies,
/// 3 for a few. A currency that does not know its own minor units cannot round an invoice.
/// </para>
/// <para>
/// <b>This type validates shape, not membership.</b> It asserts that a code is three letters; it
/// deliberately holds no list of the currencies that exist. That list — code, name and minor
/// units — is core-seeded reference data (ADR-0021 §1), and resolving a code against it belongs
/// to the currency registry that owns that table, not to a tier-0 value object. Hard-coding the
/// list here would put data that changes on its own schedule into an assembly that can only
/// change by redeploy. Because <see cref="Of"/> demands the minor-unit count, a caller cannot
/// invent a currency without having looked it up somewhere.
/// </para>
/// <para>
/// A <see langword="default"/> <see cref="Currency"/> is <em>unspecified</em>: the struct default
/// cannot be suppressed in C#, so it is made detectable (<see cref="IsSpecified"/>) and loud
/// (<see cref="Code"/> throws) instead of silently behaving like a blank or a zero.
/// </para>
/// </remarks>
public readonly record struct Currency
{
    /// <summary>
    /// The most decimal places a currency's minor unit may occupy.
    /// </summary>
    /// <remarks>
    /// Monetary amounts are stored as <c>numeric(19,4)</c> (ADR-0021 §2), so a currency needing
    /// more than four decimals of settlement precision is a schema change, not a data change.
    /// ADR-0021 records that as a known limit; this constant is where it is enforced.
    /// </remarks>
    public const int MaxMinorUnits = 4;

    private const int CodeLength = 3;

    private readonly string? _code;
    private readonly int _minorUnits;

    private Currency(string code, int minorUnits)
    {
        _code = code;
        _minorUnits = minorUnits;
    }

    /// <summary>
    /// Creates a currency from an ISO 4217 alphabetic code and its minor-unit count.
    /// </summary>
    /// <param name="code">
    /// Three ASCII letters. Case is normalized to upper case so that casing can never split one
    /// currency into two.
    /// </param>
    /// <param name="minorUnits">
    /// Decimal places in the currency's minor unit, from 0 to <see cref="MaxMinorUnits"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not three ASCII letters.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minorUnits"/> is negative or above <see cref="MaxMinorUnits"/>.
    /// </exception>
    public static Currency Of(string code, int minorUnits)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentOutOfRangeException.ThrowIfNegative(minorUnits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minorUnits, MaxMinorUnits);

        if (code.Length != CodeLength || !IsAllAsciiLetters(code))
        {
            throw new ArgumentException(
                $"'{code}' is not an ISO 4217 alphabetic currency code: expected exactly {CodeLength} letters.",
                nameof(code));
        }

        return new Currency(code.ToUpperInvariant(), minorUnits);
    }

    /// <summary>
    /// Whether this value names a currency. <see langword="false"/> only for the struct default.
    /// </summary>
    public bool IsSpecified => _code is not null;

    /// <summary>The ISO 4217 alphabetic code, in upper case.</summary>
    /// <exception cref="InvalidOperationException">The currency is unspecified.</exception>
    public string Code => _code ?? throw Unspecified();

    /// <summary>Decimal places in this currency's minor unit.</summary>
    /// <exception cref="InvalidOperationException">The currency is unspecified.</exception>
    public int MinorUnits => _code is null ? throw Unspecified() : _minorUnits;

    /// <summary>
    /// The ISO 4217 code, or a placeholder when unspecified. Never throws, because a diagnostic
    /// rendering that throws turns a logged failure into a second, unrelated one.
    /// </summary>
    public override string ToString() => _code ?? "<unspecified currency>";

    private static bool IsAllAsciiLetters(string code)
    {
        foreach (char character in code)
        {
            if (!char.IsAsciiLetter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static InvalidOperationException Unspecified() =>
        new("The currency is unspecified. A Currency obtained from `default` names no currency; " +
            "build one with Currency.Of(code, minorUnits).");
}
