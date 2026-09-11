using System;
using System.Collections.Generic;
using System.Globalization;

namespace Aurora.SharedKernel;

/// <summary>
/// An amount of something countable: a <see cref="decimal"/> amount together with the
/// <see cref="UnitOfMeasure"/> it is counted in.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Quantity"/> is to units what <see cref="Money"/> is to currencies, deliberately and
/// visibly: the unit travels with the amount, quantities in different units never combine and
/// never compare, and there is no implicit conversion to <see cref="decimal"/>. The two types are
/// the same shape so that a reader who has understood one has understood the other.
/// </para>
/// <para>
/// A quantity times a unit price is a <see cref="Money"/> (ADR-0021 §1). A quantity times a
/// quantity is not expressible, and neither is a money times a money.
/// </para>
/// <para>
/// Arithmetic here carries <b>full decimal precision and never rounds</b>: three at a unit price
/// of 0.333333 is 0.999999, and rounding that to the currency's minor units is the caller's
/// separate, explicit step (ADR-0021 §4). Negative and zero quantities are legitimate — a return,
/// a write-off and an unfulfilled line are all normal business.
/// </para>
/// </remarks>
public readonly record struct Quantity : IComparable<Quantity>
{
    /// <summary>Creates a quantity in a unit of measure.</summary>
    /// <exception cref="ArgumentException"><paramref name="unit"/> is unspecified.</exception>
    public Quantity(decimal amount, UnitOfMeasure unit)
    {
        if (!unit.IsSpecified)
        {
            throw new ArgumentException(
                "A quantity must name the unit it is counted in; a unitless quantity cannot exist.",
                nameof(unit));
        }

        Amount = amount;
        Unit = unit;
    }

    /// <summary>The amount, at full decimal precision.</summary>
    public decimal Amount { get; }

    /// <summary>The unit the amount is counted in.</summary>
    public UnitOfMeasure Unit { get; }

    /// <summary>Nothing, in the given unit.</summary>
    public static Quantity Zero(UnitOfMeasure unit) => new(0m, unit);

    /// <summary>Whether the quantity is exactly nothing.</summary>
    /// <remarks>
    /// Like every other member here, this refuses a quantity that names no unit rather than
    /// answering for it. A <see langword="default"/> <see cref="Quantity"/> is not a valid zero,
    /// and reading it as one would let <c>if (received.IsZero)</c> quietly skip an unassigned
    /// field.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The quantity names no unit.</exception>
    public bool IsZero
    {
        get
        {
            AssertSpecified();
            return Amount == 0m;
        }
    }

    /// <summary>Whether the quantity is above zero.</summary>
    /// <exception cref="InvalidOperationException">The quantity names no unit.</exception>
    public bool IsPositive
    {
        get
        {
            AssertSpecified();
            return Amount > 0m;
        }
    }

    /// <summary>Whether the quantity is below zero, as a return or a stock issue is.</summary>
    /// <exception cref="InvalidOperationException">The quantity names no unit.</exception>
    public bool IsNegative
    {
        get
        {
            AssertSpecified();
            return Amount < 0m;
        }
    }

    /// <summary>Adds two quantities in the same unit.</summary>
    /// <exception cref="UnitOfMeasureMismatchException">The units differ.</exception>
    public static Quantity operator +(Quantity left, Quantity right)
    {
        AssertSameUnit(left, right);
        return new Quantity(left.Amount + right.Amount, left.Unit);
    }

    /// <summary>Subtracts one quantity from another in the same unit.</summary>
    /// <exception cref="UnitOfMeasureMismatchException">The units differ.</exception>
    public static Quantity operator -(Quantity left, Quantity right)
    {
        AssertSameUnit(left, right);
        return new Quantity(left.Amount - right.Amount, left.Unit);
    }

    /// <summary>Reverses the direction of a quantity, keeping its unit.</summary>
    public static Quantity operator -(Quantity value)
    {
        value.AssertSpecified();
        return new Quantity(-value.Amount, value.Unit);
    }

    /// <summary>Scales a quantity by a plain number — a yield factor, a pack size.</summary>
    public static Quantity operator *(Quantity quantity, decimal factor)
    {
        quantity.AssertSpecified();
        return new Quantity(quantity.Amount * factor, quantity.Unit);
    }

    /// <summary>Scales a quantity. Mirror of <c>Quantity * decimal</c> so either order reads naturally.</summary>
    public static Quantity operator *(decimal factor, Quantity quantity) => quantity * factor;

    /// <summary>Divides a quantity by a plain number, giving a quantity at full precision.</summary>
    /// <exception cref="DivideByZeroException"><paramref name="divisor"/> is zero.</exception>
    public static Quantity operator /(Quantity quantity, decimal divisor)
    {
        quantity.AssertSpecified();
        return new Quantity(quantity.Amount / divisor, quantity.Unit);
    }

    /// <summary>
    /// Divides one quantity by another in the same unit, giving the <b>ratio</b> between them — a
    /// plain number, not a quantity. Pieces per piece is dimensionless: it is a proportion, a yield
    /// or a fill rate, and calling it a quantity again is how a unit gets lost.
    /// </summary>
    /// <exception cref="UnitOfMeasureMismatchException">The units differ.</exception>
    /// <exception cref="DivideByZeroException"><paramref name="divisor"/> is nothing.</exception>
    public static decimal operator /(Quantity quantity, Quantity divisor)
    {
        AssertSameUnit(quantity, divisor);
        return quantity.Amount / divisor.Amount;
    }

    /// <summary>
    /// Multiplies a quantity by a unit price, giving the line amount in the price's currency at
    /// full precision (ADR-0021 §4 step 1, before its rounding).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The quantity names no unit, or <paramref name="unitPrice"/> names no currency.
    /// </exception>
    public static Money operator *(Quantity quantity, Money unitPrice)
    {
        quantity.AssertSpecified();
        return unitPrice * quantity.Amount;
    }

    /// <summary>Multiplies a unit price by a quantity. Mirror of <c>Quantity * Money</c>.</summary>
    /// <exception cref="InvalidOperationException">
    /// The quantity names no unit, or <paramref name="unitPrice"/> names no currency.
    /// </exception>
    public static Money operator *(Money unitPrice, Quantity quantity) => quantity * unitPrice;

    /// <summary>Whether the left quantity is less than the right, in the same unit.</summary>
    /// <exception cref="UnitOfMeasureMismatchException">The units differ.</exception>
    public static bool operator <(Quantity left, Quantity right) => left.CompareTo(right) < 0;

    /// <summary>Whether the left quantity is greater than the right, in the same unit.</summary>
    /// <exception cref="UnitOfMeasureMismatchException">The units differ.</exception>
    public static bool operator >(Quantity left, Quantity right) => left.CompareTo(right) > 0;

    /// <summary>Whether the left quantity is at most the right, in the same unit.</summary>
    /// <exception cref="UnitOfMeasureMismatchException">The units differ.</exception>
    public static bool operator <=(Quantity left, Quantity right) => left.CompareTo(right) <= 0;

    /// <summary>Whether the left quantity is at least the right, in the same unit.</summary>
    /// <exception cref="UnitOfMeasureMismatchException">The units differ.</exception>
    public static bool operator >=(Quantity left, Quantity right) => left.CompareTo(right) >= 0;

    /// <summary>Orders this quantity against another in the same unit.</summary>
    /// <remarks>
    /// Deliberately throws rather than inventing an order across units, so that sorting a mixed
    /// list fails loudly: 10 pieces is neither more nor less than 10 kilograms.
    /// </remarks>
    /// <exception cref="UnitOfMeasureMismatchException">The units differ.</exception>
    public int CompareTo(Quantity other)
    {
        AssertSameUnit(this, other);
        return Amount.CompareTo(other.Amount);
    }

    /// <summary>
    /// Adds up a sequence of quantities, all of which must be in <paramref name="unit"/>.
    /// </summary>
    /// <remarks>
    /// The unit is a parameter, not something inferred from the first element, so that an empty
    /// sequence still produces a quantity in a named unit instead of a unitless zero. Summing no
    /// order lines is nothing-in-pieces, never just nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="quantities"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnitOfMeasureMismatchException">A quantity is in another unit.</exception>
    public static Quantity Sum(IEnumerable<Quantity> quantities, UnitOfMeasure unit)
    {
        ArgumentNullException.ThrowIfNull(quantities);

        Quantity total = Zero(unit);
        foreach (Quantity quantity in quantities)
        {
            total += quantity;
        }

        return total;
    }

    /// <summary>The quantity without its direction, in the same unit.</summary>
    public Quantity Abs()
    {
        AssertSpecified();
        return new Quantity(Math.Abs(Amount), Unit);
    }

    /// <summary>
    /// A culture-invariant rendering for logs and test failures, such as <c>12.5 KGM</c>.
    /// </summary>
    /// <remarks>
    /// This is never what a user sees. Presenting a quantity in a user's locale — separators,
    /// digit placement, a translated unit name — is a localization concern (ADR-0022).
    /// </remarks>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount} {Unit}");

    /// <summary>
    /// Refuses an operation whose operands are not counted in the same, named unit.
    /// </summary>
    /// <remarks>
    /// Unspecified units are rejected before the comparison so that a <see langword="default"/>
    /// <see cref="Quantity"/> — the one unitless quantity C# cannot prevent — is reported as what
    /// it is rather than as a mismatch against something.
    /// </remarks>
    private static void AssertSameUnit(in Quantity left, in Quantity right)
    {
        left.AssertSpecified();
        right.AssertSpecified();

        if (left.Unit != right.Unit)
        {
            throw new UnitOfMeasureMismatchException(left.Unit, right.Unit);
        }
    }

    private void AssertSpecified()
    {
        if (!Unit.IsSpecified)
        {
            throw new InvalidOperationException(
                "This Quantity names no unit of measure, so no arithmetic on it is meaningful. It " +
                "came from `default(Quantity)` or an unassigned field; build quantities with " +
                "new Quantity(amount, unit) or Quantity.Zero(unit).");
        }
    }
}
