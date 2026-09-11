using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Aurora.Countries.Contracts;
using Aurora.Countries.Contracts.Identifiers;
using Aurora.Countries.Contracts.Taxation;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Hosting.UnitTests;

/// <summary>
/// Loading a real package assembly into a real collectible load context (ADR-0008 §9.1, §9.2).
/// </summary>
public sealed class CountryPackageLoaderTests
{
    /// <summary>
    /// The whole seam in one test: a signed package on disk becomes an <see cref="ICountryPackage"/>
    /// whose extension points core can call, and whose tax rule resolves as of a business date.
    /// </summary>
    [Fact]
    public void A_signed_compatible_package_loads_and_its_extension_points_work()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(key);

        using LoadedCountryPackage loaded = Ok(Loader(package, [trusted]).Load(package.Directory));

        loaded.Manifest.Id.Value.ShouldBe("aurora.country.testland");
        loaded.Trust.Level.ShouldBe(PackageTrustLevel.FirstParty);

        ITaxRuleProvider? taxRules = loaded.Package.GetExtension<ITaxRuleProvider>();
        taxRules.ShouldNotBeNull();

        TaxCode gst = Ok(TaxCode.Create("GST"));

        TaxRuleVersion before = Ok(taxRules.Resolve(
            gst,
            CompanyId.Create(),
            TaxRegistrationId.Create(),
            new DateOnly(2009, 6, 30)));
        TaxRuleVersion after = Ok(taxRules.Resolve(
            gst,
            CompanyId.Create(),
            TaxRegistrationId.Create(),
            new DateOnly(2011, 6, 30)));

