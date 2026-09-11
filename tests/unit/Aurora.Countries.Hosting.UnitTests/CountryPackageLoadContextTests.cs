using System;
using System.Reflection;
using Aurora.Countries.Contracts;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Hosting.UnitTests;

/// <summary>
/// What a package can and cannot reach through its own load context (ADR-0008 §9.2, §3.1).
/// </summary>
public sealed class CountryPackageLoadContextTests
{
    /// <summary>
    /// The one rule that makes the plugin model work: the contract assembly comes from the default
    /// context, so the host and every package see one <see cref="Type"/> identity. Asserted by
    /// reference equality, because "the same type" is exactly what would silently stop being true.
    /// </summary>
    [Theory]
    [InlineData("Aurora.Countries.Contracts")]
    [InlineData("Aurora.SharedKernel")]
    [InlineData("AURORA.COUNTRIES.CONTRACTS")]
    [InlineData("aurora.sharedkernel")]
    public void The_shared_tier_zero_assemblies_come_from_the_default_context(string name)
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();
        CountryPackageLoadContext context = new("test", package.AssemblyPath);

        try
        {
            Assembly resolved = context.LoadFromAssemblyName(new AssemblyName(name));

            resolved.ShouldBeSameAs(
                string.Equals(name, CoreContract.AssemblyName, StringComparison.OrdinalIgnoreCase)
                    ? typeof(CoreContract).Assembly
                    : typeof(SharedKernel.Money).Assembly);
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>
    /// Defence in depth behind the metadata check. A package that names a core assembly by string at
    /// runtime — through reflection, past the reference list — is refused by the context itself
    /// rather than being handed the host's own copy.
    /// </summary>
    /// <remarks>
    /// The runtime binds assembly simple names case-insensitively, so the refusal has to as well.
    /// Before it did, <c>AURORA.Countries.Hosting</c> fell past an ordinal prefix check, missed the
    /// package directory, and was answered by the default context with the host's real assembly.
    /// </remarks>
    [Theory]
    [InlineData("Aurora.Platform.Tenancy")]
    [InlineData("Aurora.Modules.Ledger.Domain")]
    [InlineData("Aurora.Web")]
    [InlineData("Aurora.Countries.Hosting")]
    [InlineData("AURORA.Countries.Hosting")]
    [InlineData("aurora.countries.hosting")]
    [InlineData("aurora.Platform.Tenancy")]
    public void An_aurora_assembly_a_package_may_not_reference_is_refused_by_the_context(string name)
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();
        CountryPackageLoadContext context = new("test", package.AssemblyPath);

        try
        {
            // The runtime wraps whatever a load context's Load override throws, so the refusal that
            // matters is inside a FileLoadException rather than being it. The loader unwraps the
            // chain for the same reason: reporting the wrapper would hide the only sentence that
            // says what went wrong.
            Exception thrown = Should.Throw<Exception>(
                () => context.LoadFromAssemblyName(new AssemblyName(name)));

            PackageReferenceRefusedException? refused = null;
            for (Exception? candidate = thrown; candidate is not null; candidate = candidate.InnerException)
            {
                refused = candidate as PackageReferenceRefusedException ?? refused;
            }

            refused.ShouldNotBeNull(
                $"loading '{name}' should have been refused by the package's own load context, but " +
                $"failed with {thrown.GetType().Name} instead");
            refused.AssemblyName.ShouldBe(name);
            refused.Message.ShouldContain(name);
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>
    /// A package may ship and use whatever third-party libraries it likes — its own context is what
    /// keeps its choice of versions away from the host's. The rule is about reaching into core, not
    /// about libraries.
    /// </summary>
    [Theory]
    [InlineData("System.Text.Json")]
    [InlineData("SomeSchematronRunner")]
    [InlineData("NodaTime")]
    public void A_library_that_is_not_ours_is_not_the_reference_rule_s_business(string name) =>
        PackageAssemblyReferenceRule.IsAllowed(name).ShouldBeTrue();

    [Theory]
    [InlineData("Aurora.Countries.Contracts")]
    [InlineData("Aurora.SharedKernel")]
    [InlineData("Aurora.Documents.Canonical")]
    [InlineData("AURORA.SharedKernel")]
    [InlineData("aurora.documents.canonical")]
    public void The_three_assemblies_a_package_may_reference_are_allowed(string name) =>
        PackageAssemblyReferenceRule.IsAllowed(name).ShouldBeTrue();

    /// <summary>
    /// An allowlist, not a blocklist. The module that has not been written yet is exactly the one
    /// nobody would think to add to a blocklist — and neither is the module spelled in a case
    /// nobody thought of, which the loader binds all the same.
    /// </summary>
    [Theory]
    [InlineData("Aurora.Modules.Sales.Application")]
    [InlineData("Aurora.Platform.Identity")]
    [InlineData("Aurora.Something.Nobody.Has.Written.Yet")]
    [InlineData("AURORA.Ledger.Infrastructure")]
    [InlineData("aurora.Ledger.Infrastructure")]
    [InlineData("aurora.modules.sales.application")]
    public void Every_other_aurora_assembly_is_forbidden(string name) =>
        PackageAssemblyReferenceRule.IsAllowed(name).ShouldBeFalse();

    [Fact]
    public void The_allowed_set_is_exactly_the_three_tier_zero_assemblies() =>
        PackageAssemblyReferenceRule.AllowedAuroraAssemblies.Count.ShouldBe(3);

    /// <summary>
    /// The allowlist is a set of simple names, and the runtime binds simple names without regard to
    /// case. A set that matched only the canonical spelling would let a case-variant of an allowed
    /// name be treated as "not ours" — and a case-variant of a forbidden one along with it.
    /// </summary>
    [Fact]
    public void The_allowed_set_matches_names_the_way_the_loader_binds_them()
    {
        PackageAssemblyReferenceRule.AllowedAuroraAssemblies.Contains("AURORA.SHAREDKERNEL").ShouldBeTrue();
        PackageAssemblyReferenceRule.AllowedAuroraAssemblies.Contains("aurora.countries.contracts").ShouldBeTrue();
    }

    /// <summary>
    /// A load context is version isolation and unloadability, and ADR-0008 §9.4 says plainly that it
    /// is not a sandbox. The collectible flag is the part that is real, so it is the part asserted.
    /// </summary>
    [Fact]
    public void A_package_context_is_collectible_and_named_for_its_package()
    {
        using PackageOnDisk package = PackageOnDisk.Deploy();
        CountryPackageLoadContext context = new("aurora.country.testland@1.4.0", package.AssemblyPath);

        try
        {
            context.IsCollectible.ShouldBeTrue();
            context.Name.ShouldBe("aurora.country.testland@1.4.0");
        }
        finally
        {
            context.Unload();
        }
    }
}
