using System;

namespace Aurora.SharedKernel;

/// <summary>
/// Identifies one company — one legal entity doing business, with its own tax registrations and
/// its own financial statements.
/// </summary>
/// <remarks>
/// A tenant may hold several companies, so a company id is meaningful only inside its tenant's
/// database. It is a separate type from <see cref="TenantId"/> precisely because the two are
/// adjacent in almost every signature in the system.
/// </remarks>
public readonly record struct CompanyId : IEntityId<CompanyId>
{
    /// <summary>Wraps a UUID that identifies a company.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is all zeros.</exception>
    public CompanyId(Guid value) => Value = EntityId.Assigned(value, nameof(CompanyId));

    /// <inheritdoc/>
    public Guid Value { get; }

    /// <inheritdoc/>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc/>
    public static CompanyId Create() => new(Guid.CreateVersion7());

    /// <inheritdoc/>
    public static CompanyId From(Guid value) => new(value);

    /// <inheritdoc cref="IParsable{TSelf}.Parse"/>
    public static CompanyId Parse(string s, IFormatProvider? provider = null) =>
        EntityId.Parse<CompanyId>(s, nameof(CompanyId));

    /// <inheritdoc cref="IParsable{TSelf}.TryParse"/>
    public static bool TryParse(string? s, IFormatProvider? provider, out CompanyId result) =>
        EntityId.TryParse(s, out result);

    /// <summary>The UUID in its standard text form, which <see cref="Parse"/> reads back.</summary>
    public override string ToString() => Value.ToString();
}
