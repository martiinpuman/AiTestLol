using Aurora.Platform.Tenancy.Contracts;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>
/// The two constants ADR-0007 §7.5 ships with the code and ADR-0027 §4 places in the contracts
/// assembly, held to the one relation every consumer relies on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is arbitrary.</b> The two values are a fixed pair, read straight from the
/// declaration; there is no input dimension to draw from. What can fail is the relation between
/// them, and it is asserted in the direction a mistake would take.
/// </para>
/// <para>
/// <b>What is deliberately not asserted here.</b> ADR-0027 §4's two honesty tests - <c>Current</c>
/// equals the highest ordinal across the registered <c>IModuleSchemaMigrator</c>s, and
/// <c>Current - MinimumSupported &lt;= 1</c> - are B-08.3's, because no migrator is registered yet
/// for the first to check against. Until then the value is pinned below, so that raising it is a
/// change in two places rather than one.
/// </para>
/// </remarks>
public sealed class CoreSchemaVersionTests
{
    [Fact]
    public void Both_constants_are_specified_versions()
    {
        CoreSchemaVersion.Current.IsSpecified.ShouldBeTrue();
        CoreSchemaVersion.MinimumSupported.IsSpecified.ShouldBeTrue();
    }

    [Fact]
    public void The_minimum_supported_version_never_exceeds_the_current_one()
    {
        // A gate whose floor is above the version the code was built for blocks every tenant,
        // including one at exactly Current. Swapping the two declarations is the mistake that
        // produces it, and this is the test that mistake fails.
        (CoreSchemaVersion.MinimumSupported <= CoreSchemaVersion.Current).ShouldBeTrue(
            $"MinimumSupported {CoreSchemaVersion.MinimumSupported} is above Current {CoreSchemaVersion.Current}");
    }

    [Fact]
    public void No_core_migration_exists_yet_so_both_are_the_initial_version()
    {
        // The first row that lands a core migration (B-07.2, the platform schema) raises Current
        // and this pin together; B-08.3 then replaces the pin with the migrator-derived check.
        CoreSchemaVersion.Current.ShouldBe(SchemaVersion.Of(0));
        CoreSchemaVersion.MinimumSupported.ShouldBe(SchemaVersion.Of(0));
    }
}
