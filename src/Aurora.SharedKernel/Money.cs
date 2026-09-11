using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

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

    /// <summary>
    /// Splits the amount into <paramref name="parts"/> parts that add back up to it exactly
    /// (ADR-0021 §6).
    /// </summary>
    /// <remarks>
    /// A hundred dollars across three lines is not three lots of 33.333…, because a third of a
    /// cent cannot be paid. The leftover minor units go to the earliest parts, so splitting the
    /// same amount twice gives the same answer twice: a split that varied between calls would
    /// make an invoice irreproducible.
    /// </remarks>
    /// <param name="parts">How many parts to split into; at least one.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="parts"/> is less than one.</exception>
    /// <exception cref="InvalidOperationException">
    /// The amount names no currency, or is not already a whole number of minor units.
    /// </exception>
    public IReadOnlyList<Money> Allocate(int parts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);

        decimal[] equalWeights = new decimal[parts];
        Array.Fill(equalWeights, 1m);
        return Allocate(equalWeights);
    }

    /// <summary>
    /// Splits the amount in proportion to <paramref name="weights"/>, into parts that add back up
    /// to it exactly (ADR-0021 §6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one operation that distributes an amount — a discount over lines, landed cost
    /// over receipts, a payment over invoices, tax over a document. It uses the
    /// <b>largest-remainder method</b>: every part takes its whole minor units, and the minor
    /// units left over go one each to the parts with the largest fractional remainder, earliest
    /// part first when remainders tie. The parts therefore add up to the original amount exactly —
    /// never a cent short, never a cent invented — which is the property ADR-0021 §6 requires and
    /// the reason no caller should divide a <see cref="Money"/> by a part count itself.
    /// </para>
    /// <para>
    /// A part weighted zero takes nothing, and takes no leftover either: the leftover is always
    /// smaller than the number of parts with a non-zero remainder, so a zero-weighted part can
    /// never be at the front of the queue. Weights are proportions, so their unit and their scale
    /// do not matter — <c>[1, 1, 2]</c> and <c>[25, 25, 50]</c> split identically.
    /// </para>
    /// <para>
    /// No weight is too precise for this. A weight written the obvious way — <c>lineAmount /
    /// documentTotal</c> — carries 28 decimal places, and <c>decimal</c> multiplication rounds
    /// <b>silently</b> once a product needs more than its 28-29 significant digits. Allocation
    /// therefore does not multiply the amount by the weights at all: it scales the weights to
    /// whole numbers once and runs the whole largest-remainder computation in
    /// <see cref="BigInteger"/>, where nothing can round. It then checks the split it is about to
    /// return against its own law and throws rather than hand back parts that do not add up.
    /// </para>
    /// </remarks>
    /// <param name="weights">One non-negative weight per part, not all zero.</param>
    /// <exception cref="ArgumentNullException"><paramref name="weights"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="weights"/> is empty, holds a negative weight, or is entirely zero.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The amount names no currency, or is not already a whole number of minor units.
    /// </exception>
    public IReadOnlyList<Money> Allocate(IReadOnlyList<decimal> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        AssertSpecified();

        AssertAllocatable(weights);

        if (!IsInWholeMinorUnits)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{this} is not a whole number of minor units, so it cannot be split into parts that " +
                $"can each be paid. ADR-0021 §4 rounds at defined points and nowhere else, so " +
                $"allocation will not round for you: round the amount first, under an explicit " +
                $"policy, with Round(RoundingPolicy.CoreDefaultFor(currency))."));
        }

        // From here to the sign being put back, every number is a whole one. The amount is a whole
        // count of minor units by the precondition just checked, and the weights are scaled to
        // whole numbers below, so each part's share is an integer division with an integer
        // remainder. Nothing in this method can round, whatever precision a caller's weights carry.
        decimal scale = MinorUnitScale(Currency);

        // The split runs on the magnitude and puts the sign back afterwards, so that a credit note
        // is divided exactly like the invoice it reverses instead of down its own rounding path.
        BigInteger totalMinorUnits = (BigInteger)(Math.Abs(Amount) * scale);

        BigInteger[] scaledWeights = ToCommonIntegerScale(weights);
        BigInteger weightTotal = BigInteger.Zero;
        foreach (BigInteger weight in scaledWeights)
        {
            weightTotal += weight;
        }

        BigInteger[] partMinorUnits = new BigInteger[weights.Count];
        BigInteger[] remainders = new BigInteger[weights.Count];
        BigInteger claimed = BigInteger.Zero;

        for (int part = 0; part < weights.Count; part++)
        {
            partMinorUnits[part] = BigInteger.DivRem(
                totalMinorUnits * scaledWeights[part],
                weightTotal,
                out BigInteger remainder);
            remainders[part] = remainder;
            claimed += partMinorUnits[part];
        }

        int leftover = LeftoverMinorUnits(totalMinorUnits - claimed, weights);
        int[] byLargestRemainder = ByLargestRemainder(remainders);
        for (int rank = 0; rank < leftover; rank++)
        {
            partMinorUnits[byLargestRemainder[rank]] += BigInteger.One;
        }

        bool isCredit = IsNegative;
        Money[] allocation = new Money[weights.Count];
        for (int part = 0; part < allocation.Length; part++)
        {
            decimal amount = (decimal)partMinorUnits[part] / scale;
            allocation[part] = new Money(isCredit ? -amount : amount, Currency);
        }

        AssertAddsUp(allocation, weights);
        return allocation;
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

    /// <summary>
    /// Refuses a set of weights that cannot divide anything.
    /// </summary>
    /// <remarks>
    /// Whether any weight is positive is asked of each weight rather than of their sum, so that
    /// the check is a comparison of exact values and not of an addition.
    /// </remarks>
    private static void AssertAllocatable(IReadOnlyList<decimal> weights)
    {
        if (weights.Count == 0)
        {
            throw new ArgumentException(
                "An amount cannot be split across no parts at all: supply one weight per part.",
                nameof(weights));
        }

        bool anyWeightIsPositive = false;
        for (int part = 0; part < weights.Count; part++)
        {
            if (weights[part] < 0m)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"A part's share of an amount cannot be negative, but weight {part} is " +
                        $"{weights[part]}. Split by non-negative weights and reverse the sign of " +
                        $"the whole allocation if what is being split is a credit."),
                    nameof(weights));
            }

            if (weights[part] > 0m)
            {
                anyWeightIsPositive = true;
            }
        }

        if (!anyWeightIsPositive)
        {
            throw new ArgumentException(
                "Every weight is zero, so there is no share for any part to take. Weights are " +
                "proportions of a total that must itself be greater than zero.",
                nameof(weights));
        }
    }

    /// <summary>
    /// The weights as whole numbers in one shared scale, so that the split can be computed without
    /// a single rounding-capable operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Weights are proportions: multiplying every one of them by the same power of ten changes no
    /// part's share. Doing that once, exactly, is what makes the rest of allocation exact.
    /// </para>
    /// <para>
    /// The alternative — multiplying the amount by each weight as a <see cref="decimal"/> — is
    /// what this replaces. <c>decimal</c> is exact in base ten until a result needs more than its
    /// 28-29 significant digits, at which point it rounds and says nothing. A weight written the
    /// natural way, <c>lineAmount / documentTotal</c>, already carries 28 decimal places, so the
    /// product crosses that line at four-figure amounts and a cent goes missing from an ordinary
    /// invoice. Whole numbers have no such budget to exhaust.
    /// </para>
    /// </remarks>
    private static BigInteger[] ToCommonIntegerScale(IReadOnlyList<decimal> weights)
    {
        int commonScale = 0;
        for (int part = 0; part < weights.Count; part++)
        {
            commonScale = Math.Max(commonScale, weights[part].Scale);
        }

        BigInteger[] scaled = new BigInteger[weights.Count];
        for (int part = 0; part < weights.Count; part++)
        {
            scaled[part] = Digits(weights[part])
                * BigInteger.Pow(10, commonScale - weights[part].Scale);
        }

        return scaled;
    }

    /// <summary>
    /// The digits of a non-negative <see cref="decimal"/> with its decimal point taken out: 1.25
    /// gives 125, and 1.2500 gives 12500.
    /// </summary>
    /// <remarks>
    /// Read out of the value's own 96-bit representation rather than computed, so that a weight
    /// carrying every digit <c>decimal</c> can hold converts without multiplying anything and
    /// therefore without any chance of rounding or overflow.
    /// </remarks>
    private static BigInteger Digits(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);

        BigInteger digits = new((uint)bits[2]);
        digits = (digits << 32) + (uint)bits[1];
        return (digits << 32) + (uint)bits[0];
    }

    /// <summary>
    /// The minor units still to hand out, having checked that they are a number the
    /// largest-remainder method can hand out at all.
    /// </summary>
    /// <remarks>
    /// In exact arithmetic this is always at least zero and always fewer than the number of parts,
    /// because each part is short of its exact share by less than one whole minor unit. Checking it
    /// rather than assuming it is the difference between a wrong split and a wrong split nobody
    /// notices: the implementation this replaces arrived here with a fractional value and cast it
    /// to <see cref="int"/>, so a leftover of 0.9999… became none and a cent left the invoice in
    /// silence.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The leftover is not one that can be handed out.</exception>
    private int LeftoverMinorUnits(BigInteger leftover, IReadOnlyList<decimal> weights)
    {
        if (leftover < BigInteger.Zero || leftover >= weights.Count)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Splitting {this} over weights [{Describe(weights)}] left {leftover} minor units " +
                $"to hand out, and the largest-remainder method can only ever have between 0 and " +
                $"{weights.Count - 1}. Refusing to return a split that does not add up " +
                $"(ADR-0021 §6)."));
        }

        return (int)leftover;
    }

    /// <summary>
    /// Refuses to return an allocation that breaks the one law allocation exists to keep.
    /// </summary>
    /// <remarks>
    /// Checked, not trusted. Every part is whole minor units and the parts add up to the amount by
    /// construction above; this says so out loud anyway, because the failure mode is an invoice
    /// whose lines do not match its total and nothing downstream of a wrong split can tell that it
    /// was wrong.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The parts are unpayable or do not add up.</exception>
    private void AssertAddsUp(Money[] allocation, IReadOnlyList<decimal> weights)
    {
        decimal allocated = 0m;
        for (int part = 0; part < allocation.Length; part++)
        {
            if (!allocation[part].IsInWholeMinorUnits)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Splitting {this} over weights [{Describe(weights)}] gave part {part} " +
                    $"{allocation[part]}, which is not a whole number of minor units and so cannot " +
                    $"be paid. Refusing to return it (ADR-0021 §6)."));
            }

            allocated += allocation[part].Amount;
        }

        if (allocated != Amount)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Splitting {this} over weights [{Describe(weights)}] gave parts adding up to " +
                $"{allocated}, not {Amount}. Refusing to return a split that loses or invents a " +
                $"minor unit (ADR-0021 §6)."));
        }
    }

    /// <summary>The weights as an invariant list, for an exception message that names the input.</summary>
    private static string Describe(IReadOnlyList<decimal> weights)
    {
        string[] written = new string[weights.Count];
        for (int part = 0; part < weights.Count; part++)
        {
            written[part] = weights[part].ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(", ", written);
    }

    /// <summary>
    /// The part indexes ordered by the remainder each part was short-changed by, largest first,
    /// earliest part first where two remainders are equal.
    /// </summary>
    /// <remarks>
    /// The tie-break on index is what makes a split repeatable. Without it the order of equal
    /// remainders would depend on the sort implementation, and the same invoice could give its
    /// leftover cent to a different line on a different day.
    /// </remarks>
    private static int[] ByLargestRemainder(BigInteger[] remainders)
    {
        int[] order = new int[remainders.Length];
        for (int part = 0; part < order.Length; part++)
        {
            order[part] = part;
        }

        Array.Sort(
            order,
            (left, right) =>
            {
                int byRemainder = remainders[right].CompareTo(remainders[left]);
                return byRemainder != 0 ? byRemainder : left.CompareTo(right);
            });

        return order;
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
