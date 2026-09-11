using System;

namespace Aurora.SharedKernel;

/// <summary>
/// Identifies one tenant — one paying customer of Aurora, with its own database (ADR-0007 §4).
/// </summary>
/// <remarks>
/// The most load-bearing identifier in the system: it decides which database a request talks to,
/// so mistaking it for any other id is the one mistake that could cross a tenant boundary. It is
/// never the tenant's slug, which is a routing and display concern that can be renamed; an id
/// cannot.
/// </remarks>
public readonly record struct TenantId : IEntityId<TenantId>
{
    /// <summary>Wraps a UUID that identifies a tenant.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is all zeros.</exception>
    public TenantId(Guid value) => Value = EntityId.Assigned(value, nameof(TenantId));

    /// <inheritdoc/>
    public Guid Value { get; }

    /// <inheritdoc/>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc/>
    public static TenantId Create() => new(Guid.CreateVersion7());

    /// <inheritdoc/>
    public static TenantId From(Guid value) => new(value);

    /// <inheritdoc cref="IParsable{TSelf}.Parse"/>
    public static TenantId Parse(string s, IFormatProvider? provider = null) =>
        EntityId.Parse<TenantId>(s, nameof(TenantId));

    /// <inheritdoc cref="IParsable{TSelf}.TryParse"/>
    public static bool TryParse(string? s, IFormatProvider? provider, out TenantId result) =>
        EntityId.TryParse(s, out result);

    /// <summary>The UUID in its standard text form, which <see cref="Parse"/> reads back.</summary>
    public override string ToString() => Value.ToString();
}
