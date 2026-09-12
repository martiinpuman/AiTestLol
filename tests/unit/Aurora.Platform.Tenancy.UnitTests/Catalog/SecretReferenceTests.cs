using System;
using Aurora.Platform.Tenancy.Catalog;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

public sealed class SecretReferenceTests
{
    [Theory]
    [InlineData("vault://kv/aurora/clusters/nz-1/admin")]
    [InlineData("env:AURORA_NZ1_APP_PASSWORD")]
    [InlineData("k8s:aurora/nz-1#admin")]
    [InlineData("arn:aws:secretsmanager:ap-southeast-2:123456789012:secret:aurora/nz-1")]
    public void A_reference_into_a_secret_store_is_accepted(string reference)
    {
        SecretReference.Of(reference).Value.ShouldBe(reference);
        SecretReference.Of(reference).ToString().ShouldBe(reference);
    }

    [Theory]
    [InlineData("Host=pg;Username=aurora_app;Password=hunter2", "a whole connection string")]
    [InlineData("password=hunter2", "a key=value credential")]
    [InlineData("hunter2;", "a semicolon")]
    [InlineData("hunter 2", "whitespace")]
    [InlineData("hunter\t2", "a tab")]
    [InlineData("hunter\n2", "a newline")]
    [InlineData("", "empty")]
    public void A_value_shaped_like_a_credential_rather_than_a_reference_is_refused(string value, string why)
    {
        SecretReference.IsWellFormed(value).ShouldBeFalse(why);
        Should.Throw<ArgumentException>(() => SecretReference.Of(value));
    }

    [Fact]
    public void A_reference_longer_than_the_column_is_refused()
    {
        string tooLong = "vault://" + new string('a', SecretReference.MaxLength);

        Should.Throw<ArgumentException>(() => SecretReference.Of(tooLong));
    }

    [Fact]
    public void Null_is_an_argument_error()
    {
        Should.Throw<ArgumentNullException>(() => SecretReference.Of(null!));
    }

    [Fact]
    public void A_reference_nobody_assigned_says_so_rather_than_passing_for_a_real_one()
    {
        SecretReference unassigned = default;

        unassigned.IsSpecified.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => unassigned.Value);
        unassigned.ToString().ShouldNotBeNullOrEmpty();
    }
}
