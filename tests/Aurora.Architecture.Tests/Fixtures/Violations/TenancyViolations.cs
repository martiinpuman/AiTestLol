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

/// <summary>The catalog context: the one context ADR-0003 rule 3 registers conventionally.</summary>
/// <remarks>
/// <b>Deliberately compliant fixture (T1, T2).</b> Public constructor and a conventional
/// registration, both of which the rules must ignore - so the allow-list is proven to be an
/// allow-list rather than a blanket exemption.
/// </remarks>
internal sealed class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions options)
        : base(options)
    {
    }
}

/// <summary>Registrations of tenant contexts in the DI container.</summary>
/// <remarks><b>Deliberately violating fixture (T2),</b> with one compliant registration beside it.</remarks>
internal static class ContainerRegistrations
{
    /// <summary>The violation: a tenant context put in the container.</summary>
    public static void RegisterTenantContext(IServiceCollection services) =>
        services.AddDbContext<TenantDbContextWithAnInternalConstructor>();

    /// <summary>The violation again, in pooled-factory form.</summary>
    public static void RegisterTenantContextFactory(IServiceCollection services) =>
        services.AddDbContextFactory<TenantDbContextWithAPublicConstructor>();

    /// <summary>Compliant: the catalog is the one context that is registered.</summary>
    public static void RegisterCatalog(IServiceCollection services) =>
        services.AddDbContext<CatalogDbContext>();
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
