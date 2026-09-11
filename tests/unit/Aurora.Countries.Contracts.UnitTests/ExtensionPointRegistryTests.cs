using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Aurora.Countries.Contracts.Documents;
using Aurora.Countries.Contracts.Taxation;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Contracts.UnitTests;

/// <summary>
/// The ten extension points of ADR-0008 §7, and the map from a declared capability to the interface
/// that fills it.
/// </summary>
public sealed class ExtensionPointRegistryTests
{
    /// <summary>
    /// Ten capabilities and ten interfaces, one each. The count is asserted rather than implied:
    /// "the ten extension points" is the claim the whole Country Package model rests on, and a test
    /// that only checked the map was consistent would stay green at nine.
    /// </summary>
    [Fact]
    public void There_are_ten_extension_points_and_each_has_exactly_one_interface()
    {
        CountryPackageCapabilities.All.Count.ShouldBe(10);

        Type[] contracts =
        [
            .. CountryPackageCapabilities.All.Select(CountryPackageCapabilities.ContractTypeFor),
        ];

        contracts.Length.ShouldBe(10);
        contracts.Distinct().Count().ShouldBe(10);
        contracts.ShouldAllBe(contract => contract.IsInterface);
        contracts.ShouldAllBe(contract =>
            contract.Assembly == typeof(CoreContract).Assembly);
    }

    /// <summary>
    /// The map is built from the attributes on the interfaces, so an interface marked with a
    /// capability nobody declares, or a capability with two interfaces, fails at initialisation
    /// rather than at the first install.
    /// </summary>
    [Fact]
    public void Every_marked_interface_is_reachable_from_the_capability_it_claims()
    {
        IEnumerable<Type> marked = typeof(CoreContract).Assembly
            .GetExportedTypes()
            .Where(type => type.GetCustomAttribute<ExtensionPointAttribute>() is not null);

        foreach (Type contract in marked)
        {
            CountryPackageCapability capability =
                contract.GetCustomAttribute<ExtensionPointAttribute>()!.Capability;

            CountryPackageCapabilities.ContractTypeFor(capability).ShouldBe(contract);
            CountryPackageCapabilities.CapabilityOf(contract).ShouldBe(capability);
        }
    }

    /// <summary>
    /// The e-invoicing profile is generic in the document it profiles, so the registry holds its
    /// open definition and a closed implementation still resolves to the same capability.
    /// </summary>
    [Fact]
    public void A_generic_extension_point_resolves_through_its_definition()
    {
        Type registered =
            CountryPackageCapabilities.ContractTypeFor(CountryPackageCapability.EInvoicingProfile);

        registered.IsGenericTypeDefinition.ShouldBeTrue();
        CountryPackageCapabilities.CapabilityOf(typeof(IEInvoicingProfile<string>))
            .ShouldBe(CountryPackageCapability.EInvoicingProfile);
    }

    [Fact]
    public void A_type_that_is_not_an_extension_point_maps_to_nothing() =>
        CountryPackageCapabilities.CapabilityOf(typeof(ITaxCategoryMapping)).ShouldBeNull();

    [Fact]
    public void A_capability_outside_the_enum_is_refused() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => CountryPackageCapabilities.ContractTypeFor((CountryPackageCapability)999));

    /// <summary>
    /// A package fills a slot by role, never by a literal. This is the mechanical reading of "the
    /// core must never contain <c>if (country == "SE")</c>": every extension point either takes or
    /// returns a core-defined slot type, and none of them takes a country code as an argument.
    /// </summary>
    [Fact]
    public void No_extension_point_takes_a_country_code_as_an_argument()
    {
        List<string> offenders = [];

        foreach (CountryPackageCapability capability in CountryPackageCapabilities.All)
        {
            Type contract = CountryPackageCapabilities.ContractTypeFor(capability);

            foreach (MethodInfo method in contract.GetMethods())
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    if (parameter.Name is not null
                        && parameter.Name.Contains("country", StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{contract.Name}.{method.Name}({parameter.Name})");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "core asks a package what to do, never which country it is in — the package already " +
            "knows, and a country argument is the branch this contract exists to prevent");
    }
}
