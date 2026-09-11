using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aurora.Architecture.Tests.Fixtures.Violations;

/// <summary>
/// Stand-in for the real <c>TenantScope</c> that arrives with B-06 (ADR-0007 §3.4).
/// </summary>
/// <remarks>
/// The tenancy rules match this type by <b>simple name</b>, exactly as they will match B-06's, so
/// this stand-in exercises the real matching path. It is a stand-in and not the real type for one
/// reason: the real type does not exist yet, and a rule proven only against a type that does not
/// exist is a rule proven against nothing.
/// </remarks>
internal sealed class TenantScope : IAsyncDisposable
{
    public bool IsActive { get; private set; } = true;

    public ValueTask DisposeAsync()
    {
        IsActive = false;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Stand-in for ADR-0027's <c>TenantDatabaseHandle</c>: the DDL-path proof, which is a direct
/// aurora_migrator connection with no schema-version gate.
/// </summary>
/// <remarks>Matched by simple name, like <see cref="TenantScope"/> and for the same reason.</remarks>
internal sealed class TenantDatabaseHandle
{
    public string DatabaseName { get; init; } = string.Empty;
}

/// <summary>Stand-in for the real <c>ITenantDbContextFactory&lt;&gt;</c> (ADR-0007 §4.1).</summary>
/// <remarks>Matched by simple name, like <see cref="TenantScope"/> and for the same reason.</remarks>
internal interface ITenantDbContextFactory<TContext>
    where TContext : DbContext
{
    ValueTask<TContext> CreateAsync(TenantScope scope, CancellationToken cancellationToken);
}

/// <summary>
/// A tenant <c>DbContext</c> with a public constructor: anyone can build one, bound to no tenant.
/// </summary>
/// <remarks><b>Deliberately violating fixture (T1).</b> Derives from the real EF Core DbContext.</remarks>
internal sealed class TenantDbContextWithAPublicConstructor : DbContext
{
    public TenantDbContextWithAPublicConstructor(DbContextOptions options)
        : base(options)
    {
    }
}

/// <summary>A tenant <c>DbContext</c> with a protected constructor - reachable by deriving from it.</summary>
/// <remarks><b>Deliberately violating fixture (T1).</b></remarks>
internal class TenantDbContextWithAProtectedConstructor : DbContext
{
    protected TenantDbContextWithAProtectedConstructor(DbContextOptions options)
        : base(options)
    {
    }
}

/// <summary>
/// The shape ADR-0007 §4.1 requires, two inheritance steps from <c>DbContext</c>.
/// </summary>
/// <remarks>
/// <b>Deliberately compliant fixture (T1).</b> It proves two things the violating fixtures cannot:
/// that the rule stays silent on a correct context, and that the base-type walk crosses more than
/// one level - a rule that only looked at the immediate base type would not see this as a tenant
/// context at all, and would report nothing about it whatever its constructors looked like.
/// </remarks>
internal sealed class TenantDbContextWithAnInternalConstructor : TenantDbContextWithAProtectedConstructor
{
    internal TenantDbContextWithAnInternalConstructor(DbContextOptions options)
        : base(options)
    {
    }
}

/// <summary>
/// A context called <c>CatalogDbContext</c> - but not at the catalog's own full name.
/// </summary>
/// <remarks>
/// <b>Deliberately compliant fixture (T1, T2) when exempted by its full name, and a deliberately
/// violating one otherwise.</b> Passed to the rules as the exemption, it proves the allow-list is
/// an allow-list: public constructor and conventional registration, both ignored. Left to the
/// production exemption - which names <c>Aurora.Platform.Tenancy.Catalog.CatalogDbContext</c> and
/// not this type - it is the re-review's m-1 attack: a product-catalog context in some module that
/// borrowed the name, which T1 and T2 must treat as the tenant context it is.
/// </remarks>
internal sealed class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions options)
        : base(options)
    {
    }
}

