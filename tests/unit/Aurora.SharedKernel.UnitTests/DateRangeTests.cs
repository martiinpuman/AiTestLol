using System;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

public sealed class DateRangeTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Jan10 = new(2026, 1, 10);
    private static readonly DateOnly Jan31 = new(2026, 1, 31);
    private static readonly DateOnly Feb1 = new(2026, 2, 1);

    [Fact]
    public void A_range_runs_from_its_first_day_up_to_but_not_including_its_end()
    {
        DateRange january = DateRange.FromUntil(Jan1, Feb1);

        january.Start.ShouldBe(Jan1);
        january.EndExclusive.ShouldBe(Feb1);
        january.LastDay.ShouldBe(Jan31);
        january.Days.ShouldBe(31);
    }

    [Fact]
    public void The_business_spelling_of_a_period_and_the_canonical_one_are_the_same_range()
    {
        DateRange.FromThrough(Jan1, Jan31).ShouldBe(DateRange.FromUntil(Jan1, Feb1));
    }

    [Fact]
    public void The_first_day_is_in_the_range_and_the_end_is_not()
    {
        DateRange january = DateRange.FromUntil(Jan1, Feb1);

        january.Contains(Jan1).ShouldBeTrue();
        january.Contains(Jan31).ShouldBeTrue();
        january.Contains(Feb1).ShouldBeFalse();
        january.Contains(Jan1.AddDays(-1)).ShouldBeFalse();
    }

    [Fact]
    public void A_single_day_is_a_range_of_one_day()
    {
        DateRange oneDay = DateRange.SingleDay(Jan10);

        oneDay.Days.ShouldBe(1);
        oneDay.Start.ShouldBe(Jan10);
        oneDay.LastDay.ShouldBe(Jan10);
        oneDay.EndExclusive.ShouldBe(Jan10.AddDays(1));
        oneDay.Contains(Jan10).ShouldBeTrue();
        oneDay.Contains(Jan10.AddDays(1)).ShouldBeFalse();
        oneDay.IsEmpty.ShouldBeFalse();
        oneDay.ShouldBe(DateRange.FromThrough(Jan10, Jan10));
    }

    [Fact]
    public void A_range_that_ends_where_it_starts_holds_no_days_at_all()
    {
        DateRange empty = DateRange.FromUntil(Jan10, Jan10);

        empty.IsEmpty.ShouldBeTrue();
        empty.Days.ShouldBe(0);
        empty.Contains(Jan10).ShouldBeFalse();
        empty.Contains(Jan10.AddDays(-1)).ShouldBeFalse();
    }

    [Fact]
    public void An_empty_range_has_no_last_day_to_report()
    {
        Should.Throw<InvalidOperationException>(() => DateRange.FromUntil(Jan10, Jan10).LastDay);
    }

    [Fact]
    public void A_range_nobody_set_holds_no_days_rather_than_every_day()
    {
        DateRange unset = default;

        unset.IsEmpty.ShouldBeTrue();
        unset.Days.ShouldBe(0);
        unset.Contains(Jan10).ShouldBeFalse();
    }

    [Fact]
    public void A_range_cannot_end_before_it_starts()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DateRange.FromUntil(Jan10, Jan1));
    }

    [Fact]
    public void An_inclusive_period_must_name_at_least_one_day()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DateRange.FromThrough(Jan10, Jan10.AddDays(-1)));
    }

    [Fact]
    public void Ranges_that_meet_end_to_end_do_not_overlap_which_is_why_the_end_is_exclusive()
    {
        DateRange january = DateRange.FromUntil(Jan1, Feb1);
        DateRange february = DateRange.FromUntil(Feb1, new DateOnly(2026, 3, 1));

        january.Overlaps(february).ShouldBeFalse();
        february.Overlaps(january).ShouldBeFalse();
        january.Intersect(february).ShouldBeNull();
    }

    [Fact]
    public void Ranges_that_share_a_day_overlap()
    {
        DateRange first = DateRange.FromThrough(Jan1, Jan10);
        DateRange second = DateRange.FromThrough(Jan10, Jan31);

        first.Overlaps(second).ShouldBeTrue();
        second.Overlaps(first).ShouldBeTrue();
        first.Intersect(second).ShouldBe(DateRange.SingleDay(Jan10));
    }

    [Fact]
    public void The_overlap_of_two_ranges_is_the_days_they_have_in_common()
    {
        DateRange first = DateRange.FromThrough(Jan1, Jan31);
        DateRange second = DateRange.FromThrough(Jan10, Feb1);

        first.Intersect(second).ShouldBe(DateRange.FromThrough(Jan10, Jan31));
    }

    [Fact]
    public void An_empty_range_overlaps_nothing_not_even_itself()
    {
        DateRange empty = DateRange.FromUntil(Jan10, Jan10);
        DateRange january = DateRange.FromUntil(Jan1, Feb1);

        empty.Overlaps(january).ShouldBeFalse();
        january.Overlaps(empty).ShouldBeFalse();
        empty.Overlaps(empty).ShouldBeFalse();
        empty.Intersect(january).ShouldBeNull();
    }

    [Fact]
    public void A_range_counts_the_days_the_calendar_actually_has()
    {
        DateRange leapFebruary = DateRange.FromThrough(new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 29));

        leapFebruary.Days.ShouldBe(29);
    }

    [Fact]
    public void Ranges_with_the_same_bounds_are_the_same_range()
    {
        DateRange.FromUntil(Jan1, Feb1).ShouldBe(DateRange.FromUntil(Jan1, Feb1));
        DateRange.FromUntil(Jan1, Feb1).GetHashCode()
            .ShouldBe(DateRange.FromUntil(Jan1, Feb1).GetHashCode());
        DateRange.FromUntil(Jan1, Feb1).ShouldNotBe(DateRange.FromUntil(Jan1, Jan31));
    }

    [Fact]
    public void ToString_says_which_end_is_included_and_does_not_depend_on_the_current_culture()
    {
        DateRange.FromUntil(Jan1, Feb1).ToString().ShouldBe("[2026-01-01, 2026-02-01)");
    }
}
