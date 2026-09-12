using System;
using System.Linq;
using System.Text.RegularExpressions;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>
/// ADR-0038 §2.4, D1 and D2: the read-path entry refuses what the catalog refuses and accepts what
/// it accepts, because one rule decides both - and the domain writer, the check constraint and the
/// entry all read that one rule.
/// </summary>
public sealed class PackageIdFormatTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// D1's rejected set: each member fails a different clause of the rule - a token PostgreSQL's
    /// lexer would split on <c>/**/</c>, an upper-case letter, a leading digit, a hyphen, a dot,
    /// one character past the length bound - plus a trailing newline, which a .NET <c>$</c> would
    /// pass and PostgreSQL's does not.
    /// </summary>
    public static TheoryData<string, string> Rejected => new()
    {
        { "a;drop/**/schema/**/platform/**/cascade;--", "SQL with no whitespace character in it" },
        { "ADMIN", "an upper-case letter" },
        { "1nz", "a leading digit" },
        { "nz-pack", "a hyphen" },
        { "nz.pack", "a dot" },
        { new string('a', PackageIdFormat.MaxLength + 1), "one character past the length bound" },
        { "nz\n", "a trailing newline" },
    };

    /// <summary>D1's accepted set, including the exact length bound.</summary>
    public static TheoryData<string> Accepted => new()
    {
        "nz",
        "nz_gst",
        "a",
        new string('a', PackageIdFormat.MaxLength),
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void D1_the_entry_refuses_an_id_the_catalog_would_refuse(string packageId, string why)
    {
        PackageIdFormat.IsWellFormed(packageId).ShouldBeFalse(why);

        ArgumentException refused = Should.Throw<ArgumentException>(
            () => new InstalledPackageEntry(packageId, "1.0.0", InstalledPackageState.Active),
            why);
        refused.ParamName.ShouldBe("packageId");
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void D1_the_entry_accepts_an_id_the_catalog_accepts(string packageId)
    {
        PackageIdFormat.IsWellFormed(packageId).ShouldBeTrue();

        new InstalledPackageEntry(packageId, "1.0.0", InstalledPackageState.Active).PackageId.ShouldBe(packageId);
    }

    [Theory]
    [MemberData(nameof(Rejected))]
    public void D2_the_domain_writer_decides_by_the_same_rule_and_refuses_the_same_ids(string packageId, string why)
    {
        Should.Throw<ArgumentException>(
                () => InstalledPackage.Begin(TenantId.Create(), packageId, "1.0.0", "user:7f3a", Now),
                why)
            .ParamName.ShouldBe("packageId");
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void D2_the_domain_writer_decides_by_the_same_rule_and_accepts_the_same_ids(string packageId)
    {
        InstalledPackage.Begin(TenantId.Create(), packageId, "1.0.0", "user:7f3a", Now).PackageId.ShouldBe(packageId);
    }

    [Fact]
    public void D2_the_check_constraint_is_generated_byte_for_byte_as_the_merged_migration_declares_it()
    {
        // The literal in 20260911172124_InitialCatalog.cs, which is already applied to every catalog
        // and which this type must reproduce exactly so that adopting it touches no migration
        // (ADR-0038 §2.2). The constraint's effect in the database is B-05.1's to prove (§2.5).
        PackageIdFormat.CheckConstraintSql("package_id").ShouldBe("package_id ~ '^[a-z][a-z0-9_]*$'");
        PackageIdFormat.MaxLength.ShouldBe(32, "package_id varchar(32)");
    }

    [Fact]
    public void The_constraint_column_must_be_named()
    {
        Should.Throw<ArgumentException>(() => PackageIdFormat.CheckConstraintSql(" "));
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void The_character_walk_and_the_pattern_agree_on_every_accepted_id(string packageId)
    {
        Regex.IsMatch(packageId, PackageIdFormat.CharacterPattern, RegexOptions.CultureInvariant)
            .ShouldBe(PackageIdFormat.IsWellFormed(packageId));
    }

    /// <summary>
    /// The two rejections the character pattern cannot make on its own: the length bound is the
    /// column's <c>varchar(32)</c>, not the pattern's, and .NET's <c>$</c> matches before a final
    /// newline where PostgreSQL's matches only at the end of the string.
    /// </summary>
    private static readonly string[] RejectedByTheWalkAlone = [new string('a', PackageIdFormat.MaxLength + 1), "nz\n"];

    [Theory]
    [MemberData(nameof(Rejected))]
    public void The_pattern_refuses_every_rejected_id_except_the_two_only_the_walk_can(string packageId, string why)
    {
        // The walk carries the length bound and sides with PostgreSQL on the newline, which is why
        // it is a walk and not a Regex over CharacterPattern; every other rejection is the pattern's own.
        bool dotNetPatternAccepts = Regex.IsMatch(packageId, PackageIdFormat.CharacterPattern, RegexOptions.CultureInvariant);

        dotNetPatternAccepts.ShouldBe(RejectedByTheWalkAlone.Contains(packageId, StringComparer.Ordinal), why);
        PackageIdFormat.IsWellFormed(packageId).ShouldBeFalse(why);
    }

    [Fact]
    public void A_null_id_is_refused_by_name()
    {
        Should.Throw<ArgumentNullException>(() => PackageIdFormat.IsWellFormed(null!)).ParamName.ShouldBe("packageId");
    }
}
