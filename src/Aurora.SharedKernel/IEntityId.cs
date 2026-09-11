using System;

namespace Aurora.SharedKernel;

/// <summary>
/// What every strongly-typed identifier in Aurora is: a value type wrapping one time-ordered
/// UUIDv7, which nothing else can be mistaken for.
/// </summary>
/// <typeparam name="TSelf">The identifier type itself.</typeparam>
/// <remarks>
/// <para>
/// ADR-0007 §4 fixes the shape for <c>TenantId</c> and the same reasoning holds for every other
/// identifier in the system: never a <see cref="string"/>, never an <see cref="int"/>, never a
/// human-facing code that can be renamed. Passing a company's id where a tenant's was wanted is
/// the kind of mistake that crosses a tenant boundary, so it is made a compile error rather than
/// a code-review responsibility.
/// </para>
/// <para>
/// <b>The identifiers are cheap.</b> Each is a <c>readonly record struct</c> over a single
/// <see cref="Guid"/>: no allocation to create one, no allocation to pass one, and equality and
/// hashing come straight from the <see cref="Guid"/>.
/// </para>
/// <para>
/// <b>They convert for EF Core without a corner to paint into.</b> The two halves of a value
/// conversion are <c>id =&gt; id.Value</c> and <c>value =&gt; From(value)</c>. Both are available
/// here, so one converter can serve every identifier in the system rather than one per type, and
/// the <c>value =&gt; From(value)</c> half is reachable as a delegate (<c>Func&lt;Guid, TSelf&gt;
/// factory = TSelf.From;</c>) for the case where the expression tree an ORM wants may not name a
/// static abstract member directly.
/// </para>
/// <para>
/// <see cref="IParsable{TSelf}"/> comes with the contract so that an identifier binds from a route
/// segment, a query string or a configuration value without hand-written plumbing, and so that
/// <see cref="object.ToString"/> and <see cref="IParsable{TSelf}.Parse"/> are inverses — an
/// identifier whose text form cannot be read back is a trap.
/// </para>
/// </remarks>
public interface IEntityId<TSelf> : IParsable<TSelf>
    where TSelf : struct, IEntityId<TSelf>
{
    /// <summary>The underlying UUID, as stored and as transported.</summary>
    Guid Value { get; }

    /// <summary>
    /// Whether this identifier identifies nothing. <see langword="true"/> only for the struct
    /// default, which no factory here can produce.
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// Mints a new identifier: a UUIDv7, time-ordered so that inserts land at the end of the index
    /// rather than scattering across it (ADR-0007 §4).
    /// </summary>
    static abstract TSelf Create();

    /// <summary>
    /// Rebuilds an identifier from a stored or transported <see cref="Guid"/> — the read half of a
    /// value conversion.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is all zeros.</exception>
    static abstract TSelf From(Guid value);
}
