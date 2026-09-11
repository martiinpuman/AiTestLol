using System;
using System.Globalization;

namespace Aurora.SharedKernel;

/// <summary>
/// A run of whole calendar days: an accounting period, a tax rate's validity, a price list's
/// life, a contract's term.
/// </summary>
/// <remarks>
/// <para>
/// <b>The range is half-open: <see cref="Start"/> is included, <see cref="EndExclusive"/> is
/// not.</b> This is the decision this type exists to make once, because the alternative — every
/// caller deciding for itself whether "1 January to 31 January" includes the 31st — is how a
/// transaction on a period boundary ends up counted twice or not at all.
/// </para>
/// <para>
/// Half-open is the canonical form for three reasons that matter in an ERP:
/// </para>
/// <list type="number">
/// <item>
/// Consecutive periods tile exactly. January's end <em>is</em> February's start, so no day can
/// fall in both and no day can fall in neither. With inclusive ends, tiling requires adding a day
/// somewhere, and that addition is the bug.
/// </item>
/// <item>
/// The length is the subtraction: <see cref="Days"/> is end minus start, with no
/// off-by-one correction to forget.
/// </item>
/// <item>
/// A range holding no days at all is expressible (<see cref="IsEmpty"/>), which an inclusive range
/// cannot do. A rate superseded on the day it took effect is a real thing for a Country Package to
/// ship, and the right answer is to represent it and let validation reject it, not to make it
/// unrepresentable and throw at construction.
/// </item>
/// </list>
/// <para>
/// Business documents and statutes speak in inclusive ends, so both spellings are offered:
/// <see cref="FromUntil"/> takes the exclusive end and <see cref="FromThrough"/> takes the last
/// day. <see cref="LastDay"/> reads it back inclusively for display. The two factories build the
/// same value; only the way the caller says it differs.
/// </para>
/// <para>
/// A <see langword="default"/> <see cref="DateRange"/> is the empty range at
/// <see cref="DateOnly.MinValue"/>. That needs no special handling, unlike a
/// <see langword="default"/> <see cref="Currency"/>: a range holding no days contains nothing,
/// overlaps nothing, and is the safe answer rather than a misleading one.
/// </para>
/// </remarks>
public readonly record struct DateRange
{
    private DateRange(DateOnly start, DateOnly endExclusive)
    {
        Start = start;
        EndExclusive = endExclusive;
    }

    /// <summary>The first day in the range. Included.</summary>
    public DateOnly Start { get; }

    /// <summary>The day after the range. Not included.</summary>
    public DateOnly EndExclusive { get; }

    /// <summary>
    /// Creates a range from its first day up to, but not including, <paramref name="until"/>.
    /// </summary>
    /// <remarks>
    /// The canonical spelling. <c>FromUntil(day, day)</c> is the empty range on that day.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="until"/> is before <paramref name="from"/>.</exception>
    public static DateRange FromUntil(DateOnly from, DateOnly until)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(until, from);
        return new DateRange(from, until);
    }

    /// <summary>
    /// Creates a range from its first day through its last day, both included — the way a period,
    /// a statute or a contract is written.
    /// </summary>
    /// <remarks>
    /// An inclusive range always names at least one day, so there is no inclusive spelling of the
    /// empty range; say <c>FromUntil(day, day)</c> for that.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="through"/> is before <paramref name="from"/>.</exception>
    public static DateRange FromThrough(DateOnly from, DateOnly through)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(through, from);
        return new DateRange(from, through.AddDays(1));
    }

    /// <summary>Creates the range covering exactly one day.</summary>
    public static DateRange SingleDay(DateOnly day) => new(day, day.AddDays(1));

    /// <summary>How many days the range covers.</summary>
    public int Days => EndExclusive.DayNumber - Start.DayNumber;

    /// <summary>Whether the range covers no days at all.</summary>
    public bool IsEmpty => EndExclusive == Start;

    /// <summary>
    /// The last day in the range, for the inclusive spelling a document or a screen uses.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The range is empty, so there is no last day. Asking an empty range for its last day is a
    /// question with no honest answer; the day before its start is not in it.
    /// </exception>
    public DateOnly LastDay => IsEmpty
        ? throw new InvalidOperationException(
            "An empty date range has no last day. Check IsEmpty before reading LastDay, or use " +
            "EndExclusive, which every range has.")
        : EndExclusive.AddDays(-1);

    /// <summary>Whether a day falls in the range.</summary>
    public bool Contains(DateOnly day) => day >= Start && day < EndExclusive;

    /// <summary>
    /// Whether this range and another share at least one day.
    /// </summary>
    /// <remarks>
    /// Ranges that meet end to end do not overlap — that is the whole point of the exclusive end —
    /// and an empty range overlaps nothing, including itself. The emptiness test is not redundant:
    /// the usual half-open comparison reports an overlap for an empty range sitting inside another
    /// one, which shares no day with it.
    /// </remarks>
    public bool Overlaps(DateRange other) =>
        !IsEmpty
        && !other.IsEmpty
        && Start < other.EndExclusive
        && other.Start < EndExclusive;

    /// <summary>
    /// The days this range and another have in common, or <see langword="null"/> when they have
    /// none.
    /// </summary>
    /// <remarks>
    /// No overlap gives <see langword="null"/> rather than an empty range, because an empty range
    /// would have to invent a date to sit on and callers would then compare that invented date.
    /// </remarks>
    public DateRange? Intersect(DateRange other)
    {
        if (!Overlaps(other))
        {
            return null;
        }

        DateOnly start = Start > other.Start ? Start : other.Start;
        DateOnly endExclusive = EndExclusive < other.EndExclusive ? EndExclusive : other.EndExclusive;
        return new DateRange(start, endExclusive);
    }

    /// <summary>
    /// A culture-invariant rendering that states which end is included, such as
    /// <c>[2026-01-01, 2026-02-01)</c>.
    /// </summary>
    /// <remarks>
    /// The bracket and the parenthesis are deliberate: a log line reading <c>1 Jan - 31 Jan</c>
    /// cannot be checked against the code, and one reading <c>[2026-01-01, 2026-02-01)</c> can.
    /// Presenting a period to a user is a localization concern (ADR-0022).
    /// </remarks>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"[{Start:yyyy-MM-dd}, {EndExclusive:yyyy-MM-dd})");
}