/// <summary>Calls to the <c>AddDbContext</c> family, which ADR-0032 §4.1 confines to one assembly and one argument.</summary>
/// <remarks>
/// <b>Deliberately violating fixture (T2).</b> This class is compiled into
/// <c>Aurora.Architecture.Tests</c>, so every call in it is outside <c>Aurora.Platform.Tenancy</c>
/// and T2 reports all of them, whatever they register - the call site, not the argument, is what
/// the rule judges. The tests relabel the class into the tenancy assembly to exercise the one
/// permitted shape (<c>CatalogFixture.RegistrationsInsideTheTenancyAssembly</c>), where only
/// <see cref="RegisterCatalog"/> falls silent.
/// </remarks>
internal static class ContainerRegistrations
{
    /// <summary>The violation: a tenant context put in the container.</summary>
    public static void RegisterTenantContext(IServiceCollection services) =>
        services.AddDbContext<TenantDbContextWithAnInternalConstructor>();

    /// <summary>The violation again, in pooled-factory form.</summary>
    public static void RegisterTenantContextFactory(IServiceCollection services) =>
        services.AddDbContextFactory<TenantDbContextWithAPublicConstructor>();

    /// <summary>
    /// The catalog registration ADR-0003 rule 3 prescribes: the one <c>AddDbContext</c> call that is
    /// permitted, and only from inside <c>Aurora.Platform.Tenancy</c>. Names the stand-in compiled
    /// at B-05's exact full name (<c>Fixtures/StandIns</c>).
    /// </summary>
    public static void RegisterCatalog(IServiceCollection services) =>
        services.AddDbContext<global::Aurora.Platform.Tenancy.Catalog.CatalogDbContext>();

    /// <summary>
    /// ADR-0032 §4.1.2 (b): the helper B-06 must not write. Its body forwards a type parameter into
    /// <c>AddDbContext</c>; a rule keyed on the argument sees <c>!!0</c> and nothing to match, a rule
    /// keyed on the call site reports the body wherever it lives.
    /// </summary>
    public static IServiceCollection AddTenantDbContext<TContext>(this IServiceCollection services)
        where TContext : DbContext =>
        services.AddDbContext<TContext>();

    /// <summary>
    /// The helper's call site, where the concrete context finally appears. Its member name is not
    /// <c>AddDbContext*</c>, so it is outside T2's population by design; ADR-0032 §4.1.1 assigns the
    /// call site to T9.
    /// </summary>
    public static void RegisterThroughTheHelper(IServiceCollection services) =>
        services.AddTenantDbContext<TenantDbContextWithAnInternalConstructor>();
}

