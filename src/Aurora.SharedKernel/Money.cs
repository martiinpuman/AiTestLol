using System;
using System.Collections.Generic;
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
public readonly record struct Money : IComparable<Money>
{
    /// <summary>
    /// How many minor units make one major unit, indexed by a currency's minor-unit count:
    /// 100 for NZD, 1 for JPY. A lookup rather than a computation, so each value is an exact
    /// <see cref="decimal"/> literal and no rounding can creep into the scaling that rounding and
    /// allocation depend on.
    /// </summary>
    private static readonly decimal[] MinorUnitScales = [1m, 10m, 100m, 1_000m, 10_000m];

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

    /// <summary>
    /// Scales an amount — a quantity times a unit price, a rate applied to a base. The result is
    /// an amount in the same currency, at full precision.
    /// </summary>
    public static Money operator *(Money amount, decimal factor)
    {
        amount.AssertSpecified();
        return new Money(amount.Amount * factor, amount.Currency);
    }

    /// <summary>Scales an amount. Mirror of <c>Money * decimal</c> so either order reads naturally.</summary>
    public static Money operator *(decimal factor, Money amount) => amount * factor;

    /// <summary>Divides an amount by a plain number, giving an amount at full precision.</summary>
    /// <exception cref="DivideByZeroException"><paramref name="divisor"/> is zero.</exception>
    public static Money operator /(Money amount, decimal divisor)
    {
        amount.AssertSpecified();
        return new Money(amount.Amount / divisor, amount.Currency);
    }

    /// <summary>
    /// Divides one amount by another in the same currency, giving the <b>ratio</b> between them —
    /// a plain number, not an amount. Money per money is dimensionless: it is a proportion, a
    /// margin or an allocation weight, and calling it money again is how a currency gets lost.
    /// </summary>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    /// <exception cref="DivideByZeroException"><paramref name="divisor"/> is nothing.</exception>
    public static decimal operator /(Money amount, Money divisor)
    {
        AssertSameCurrency(amount, divisor);
        return amount.Amount / divisor.Amount;
    }

    /// <summary>Whether the left amount is less than the right, in the same currency.</summary>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    /// <summary>Whether the left amount is greater than the right, in the same currency.</summary>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    /// <summary>Whether the left amount is at most the right, in the same currency.</summary>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    /// <summary>Whether the left amount is at least the right, in the same currency.</summary>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    /// <summary>Orders this amount against another in the same currency.</summary>
    /// <remarks>
    /// Deliberately throws rather than inventing an order across currencies. That makes sorting a
    /// mixed-currency list fail loudly, which is the correct outcome: NZD 10 is neither more nor
    /// less than AUD 10, and a report that quietly ordered them would be wrong in a way nobody
    /// would notice.
    /// </remarks>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public int CompareTo(Money other)
    {
        AssertSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    /// <summary>
    /// Adds up a sequence of amounts, all of which must be in <paramref name="currency"/>.
    /// </summary>
    /// <remarks>
    /// The currency is a parameter, not something inferred from the first element, so that an
    /// empty sequence still produces an amount in a named currency instead of a currency-less
    /// zero. Summing no invoice lines is nothing-in-NZD, never just nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="amounts"/> is <see langword="null"/>.</exception>
    /// <exception cref="CurrencyMismatchException">An amount is in another currency.</exception>
    public static Money Sum(IEnumerable<Money> amounts, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        Money total = Zero(currency);
        foreach (Money amount in amounts)
        {
            total += amount;
        }

        return total;
    }

    /// <summary>
    /// Rounds the amount under an explicitly named policy (ADR-0021 §3, §4).
    /// </summary>
    /// <remarks>
    /// There is no overload without a policy, and no default parameter value. Rounding is one of
    /// the four things ADR-0021 §4 says happen at defined points and nowhere else, and a
    /// convenience overload that picked a rule for the caller would put a jurisdiction's decision
    /// inside core code.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The amount names no currency, or <paramref name="policy"/> is unspecified.
    /// </exception>
    public Money Round(RoundingPolicy policy)
    {
        AssertSpecified();
        return new Money(policy.Round(Amount), Currency);
    }

    /// <summary>
    /// Whether the amount is already expressed in whole minor units of its currency — 2.34 NZD is,
    /// 2.345 NZD is not.
    /// </summary>
    /// <remarks>
    /// Trailing zeros do not change the answer: 2.3400 NZD is two cents' worth of information
    /// written four digits wide. This is the precondition for splitting an amount into parts that
    /// can each be paid.
    /// </remarks>
    public bool IsInWholeMinorUnits
    {
        get
        {
            AssertSpecified();
            decimal inMinorUnits = Amount * MinorUnitScale(Currency);
            return inMinorUnits == decimal.Truncate(inMinorUnits);
        }
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

    private static decimal MinorUnitScale(Currency currency) => MinorUnitScales[currency.MinorUnits];

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
