using System;
using System.Collections;
using System.Collections.Generic;
using Aurora.Platform.Tenancy.Contracts;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>
/// The package set a <see cref="TenantScope"/> carries (ADR-0007 §3.4): what
/// <c>catalog.installed_package</c> says about one tenant, read once at scope open and immutable
/// after that.
/// </summary>
public sealed class InstalledPackagesTests
{
    private static readonly InstalledPackageEntry Nz = new("nz", "1.2.0", InstalledPackageState.Active);
    private static readonly InstalledPackageEntry Au = new("au", "1.0.0", InstalledPackageState.Installing);

    [Fact]
    public void None_holds_no_package_and_answers_every_lookup_with_no()
    {
        InstalledPackages.None.Count.ShouldBe(0);
        InstalledPackages.None.Entries.ShouldBeEmpty();
        InstalledPackages.None.TryGet("nz", out InstalledPackageEntry? entry).ShouldBeFalse();
        entry.ShouldBeNull();
    }

    [Fact]
    public void Of_keeps_every_entry_once_in_package_id_order()
    {
        InstalledPackages packages = InstalledPackages.Of([Nz, Au]);

        packages.Count.ShouldBe(2);
        packages.Entries.ShouldBe([Au, Nz]);
        packages.TryGet("nz", out InstalledPackageEntry? nz).ShouldBeTrue();
        nz.ShouldBe(Nz);
        packages.TryGet("au", out InstalledPackageEntry? au).ShouldBeTrue();
        au.ShouldBe(Au);
    }

    [Fact]
    public void Of_over_no_entries_is_None_itself()
    {
        InstalledPackages.Of([]).ShouldBeSameAs(InstalledPackages.None);
    }

    [Fact]
    public void Lookup_is_by_exact_package_id_because_the_id_is_the_stem_of_a_schema_name()
    {
        InstalledPackages packages = InstalledPackages.Of([Nz]);

        packages.TryGet("NZ", out _).ShouldBeFalse();
        packages.TryGet("nz ", out _).ShouldBeFalse();
        packages.TryGet("", out _).ShouldBeFalse();
        packages.TryGet(null!, out _).ShouldBeFalse();
    }

    [Fact]
    public void Of_refuses_null_and_a_null_entry()
    {
        Should.Throw<ArgumentNullException>(() => InstalledPackages.Of(null!));
        Should.Throw<ArgumentException>(() => InstalledPackages.Of([Nz, null!]));
    }

    [Fact]
    public void Of_refuses_two_entries_for_one_package_rather_than_picking_one()
    {
        // One row per (tenant, package) is the catalog's rule. Two entries for one id here means a
        // reader that joined wrongly, and choosing either would hide it.
        InstalledPackageEntry nzAgain = new("nz", "1.3.0", InstalledPackageState.UpgradePending);

        ArgumentException refused = Should.Throw<ArgumentException>(() => InstalledPackages.Of([Nz, nzAgain]));

        refused.Message.ShouldContain("nz");
    }

    [Fact]
    public void Of_reads_its_input_once_and_keeps_its_own_copy()
    {
        OneShotSequence sequence = new([Nz, Au]);
        List<InstalledPackageEntry> source = [Nz];

        InstalledPackages fromSequence = InstalledPackages.Of(sequence);
        InstalledPackages fromList = InstalledPackages.Of(source);
        source.Clear();

        sequence.Enumerations.ShouldBe(1);
        fromSequence.Count.ShouldBe(2);
        fromList.Entries.ShouldBe([Nz]);
    }

    [Fact]
    public void An_entry_carries_its_id_version_and_state_and_compares_by_them()
    {
        InstalledPackageEntry entry = new("nz", "1.2.0", InstalledPackageState.Active);

        entry.PackageId.ShouldBe("nz");
        entry.Version.ShouldBe("1.2.0");
        entry.State.ShouldBe(InstalledPackageState.Active);
        entry.ShouldBe(Nz);
        entry.ShouldNotBe(new InstalledPackageEntry("nz", "1.2.0", InstalledPackageState.Deactivated));
    }

    [Theory]
    [InlineData(null, "1.0.0", "a null id")]
    [InlineData("", "1.0.0", "an empty id")]
    [InlineData("  ", "1.0.0", "a blank id")]
    [InlineData("n z", "1.0.0", "whitespace inside the id")]
    [InlineData("nz", null, "a null version")]
    [InlineData("nz", "", "an empty version")]
    [InlineData("nz", "1.0 .0", "whitespace inside the version")]
    public void An_entry_refuses_an_id_or_version_that_could_not_have_come_from_the_catalog(
        string? packageId, string? version, string why)
    {
        Should.Throw<ArgumentException>(
            () => new InstalledPackageEntry(packageId!, version!, InstalledPackageState.Active), why);
    }

    [Fact]
    public void An_entry_refuses_a_state_outside_the_enum()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new InstalledPackageEntry("nz", "1.0.0", (InstalledPackageState)99));
    }

    [Fact]
    public void The_entries_cannot_be_changed_through_a_cast()
    {
        InstalledPackages packages = InstalledPackages.Of([Nz]);

        packages.Entries.ShouldNotBeOfType<InstalledPackageEntry[]>();
        ICollection<InstalledPackageEntry>? mutableView = packages.Entries as ICollection<InstalledPackageEntry>;
        (mutableView is null || mutableView.IsReadOnly).ShouldBeTrue();
    }

    private sealed class OneShotSequence(IReadOnlyList<InstalledPackageEntry> items) : IEnumerable<InstalledPackageEntry>
    {
        public int Enumerations { get; private set; }

        public IEnumerator<InstalledPackageEntry> GetEnumerator()
        {
            Enumerations++;
            if (Enumerations > 1)
            {
                throw new InvalidOperationException("This sequence has already been enumerated.");
            }

            return items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
