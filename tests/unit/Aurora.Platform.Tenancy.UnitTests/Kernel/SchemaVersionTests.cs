using System;
using System.Globalization;
using Aurora.Platform.Tenancy.Contracts;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>
/// <see cref="SchemaVersion"/> by example: the ordinal it carries, the values it refuses, and the
/// struct default that names no version rather than version zero.
/// </summary>
public sealed class SchemaVersionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(SchemaVersion.MaxOrdinal)]
    public void A_version_keeps_its_ordinal_and_renders_it(int ordinal)
    {
        SchemaVersion version = SchemaVersion.Of(ordinal);

        version.IsSpecified.ShouldBeTrue();
        version.Ordinal.ShouldBe(ordinal);
        version.ToString().ShouldBe(ordinal.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(SchemaVersion.MaxOrdinal + 1)]
    public void An_ordinal_outside_the_range_is_refused(int ordinal)
    {
        ArgumentOutOfRangeException refused = Should.Throw<ArgumentOutOfRangeException>(() => SchemaVersion.Of(ordinal));

        refused.ParamName.ShouldBe("ordinal");
    }

    [Fact]
    public void The_struct_default_is_no_version_at_all_not_version_zero()
    {
        // Version 0 is a real version (a database nothing has migrated yet). A field nobody set
        // must not read as that: the §7.5 gate would compare it, and whether it passed would depend
        // on what Current happens to be that release.
        SchemaVersion unspecified = default;

        unspecified.IsSpecified.ShouldBeFalse();
        unspecified.ShouldNotBe(SchemaVersion.Of(0));
        Should.Throw<InvalidOperationException>(() => unspecified.Ordinal);
        unspecified.ToString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void An_unspecified_version_cannot_be_compared_on_either_side()
    {
        SchemaVersion unspecified = default;
        SchemaVersion real = SchemaVersion.Of(1);

        Should.Throw<InvalidOperationException>(() => unspecified.CompareTo(real));
        Should.Throw<InvalidOperationException>(() => real.CompareTo(unspecified));
        Should.Throw<InvalidOperationException>(() => unspecified < real);
        Should.Throw<InvalidOperationException>(() => real >= unspecified);
    }

    [Fact]
    public void Two_versions_with_the_same_ordinal_are_one_version()
    {
        SchemaVersion.Of(3).ShouldBe(SchemaVersion.Of(3));
        SchemaVersion.Of(3).GetHashCode().ShouldBe(SchemaVersion.Of(3).GetHashCode());
        (SchemaVersion.Of(3) == SchemaVersion.Of(3)).ShouldBeTrue();
        SchemaVersion.Of(3).ShouldNotBe(SchemaVersion.Of(4));
        (SchemaVersion.Of(3) != SchemaVersion.Of(4)).ShouldBeTrue();
    }

    [Fact]
    public void Versions_order_by_ordinal()
    {
        SchemaVersion earlier = SchemaVersion.Of(1);
        SchemaVersion later = SchemaVersion.Of(2);

        (earlier < later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (earlier <= SchemaVersion.Of(1)).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
        (later >= SchemaVersion.Of(2)).ShouldBeTrue();
        (later < earlier).ShouldBeFalse();
        earlier.CompareTo(later).ShouldBeLessThan(0);
        later.CompareTo(earlier).ShouldBeGreaterThan(0);
        earlier.CompareTo(SchemaVersion.Of(1)).ShouldBe(0);
    }
}
