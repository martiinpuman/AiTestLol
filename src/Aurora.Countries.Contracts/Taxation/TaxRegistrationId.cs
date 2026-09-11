using System;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Taxation;

/// <summary>
/// Identifies one tax registration a company holds (ADR-0008 §8.3, ADR-0023).
/// </summary>
/// <remarks>
/// <para>
/// A company has <b>one or more</b> tax registrations from the first migration, and every tax
/// resolution and statutory report slot is keyed by one, even though v1 only ever passes a
/// company's single primary registration. Carrying it in the signature now is what makes the
/// multi-registration case — one legal entity with obligations in two jurisdictions — behaviour and
/// UI work later, rather than a contract change that reaches every package in the fleet.
/// </para>
/// <para>
/// It lives in this assembly because this is where the seam is: the contracts a package implements
/// must name it, and a package may reference no other core assembly. When the Tax module lands with
/// a registration aggregate, this type is a candidate to move down into the shared kernel — which
/// would be a MAJOR core-contract bump, so it is worth deciding once rather than twice.
/// </para>
/// </remarks>
public readonly record struct TaxRegistrationId : IEntityId<TaxRegistrationId>
{
    /// <summary>Wraps an already-assigned identifier.</summary>
    public TaxRegistrationId(Guid value) =>
        Value = EntityId.Assigned(value, nameof(TaxRegistrationId));

    /// <summary>The identifier.</summary>
    public Guid Value { get; }

    /// <summary>Whether this is the unassigned value.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>A new identifier.</summary>
    public static TaxRegistrationId Create() => new(Guid.CreateVersion7());

    /// <summary>Wraps an already-assigned identifier.</summary>
    public static TaxRegistrationId From(Guid value) => new(value);

    /// <inheritdoc/>
    public static TaxRegistrationId Parse(string s, IFormatProvider? provider = null) =>
        EntityId.Parse<TaxRegistrationId>(s, nameof(TaxRegistrationId));

    /// <inheritdoc/>
    public static bool TryParse(string? s, IFormatProvider? provider, out TaxRegistrationId result) =>
        EntityId.TryParse(s, out result);

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
