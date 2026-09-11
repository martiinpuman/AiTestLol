using System;
using System.Globalization;

namespace Aurora.SharedKernel;

/// <summary>
/// An amount of money: a <see cref="decimal"/> amount together with the
/// <see cref="SharedKernel.Currency"/> it is denominated in (ADR-0021 §1).
/// </summary>
/// <remarks>
/// <para>
/// There is no such thing in this product as a bare number representing an amount of money
/// (glossary: <em>Money</em>). The currency travels with the amount, so a currency-less amount
/// cannot exist; amounts in different currencies never combine and never compare; and there is
/// no implicit conversion to <see cref="decimal"/>, so extracting <see cref="Amount"/> is
/// visible in review.
/// </para>
/// <para>
/// The amount is <see cref="decimal"/> — 128-bit base-10 and exact for the magnitudes an SMB ERP
/// sees. <c>double</c> and <c>float</c> appear nowhere in this assembly and are banned from the
/// domain by fitness rule F1.
/// </para>
/// <para>
/// Arithmetic here carries <b>full decimal precision and never rounds</b>. Rounding happens only
/// at the four points ADR-0021 §4 names, and only where a caller asks for it explicitly. A
/// negative or zero amount is legitimate: credit notes and reversals are normal business, not
/// error states.
/// </para>
/// </remarks>
public readonly record struct Money
{
    /// <summary>Creates an amount in a currency.</summary>
    /// <exception cref="ArgumentException"><paramref name="currency"/> is unspecified.</exception>
    public Money(decimal amount, Currency currency)
    {
        if (!currency.IsSpecified)
        {
            throw new ArgumentException(
                "An amount of money must name its currency; a currency-less amount cannot exist.",
                nameof(currency));
        }

        Amount = amount;
        Currency = currency;
    }

    /// <summary>The amount, at full decimal precision.</summary>
    public decimal Amount { get; }

    /// <summary>The currency the amount is denominated in.</summary>
    public Currency Currency { get; }

    /// <summary>Nothing, in the given currency.</summary>
    public static Money Zero(Currency currency) => new(0m, currency);

    /// <summary>Whether the amount is exactly nothing.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>Whether the amount is above zero.</summary>
    public bool IsPositive => Amount > 0m;

    /// <summary>Whether the amount is below zero, as a credit note or a reversal is.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>Adds two amounts in the same currency.</summary>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static Money operator +(Money left, Money right)
    {
        AssertSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    /// <summary>Subtracts one amount from another in the same currency.</summary>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static Money operator -(Money left, Money right)
    {
        AssertSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    /// <summary>Reverses the sign of an amount, keeping its currency.</summary>
    public static Money operator -(Money value)
    {
        value.AssertSpecified();
        return new Money(-value.Amount, value.Currency);
    }

    /// <summary>The amount without its sign, in the same currency.</summary>
    public Money Abs()
    {
        AssertSpecified();
        return new Money(Math.Abs(Amount), Currency);
    }

    /// <summary>
    /// A culture-invariant rendering for logs and test failures, such as <c>1234.50 NZD</c>.
    /// </summary>
    /// <remarks>
    /// This is never what a user sees. Presenting an amount in a user's locale — symbol,
    /// separators, digit placement — is a localization concern (ADR-0022), and doing it here
    /// would make a log line change meaning with the ambient culture.
    /// </remarks>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount} {Currency}");

    /// <summary>
    /// Refuses an operation whose operands are not in the same, named currency.
    /// </summary>
    /// <remarks>
    /// Unspecified currencies are rejected before the comparison so that a <see langword="default"/>
    /// <see cref="Money"/> — the one currency-less amount C# cannot prevent — is reported as what
    /// it is rather than as a mismatch against something.
    /// </remarks>
    private static void AssertSameCurrency(in Money left, in Money right)
    {
        left.AssertSpecified();
        right.AssertSpecified();

        if (left.Currency != right.Currency)
        {
            throw new CurrencyMismatchException(left.Currency, right.Currency);
        }
    }

    private void AssertSpecified()
    {
        if (!Currency.IsSpecified)
        {
            throw new InvalidOperationException(
                "This Money names no currency, so no arithmetic on it is meaningful. It came from " +
                "`default(Money)` or an unassigned field; build amounts with new Money(amount, currency) " +
                "or Money.Zero(currency).");
        }
    }
}
