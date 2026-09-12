using System.Collections.Immutable;
using Aurora.Architecture.Tests.Fixtures.Violations;
using Aurora.Architecture.Tests.Metadata;
using Aurora.Architecture.Tests.Rules;

namespace Aurora.Architecture.Tests.Fixtures;

/// <summary>
/// Populations built around the exact identities ADR-0032 §4.1 and §4.3 key on: the catalog
/// context as a (full name, assembly) pair, and <c>Aurora.Platform.Tenancy</c> as the one assembly
/// where the <c>AddDbContext</c> family may be called.
/// </summary>
/// <remarks>
/// The full-name half of each identity is compiled (<c>Fixtures/StandIns</c> declares the catalog
/// context at B-05's exact full name; <c>ContainerRegistrations</c> registers it by that name). The
/// assembly half cannot be - everything here compiles into <c>Aurora.Architecture.Tests</c> - so
/// the scanner's records are relabelled into the assembly a test needs, and nothing else about them
/// changes. Every method says which half is which.
/// </remarks>
internal static class CatalogFixture
{
    /// <summary>
    /// The exempt pair itself: the stand-in, compiled at the exempt full name, relabelled into the
    /// exempt assembly. The one <c>DbContext</c> the tenant-context rules ignore.
    /// </summary>
    public static ImmutableArray<ScannedType> TheExemptPair() =>
        [FixtureAssembly.CatalogStandIn with { AssemblyName = TenancyNames.TenancyAssemblyName }];

    /// <summary>
    /// The stand-in as compiled: the right full name in the wrong assembly, which is not the pair
    /// and so a tenant context to every rule.
    /// </summary>
    public static ImmutableArray<ScannedType> TheRightNameInTheWrongAssembly() => [FixtureAssembly.CatalogStandIn];

    /// <summary>
    /// The <c>AddDbContext*</c> calls of <see cref="ContainerRegistrations"/> relabelled into
    /// <c>Aurora.Platform.Tenancy</c>: the one site where T2 judges the argument rather than the
    /// site. Only the catalog registration passes.
    /// </summary>
    public static ImmutableArray<ScannedType> RegistrationsInsideTheTenancyAssembly() =>
        FixtureAssembly.ViolationInAssembly(nameof(ContainerRegistrations), TenancyNames.TenancyAssemblyName);
}
