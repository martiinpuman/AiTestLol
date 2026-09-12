using System;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// Identifies one subscription — one span of time during which a tenant is entitled to a plan
/// with a number of seats (ADR-0007 §9.2, <c>catalog.subscription</c>).
/// </summary>
/// <remarks>
/// A tenant has a history of subscriptions, not one mutable row: an upgrade ends the current one
/// and starts the next, so that billing questions about the past can be answered from the
/// catalog. The identifier makes each span addressable on its own.
/// </remarks>
public readonly record struct SubscriptionId : IEntityId<SubscriptionId>
{
    /// <summary>Wraps a UUID that identifies a subscription.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is all zeros.</exception>
    public SubscriptionId(Guid value) => Value = EntityId.Assigned(value, nameof(SubscriptionId));

    /// <inheritdoc/>
    public Guid Value { get; }

    /// <inheritdoc/>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc/>
    public static SubscriptionId Create() => new(Guid.CreateVersion7());

    /// <inheritdoc/>
    public static SubscriptionId From(Guid value) => new(value);

    /// <inheritdoc cref="IParsable{TSelf}.Parse"/>
    public static SubscriptionId Parse(string s, IFormatProvider? provider = null) =>
        EntityId.Parse<SubscriptionId>(s, nameof(SubscriptionId));

    /// <inheritdoc cref="IParsable{TSelf}.TryParse"/>
    public static bool TryParse(string? s, IFormatProvider? provider, out SubscriptionId result) =>
        EntityId.TryParse(s, out result);

    /// <summary>The UUID in its standard text form, which <see cref="Parse"/> reads back.</summary>
    public override string ToString() => Value.ToString();
}
