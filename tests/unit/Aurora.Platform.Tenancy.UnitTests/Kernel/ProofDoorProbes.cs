using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Contracts;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>A delegate declared without generics, so a parameter of this type names no <c>TenantScope</c> in its own type arguments.</summary>
internal delegate void ScopeCallback(TenantScope scope);

/// <summary>
/// A non-generic interface declared outside the scanned population, whose one member names a
/// proof: a type implementing it explicitly has a public surface, an inheritance list and a
/// derivability verdict that are all silent about the door (fourth review).
/// </summary>
internal interface IScopeSource
{
    TenantScope? Provide();
}

/// <summary>
/// A base declared outside the scanned set, carrying the explicit implementation its derived types
/// are reached through and leaving the scope itself to a hook only a derived type supplies (fifth
/// review, second shape).
/// </summary>
internal abstract class ScopeSourceBase : IScopeSource
{
    TenantScope? IScopeSource.Provide() => Hook();

    protected abstract TenantScope? Hook();
}

/// <summary>
/// Every shape by which a public member or a public type can hand a tenant proof to code outside
/// the friend set, plus the inbound shape and members that mention no proof at all. Link 3's two
/// scans are proven against this type: each door must be reported, the negatives must not be.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately door-shaped fixture.</b> Nothing calls these; they exist to be scanned. The
/// first two are the shapes PR #13's first security review walked through the original scan in
/// green: an event, and a callback parameter. The three collections are the second review's
/// door: types that declare nothing and hand proofs out through what they inherit, which a scan of
/// declared members cannot see. <see cref="ScopeHost"/> is the third review's: a public type nobody
/// sealed, whose protected members anyone deriving from it can reach, which a scan of public
/// members cannot see and the derivability check must. <see cref="ExplicitScopeSource"/> is the
/// fourth review's: a sealed type whose only door is an explicitly implemented member of an
/// interface declared elsewhere, private in IL and named by nothing the type inherits, which only
/// the interface map reaches. <see cref="OpenedHost"/> is that review's nit: a derivable type whose
/// protected doors are all inherited, which the verdict must still list.
/// <see cref="IDimScopeSource"/> and <see cref="InheritedExplicitSource"/> are the fifth review's:
/// an explicit implementation carried by an interface as a default interface member, which no
/// interface map describes, and one inherited from a base outside the scanned set, which no type
/// in the set would otherwise examine.
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

    /// <summary>
    /// Second-review probe, member half: a signature that names no proof and returns a type that
    /// inherits one. The member is silent by design; the type it returns is what the scan reports.
    /// </summary>
    public static OpenScopeCollection All() => new();

    /// <summary>The same, through an interface rather than a base type.</summary>
    public static ExplicitScopeCollection Bag() => new();

    /// <summary>Negative control: mentions no proof.</summary>
    public static int Version => 0;

    /// <summary>Negative control: mentions no proof.</summary>
    public static string Describe(string what) => what;

    /// <summary>A nested public type, reached only if the scan expands nested types.</summary>
    public static class Nested
    {
        public static TenantScope? Held { get; }
    }

    /// <summary>
    /// Second-review probe, type half: declares nothing, inherits every member of a list of
    /// scopes. Deliberately not sealed, so that <see cref="DeeperCollection"/> can derive from it -
    /// which also makes it a type the derivability check must report.
    /// </summary>
    public class OpenScopeCollection : List<TenantScope>;

    /// <summary>One step further down the chain, so the base walk must not stop at the first base type.</summary>
    public sealed class DeeperCollection : OpenScopeCollection;

    /// <summary>
    /// Every interface member implemented explicitly, so the only public member mentions nothing;
    /// the interface list is the only place the proof appears.
    /// </summary>
    public sealed class ExplicitScopeCollection : IReadOnlyCollection<TenantScope>
    {
        private readonly List<TenantScope> _scopes = [];

        int IReadOnlyCollection<TenantScope>.Count => _scopes.Count;

        public override string ToString() => "bag";

        IEnumerator<TenantScope> IEnumerable<TenantScope>.GetEnumerator() => _scopes.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _scopes.GetEnumerator();
    }

    /// <summary>
    /// Fourth-review probe: sealed, no public member, no proof-bearing base or interface - and one
    /// explicitly implemented interface member that hands a scope to anyone holding the interface.
    /// </summary>
    public sealed class ExplicitScopeSource : IScopeSource
    {
        TenantScope? IScopeSource.Provide() => null;
    }

    /// <summary>
    /// Fifth-review probe, first shape: a public interface whose only door is a default interface
    /// member explicitly implementing a proof-returning member of an interface outside the scanned
    /// set. Private in IL, no interface map to walk, no protected member, nothing proof-bearing
    /// among its base interfaces: only the declared-with-a-body walk reaches it.
    /// </summary>
    public interface IDimScopeSource : IScopeSource
    {
        TenantScope? IScopeSource.Provide() => null;
    }

    /// <summary>
    /// Fifth-review probe, second shape: sealed and declaring no door of its own - the explicit
    /// implementation it is reached through is <see cref="ScopeSourceBase"/>'s, and that base is
    /// outside the scanned set, so nothing examines it there.
    /// </summary>
    public sealed class InheritedExplicitSource : ScopeSourceBase
    {
        protected override TenantScope? Hook() => null;
    }

    /// <summary>
    /// Third-review probe: a public type nobody sealed, with three protected doors. The member scan
    /// sees none of them (protected is not public); the derivability check reports the type, because
    /// anyone deriving from it reaches all three.
    /// </summary>
    public abstract class ScopeHost
    {
        protected TenantScope? Held { get; set; }

        protected internal TenantScope? Shared { get; set; }

        protected static void With(Action<TenantScope> callback) => callback(null!);
    }

    /// <summary>
    /// Control: <c>TenantAccess</c>'s own shape - abstract, with an internal constructor, so nothing
    /// outside the assembly can derive from it and its protected member is unreachable. Not reported.
    /// </summary>
    public abstract class LockedHost
    {
        internal LockedHost()
        {
        }

        protected TenantScope? Held { get; }
    }

    /// <summary>
    /// Fourth-review nit probe: derivable, declaring no protected member of its own - every door it
    /// exposes is <see cref="LockedHost"/>'s. Reported, and the verdict must name the inherited door.
    /// </summary>
    public class OpenedHost : LockedHost
    {
        public OpenedHost()
        {
        }
    }

    /// <summary>
    /// Control: sealed at the end of the chain, so nothing derives from it and the protected door it
    /// inherits is unreachable through it. Not reported.
    /// </summary>
    public sealed class ClosedHost : OpenedHost;

    /// <summary>Control: sealed, with a public constructor. Not derivable, not reported.</summary>
    public sealed class SealedHost
    {
        public SealedHost()
        {
        }
    }
}
