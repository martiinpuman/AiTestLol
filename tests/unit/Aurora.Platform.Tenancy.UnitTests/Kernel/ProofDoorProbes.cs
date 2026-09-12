using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Contracts;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>A delegate declared without generics, so a parameter of this type names no <c>TenantScope</c> in its own type arguments.</summary>
internal delegate void ScopeCallback(TenantScope scope);

/// <summary>
/// Every shape by which a public member can hand a tenant proof to code outside the friend set,
/// plus the inbound shape and two members that mention no proof at all. Link 3's scan is proven
/// against this type: each door must be reported, the two negatives must not be.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately door-shaped fixture.</b> Nothing calls these; they exist to be scanned. The
/// first two are the shapes the PR #13 security review walked through the original scan in green:
/// an event, and a callback parameter. An event's accessors return <c>void</c> and take a non-by-ref
/// parameter, and a by-value delegate parameter looks inbound to a scan that reads only by-ref
/// parameters as outbound - yet both hand a live scope to any subscriber or callback.
/// </para>
/// <para>
/// The rest are the shapes that scan already caught (kept as controls, so a rewrite cannot lose
/// them), the shapes a factory is likely to take next (a generic constraint, a non-generic delegate,
/// a nested type), and ADR-0007 §4.5's inbound shape - a scope taken by value - which is reported
/// too, so that it must be named in the inbound allow-list rather than pass unnoticed.
/// </para>
/// </remarks>
internal static class ProofDoorProbes
{
    /// <summary>Review probe 1: an event whose handler receives a scope.</summary>
    public static event Action<TenantScope>? ScopeOpened;

    /// <summary>Review probe 2: a callback parameter, by value, that receives a scope.</summary>
    public static void WithScope(Action<TenantScope> callback) => ScopeOpened += callback;

    /// <summary>Inbound, by value: ADR-0007 §4.5's handler shape. Reported so it has to be allow-listed by name.</summary>
    public static void Raise(TenantScope scope) => ScopeOpened?.Invoke(scope);

    /// <summary>Control: an out parameter.</summary>
    public static bool TryOpen(out TenantScope? scope)
    {
        scope = null;
        return false;
    }

    /// <summary>Control: a scope buried two generic arguments deep.</summary>
    public static Task<IReadOnlyList<TenantScope>> OpenAllAsync() => Task.FromResult<IReadOnlyList<TenantScope>>([]);

    /// <summary>Control: a tuple element.</summary>
    public static (TenantScope? Scope, int Version) OpenWithVersion() => (null, 0);

    /// <summary>A generic method whose only mention of the proof is a type-parameter constraint.</summary>
    public static T OpenAs<T>()
        where T : TenantAccess =>
        throw new NotSupportedException("probe");

    /// <summary>A non-generic delegate parameter: the mention is inside the delegate's Invoke signature.</summary>
    public static void OnScope(ScopeCallback callback) => callback(null!);

    /// <summary>A property, whose getter and setter are doors in both directions.</summary>
    public static TenantScope? Current { get; set; }

    /// <summary>Negative control: mentions no proof.</summary>
    public static int Version => 0;

    /// <summary>Negative control: mentions no proof.</summary>
    public static string Describe(string what) => what;

    /// <summary>A nested public type, reached only if the scan expands nested types.</summary>
    public static class Nested
    {
        public static TenantScope? Held { get; }
    }
}
