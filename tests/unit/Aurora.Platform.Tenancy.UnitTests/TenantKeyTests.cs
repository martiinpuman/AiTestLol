using System;
using Aurora.Platform.Tenancy.Contracts;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests;

public sealed class TenantKeyTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-trading")]
    [InlineData("a1b")]
    [InlineData("acme-2-nz")]
    [InlineData("abc")]
    [InlineData("k234567890123456789012345678901234567890")]
    public void A_well_formed_key_parses_and_keeps_its_text(string text)
    {
        // The last case is exactly MaxLength characters long.
        text.Length.ShouldBeLessThanOrEqualTo(TenantKey.MaxLength);

        TenantKey key = TenantKey.Parse(text, null);

        key.IsSpecified.ShouldBeTrue();
        key.Value.ShouldBe(text);
        key.ToString().ShouldBe(text);
    }

    [Theory]
    [InlineData("ab", "shorter than MinLength")]
    [InlineData("k23456789012345678901234567890123456789012", "longer than MaxLength")]
    [InlineData("Acme", "upper case would fold differently in DNS and PostgreSQL")]
    [InlineData("acme_trading", "underscore is not a DNS label character")]
    [InlineData("1acme", "a leading digit")]
    [InlineData("-acme", "a leading hyphen")]
    [InlineData("acme-", "a trailing hyphen")]
    [InlineData("acme--trading", "a doubled hyphen")]
    [InlineData("acme trading", "whitespace")]
    [InlineData("acmé", "a non-ASCII letter")]
    [InlineData("acme.trading", "a dot would make a second DNS label")]
    [InlineData("", "empty")]
    public void A_malformed_key_is_refused(string text, string why)
    {
        TenantKey.TryParse(text, null, out TenantKey result).ShouldBeFalse(why);
        result.IsSpecified.ShouldBeFalse();

        FormatException thrown = Should.Throw<FormatException>(() => TenantKey.Parse(text, null));
        thrown.Message.ShouldContain(text);
    }

    [Fact]
    public void Null_text_is_an_argument_error_not_a_format_error()
    {
        Should.Throw<ArgumentNullException>(() => TenantKey.Parse(null!, null));
        TenantKey.TryParse(null, null, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_key_nobody_assigned_says_so_rather_than_passing_for_a_real_one()
    {
        TenantKey unassigned = default;

        unassigned.IsSpecified.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => unassigned.Value);
        unassigned.ToString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Two_keys_with_the_same_text_are_the_same_key()
    {
        TenantKey.Parse("acme", null).ShouldBe(TenantKey.Parse("acme", null));
        TenantKey.Parse("acme", null).GetHashCode().ShouldBe(TenantKey.Parse("acme", null).GetHashCode());
        TenantKey.Parse("acme", null).ShouldNotBe(TenantKey.Parse("acme-2", null));
    }

    [Fact]
    public void The_longest_key_still_fits_every_PostgreSQL_name_derived_from_it()
    {
        // PostgreSQL silently truncates identifiers longer than 63 bytes. Every name ADR-0007
        // derives from a key must stay inside that, or a rename could collide with another tenant.
        const int PostgresIdentifierLimit = 63;
        string longest = new('k', TenantKey.MaxLength);
        TenantKey key = TenantKey.Parse(longest, null);

        string database = "aurora_t_" + key.Value;
        string appRole = "aurora_app_" + key.Value;
        string renamedOnOffboarding = "deleted_" + key.Value + "_20260911";

        database.Length.ShouldBeLessThanOrEqualTo(PostgresIdentifierLimit);
        appRole.Length.ShouldBeLessThanOrEqualTo(PostgresIdentifierLimit);
        renamedOnOffboarding.Length.ShouldBeLessThanOrEqualTo(PostgresIdentifierLimit);
        longest.Length.ShouldBeLessThanOrEqualTo(PostgresIdentifierLimit, "a key is also a DNS label");
    }
}