        before.Rate.AsPercentage.AsPercent.ShouldBe(12.5m);
        after.Rate.AsPercentage.AsPercent.ShouldBe(15m);
        after.TaxOn(new Money(100m, Currency.Of("NZD", 2))).ShouldBe(new Money(15m, Currency.Of("NZD", 2)));
    }

    /// <summary>
    /// The classic plugin bug, asserted rather than hoped for. If the package's load context had
    /// resolved its own copy of the contract assembly, the interface the package implements would be
    /// a different <see cref="Type"/> from the one core asks for, and this cast would fail with a
    /// message that reads like nonsense.
    /// </summary>
    [Fact]
    public void The_package_and_the_host_share_one_contract_assembly()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(key);

        using LoadedCountryPackage loaded = Ok(Loader(package, [trusted]).Load(package.Directory));

        ITaxRuleProvider taxRules = loaded.Package.GetExtension<ITaxRuleProvider>()!;
        Assembly packageAssembly = taxRules.GetType().Assembly;
        Assembly contractFromPackage = taxRules.GetType()
            .GetInterface(nameof(ITaxRuleProvider))!
            .Assembly;

        contractFromPackage.ShouldBeSameAs(typeof(ITaxRuleProvider).Assembly);
        AssemblyLoadContext.GetLoadContext(packageAssembly)
            .ShouldNotBeSameAs(AssemblyLoadContext.GetLoadContext(typeof(ITaxRuleProvider).Assembly));
    }

    [Fact]
    public void A_package_answers_only_for_the_extensions_it_has()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(key);

        using LoadedCountryPackage loaded = Ok(Loader(package, [trusted]).Load(package.Directory));

        loaded.Package.GetExtension<IIdentifierValidator>().ShouldNotBeNull();
        loaded.Package.GetExtension<ITaxCategoryMapping>().ShouldBeNull();

        loaded.Package.Declares(CountryPackageCapability.TaxRuleSet).ShouldBeTrue();
        loaded.Package.Declares(CountryPackageCapability.StatutoryReport).ShouldBeFalse();
    }

    /// <summary>
    /// The identifier validator the package ships can actually reject, which is what makes the
    /// extension point worth having. A validator that says yes to everything proves nothing.
    /// </summary>
    [Fact]
    public void The_package_s_identifier_validator_accepts_and_rejects()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(key);

        using LoadedCountryPackage loaded = Ok(Loader(package, [trusted]).Load(package.Directory));

        IIdentifierValidator validator = loaded.Package.GetExtension<IIdentifierValidator>()!;

        // The fixture publishes its own check-digit function, so the good value is computed rather
        // than copied - a hand-written vector with a wrong check digit would make this test pass for
        // the wrong reason.
        MethodInfo checkDigit = validator.GetType().GetMethod("CheckDigitFor", BindingFlags.Public | BindingFlags.Static)!;
        int digit = (int)checkDigit.Invoke(null, ["1234567"])!;

        validator.Validate("1234567" + digit.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Status.ShouldBe(IdentifierStatus.Verified);
        validator.Validate("12345678901").Status.ShouldBe(IdentifierStatus.Rejected);
        validator.Validate("abcdefgh").Status.ShouldBe(IdentifierStatus.Rejected);
    }

    /// <summary>
    /// The acceptance criterion, end to end: install refused against an out-of-range core contract,
    /// with both versions named — and nothing loaded.
    /// </summary>
    [Fact]
    public void A_package_built_for_another_core_contract_is_refused_before_anything_loads()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(key);

        CountryPackageLoader loader = new(Options(package, [trusted], allowUnsigned: false), "7.3.1");

        Result<LoadedCountryPackage> loaded = loader.Load(package.Directory);

        loaded.IsFailure.ShouldBeTrue();
        loaded.Error.Code.ShouldBe(HostingErrors.IncompatibleCoreContractCode);
        loaded.Error.Description.ShouldContain("[1.0.0, 2.0.0)");
        loaded.Error.Description.ShouldContain("7.3.1");
    }

    [Fact]
    public void An_unsigned_package_does_not_load_in_production()
    {
        using TestTrustStore store = new();
        (_, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();

        Result<LoadedCountryPackage> loaded = Loader(package, [trusted]).Load(package.Directory);

        loaded.IsFailure.ShouldBeTrue();
        loaded.Error.Code.ShouldBe(HostingErrors.UntrustedCode);
    }

    /// <summary>
    /// Deactivating a package has to be able to unload its code, which is the only reason the load
    /// context is collectible at all (ADR-0008 §9.1). Proving it means proving nothing holds a
    /// reference: the package, its extensions and the handle all go out of scope inside a method
    /// that returns only a weak reference.
    /// </summary>
    [Fact]
    public void A_deactivated_package_s_load_context_is_actually_collectible()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(key);

        WeakReference context = LoadAndUnload(Loader(package, [trusted]), package.Directory);

        for (int attempt = 0; attempt < 20 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        context.IsAlive.ShouldBeFalse(
            "the package's AssemblyLoadContext is still alive after Dispose and twenty collections, " +
            "so unloading a package does not actually release its code");
    }

    [Fact]
    public void Disposing_a_loaded_package_twice_is_harmless()
    {
        using TestTrustStore store = new();
        (ECDsa key, TrustedPackageKey trusted) = store.Add(PackageTrustLevel.FirstParty);

        using PackageOnDisk package = PackageOnDisk.Deploy();
        package.SignWith(key);

        LoadedCountryPackage loaded = Ok(Loader(package, [trusted]).Load(package.Directory));

        loaded.IsUnloadRequested.ShouldBeFalse();
        loaded.Dispose();
        loaded.IsUnloadRequested.ShouldBeTrue();
        Should.NotThrow(loaded.Dispose);
    }

    // Everything the load produced has to be unreachable when this returns, or the collectibility
    // assertion above would be measuring the test's own locals rather than the runtime's behaviour.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndUnload(CountryPackageLoader loader, string directory)
    {
        LoadedCountryPackage loaded = Ok(loader.Load(directory));
        WeakReference context = loaded.LoadContextHandle;

        loaded.Package.GetExtension<ITaxRuleProvider>().ShouldNotBeNull();
        loaded.Dispose();

        return context;
    }

    private static CountryPackageLoader Loader(PackageOnDisk package, IReadOnlyList<TrustedPackageKey> keys) =>
        new(Options(package, keys, allowUnsigned: false));

    private static CountryPackageHostOptions Options(
        PackageOnDisk package,
        IReadOnlyList<TrustedPackageKey> keys,
        bool allowUnsigned) =>
        Ok(CountryPackageHostOptions.Create(
            package.Root,
            allowUnsigned ? CountryPackageHostOptions.DevelopmentEnvironmentName : "Production",
            allowUnsigned,
            keys));

    private static T Ok<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Expected success, got: {result.Error}");
}