/// <summary>A second door to a tenant <c>DbContext</c>, outside Aurora.Platform.Tenancy.</summary>
/// <remarks><b>Deliberately violating fixture (T3).</b></remarks>
internal sealed class FactoryImplementedInTheWrongAssembly
    : ITenantDbContextFactory<TenantDbContextWithAnInternalConstructor>
{
    public ValueTask<TenantDbContextWithAnInternalConstructor> CreateAsync(
        TenantScope scope,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("fixture");
}

/// <summary>ADR-0027's DDL-path sibling of the factory, implemented outside the tenancy assembly.</summary>
/// <remarks><b>Deliberately violating fixture (T3).</b></remarks>
internal interface ITenantMigrationContextFactory<TContext>
    where TContext : DbContext
{
    ValueTask<TContext> CreateAsync(TenantDatabaseHandle handle, CancellationToken cancellationToken);
}

/// <summary>The DDL-path factory, in the wrong assembly, which ADR-0027 §1 bans exactly as T3 bans the other.</summary>
/// <remarks><b>Deliberately violating fixture (T3).</b></remarks>
internal sealed class MigrationFactoryImplementedInTheWrongAssembly
    : ITenantMigrationContextFactory<TenantDbContextWithAnInternalConstructor>
{
    public ValueTask<TenantDbContextWithAnInternalConstructor> CreateAsync(
        TenantDatabaseHandle handle,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("fixture");
}

/// <summary>A module reaching for the DDL path, which ADR-0027 §1 confines to a named allow-list.</summary>
/// <remarks>
/// <b>Deliberately violating fixture (T6).</b> The signature is <c>(string) -&gt; bool</c>: only the
/// local and the instructions name the banned type, so a rule reading signatures reports nothing.
/// </remarks>
internal static class ModuleReachingForTheDdlPath
{
    public static bool LooksLikeOurs(string databaseName)
    {
        TenantDatabaseHandle handle = new() { DatabaseName = databaseName };
        return handle.DatabaseName.StartsWith("aurora_", StringComparison.Ordinal);
    }
}

/// <summary>A singleton holding the DDL-path proof, which is T5's defect with more authority.</summary>
/// <remarks><b>Deliberately violating fixture (T5).</b></remarks>
internal sealed class SingletonCacheHoldingAHandle
{
    private TenantDatabaseHandle? _handle;

    public void Remember(TenantDatabaseHandle handle) => _handle = handle;

    public bool Knows(TenantDatabaseHandle handle) => ReferenceEquals(_handle, handle);
}

/// <summary>A tenant scope parked in a static field, where it outlives every request.</summary>
/// <remarks><b>Deliberately violating fixture (T5, static half).</b></remarks>
internal static class AmbientScopeHolder
{
    private static TenantScope? _current;

    public static TenantScope? Current
    {
        get => _current;
        set => _current = value;
    }
}

/// <summary>A cache registered as a singleton, holding the scope of whichever tenant arrived first.</summary>
/// <remarks><b>Deliberately violating fixture (T5, container half).</b></remarks>
internal sealed class SingletonCacheHoldingAScope
{
    private TenantScope? _scope;

    public void Remember(TenantScope scope) => _scope = scope;

    public bool Knows(TenantScope scope) => ReferenceEquals(_scope, scope);
}

/// <summary>A per-request type holding a scope, which is correct and must not be reported.</summary>
/// <remarks><b>Deliberately compliant fixture (T5).</b></remarks>
internal sealed class ScopedHandlerHoldingAScope
{
    private readonly TenantScope _scope;

    public ScopedHandlerHoldingAScope(TenantScope scope) => _scope = scope;

    public bool IsUsable => _scope.IsActive;
}

/// <summary>The lifetime registrations the T5 fixtures depend on.</summary>
/// <remarks><b>Deliberately violating fixture (T5).</b></remarks>
internal static class LifetimeRegistrations
{
    /// <summary>The violation: a singleton that holds a scope.</summary>
    public static void RegisterSingletonCache(IServiceCollection services) =>
        services.AddSingleton<SingletonCacheHoldingAScope>();

    /// <summary>The violation again, holding ADR-0027's DDL-path proof instead of a scope.</summary>
    public static void RegisterSingletonHandleCache(IServiceCollection services) =>
        services.AddSingleton<SingletonCacheHoldingAHandle>();

    /// <summary>Compliant: a scoped registration of a type that holds a scope is exactly right.</summary>
    public static void RegisterScopedHandler(IServiceCollection services) =>
        services.AddScoped<ScopedHandlerHoldingAScope>();
}

/// <summary>A component reading the tenant out of the HTTP context, which a Blazor circuit has not got.</summary>
/// <remarks><b>Deliberately violating fixture (T4).</b> Uses the real ASP.NET Core type.</remarks>
internal sealed class ComponentReadingTheHttpContext
{
    private readonly IHttpContextAccessor _accessor;

    public ComponentReadingTheHttpContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public string? TenantHeader => _accessor.HttpContext?.Request.Headers["X-Tenant"];
}

/// <summary>The same violation hidden in a method body, with nothing in the signature.</summary>
/// <remarks>
/// <b>Deliberately violating fixture (T4).</b> The signature is
/// <c>(IServiceProvider) -&gt; string?</c>; only the local and the instructions name the banned type.
/// </remarks>
internal static class ServiceLocatorReachingForTheHttpContext
{
    public static string? TenantHeader(IServiceProvider services)
    {
        IHttpContextAccessor accessor = services.GetRequiredService<IHttpContextAccessor>();
        return accessor.HttpContext?.Request.Headers["X-Tenant"];
    }
}

/// <summary>Domain code reading the ambient clock instead of a <c>TimeProvider</c>.</summary>
/// <remarks><b>Deliberately violating fixture (S3).</b></remarks>
internal static class PeriodCloseReadingTheAmbientClock
{
    public static bool IsPastCutOff(DateTimeOffset cutOff) => DateTimeOffset.UtcNow > cutOff;

    public static DateTime Stamp() => DateTime.Now;

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);
}

/// <summary>The same code, taking time as a parameter, which is how it is supposed to look.</summary>
/// <remarks><b>Deliberately compliant fixture (S3).</b></remarks>
internal static class PeriodCloseTakingTimeAsAParameter
{
    public static bool IsPastCutOff(TimeProvider time, DateTimeOffset cutOff) =>
        time.GetUtcNow() > cutOff;
}
