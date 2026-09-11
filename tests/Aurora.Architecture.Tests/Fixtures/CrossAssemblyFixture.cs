using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Fixtures.Violations;
using Aurora.Architecture.Tests.Metadata;
using Aurora.Architecture.Tests.Rules;

namespace Aurora.Architecture.Tests.Fixtures;

/// <summary>
/// The one tenancy fixture that cannot be compiled: a module <c>DbContext</c> whose base type lives
/// in a different assembly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> Every compiled fixture in <c>Fixtures/Violations</c> reaches
/// <c>Microsoft.EntityFrameworkCore.DbContext</c> without the base-type walk ever having to
/// resolve a type from another assembly: each hop either matches <c>DbContext</c> by name or stays
/// inside the fixture assembly. B-06's shape is different - <c>SalesDbContext</c> in
/// <c>Aurora.Modules.Sales.Infrastructure</c> derives from <c>ModuleDbContext</c> in
/// <c>Aurora.Platform.Tenancy</c> - and <c>docs/reviews/B-04-rereview.md</c> m-3 showed that a
/// walk which gave up at that boundary left the whole suite green while T1 and T2 no longer
/// recognised a single module context. This fixture is the hop the compiled ones never take.
/// </para>
/// <para>
/// <b>Why it is not hand-written.</b> The fixture assembly cannot derive from a type in an assembly
/// that does not exist yet, so the two records are real scanner output for two compiled fixture
/// types - constructors, accessibility flags and all - with exactly three identity fields relabelled
/// to B-06's names: <c>FullName</c>, <c>AssemblyName</c> and <c>BaseTypeName</c>. Nothing about
/// what the compiler emitted for their members is invented.
/// </para>
/// </remarks>
internal static class CrossAssemblyFixture
{
    public const string TenancyBaseFullName = "Aurora.Platform.Tenancy.ModuleDbContext";

    public const string ModuleContextFullName = "Aurora.Modules.Sales.Infrastructure.SalesDbContext";

    public const string ModuleAssemblyName = "Aurora.Modules.Sales.Infrastructure";

    /// <summary>
    /// <c>SalesDbContext</c> (public constructor, module assembly) deriving from
    /// <c>ModuleDbContext</c> (internal constructor, tenancy assembly) deriving from EF Core's
    /// <c>DbContext</c>. Recognising the module context takes one cross-assembly hop.
    /// </summary>
    public static ImmutableArray<ScannedType> ModuleContextAndItsTenancyBase()
    {
        ScannedType tenancyBase = FixtureAssembly.Violation(nameof(TenantDbContextWithAnInternalConstructor)).Single() with
        {
            FullName = TenancyBaseFullName,
            AssemblyName = TenancyNames.TenancyAssemblyName,
            BaseTypeName = TenancyNames.DbContext,
        };

        ScannedType moduleContext = FixtureAssembly.Violation(nameof(TenantDbContextWithAPublicConstructor)).Single() with
        {
            FullName = ModuleContextFullName,
            AssemblyName = ModuleAssemblyName,
            BaseTypeName = TenancyBaseFullName,
        };

        return [tenancyBase, moduleContext];
    }

    /// <summary>
    /// A type in one assembly deriving from a type in another that declares the field. The T5
    /// question - does a singleton hold a scope - is asked through the same walk.
    /// </summary>
    public static ImmutableArray<ScannedType> HolderDerivingFromABaseInAnotherAssembly()
    {
        ScannedType baseWithTheField = FixtureAssembly.Violation(nameof(SingletonCacheHoldingAScope)).Single() with
        {
            FullName = "Aurora.Platform.Tenancy.ScopeCacheBase",
            AssemblyName = TenancyNames.TenancyAssemblyName,
        };

        ScannedType derivedWithoutIt = FixtureAssembly.Violation(nameof(PeriodCloseTakingTimeAsAParameter)).Single() with
        {
            FullName = "Aurora.Modules.Sales.Infrastructure.SalesScopeCache",
            AssemblyName = ModuleAssemblyName,
            BaseTypeName = baseWithTheField.FullName,
        };

        return [baseWithTheField, derivedWithoutIt];
    }
}
