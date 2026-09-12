using System;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Secrets;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Secrets;

/// <summary>The bootstrap secret store: <c>env:NAME</c> reads the environment, and every failure names the reference, never a value.</summary>
public sealed class EnvironmentSecretStoreTests
{
    private readonly EnvironmentSecretStore _store = new();

    [Fact]
    public async Task An_env_reference_reads_the_named_environment_variable()
    {
        string name = "AURORA_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "value-for-this-test");
        try
        {
            string value = await _store.ReadAsync(SecretReference.Of("env:" + name), default);

            value.ShouldBe("value-for-this-test");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public async Task A_variable_that_is_not_set_is_reported_by_name()
    {
        string name = "AURORA_TEST_UNSET_" + Guid.NewGuid().ToString("N");

        SecretUnavailableException failed = await Should.ThrowAsync<SecretUnavailableException>(
            () => _store.ReadAsync(SecretReference.Of("env:" + name), default).AsTask());

        failed.Message.ShouldContain(name);
        failed.Reference.Value.ShouldBe("env:" + name);
    }

    [Theory]
    [InlineData("vault://kv/aurora/clusters/nz-1/app")]
    [InlineData("env:")]
    [InlineData("ENV:AURORA_X")]
    public async Task A_reference_this_store_does_not_understand_is_refused_rather_than_guessed(string reference)
    {
        SecretUnavailableException failed = await Should.ThrowAsync<SecretUnavailableException>(
            () => _store.ReadAsync(SecretReference.Of(reference), default).AsTask());

        failed.Message.ShouldContain(reference);
    }

    [Fact]
    public async Task An_unassigned_reference_is_refused()
    {
        await Should.ThrowAsync<ArgumentException>(() => _store.ReadAsync(default, default).AsTask());
    }

    [Fact]
    public async Task A_variable_that_is_set_but_empty_is_reported_as_such_and_not_as_missing()
    {
        // PR #14 L-3: refusing an empty credential is right; reporting it as "not set" sends the
        // operator to printenv, where the line is. The process API cannot create a set-but-empty
        // variable on demand, so the lookup is injected.
        var store = new EnvironmentSecretStore(_ => "");

        SecretUnavailableException failed = await Should.ThrowAsync<SecretUnavailableException>(
            () => store.ReadAsync(SecretReference.Of("env:AURORA_EMPTY"), default).AsTask());

        failed.Message.ShouldContain("'AURORA_EMPTY' is set but empty");
        failed.Message.ShouldNotContain("is not set");
    }

    [Fact]
    public async Task A_variable_that_is_absent_is_reported_as_not_set()
    {
        var store = new EnvironmentSecretStore(_ => null);

        SecretUnavailableException failed = await Should.ThrowAsync<SecretUnavailableException>(
            () => store.ReadAsync(SecretReference.Of("env:AURORA_ABSENT"), default).AsTask());

        failed.Message.ShouldContain("'AURORA_ABSENT' is not set");
    }
}
