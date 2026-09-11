using System;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// One span of entitlement: a tenant is on <see cref="Plan"/> with <see cref="Seats"/> seats from
/// <see cref="ValidFrom"/> until <see cref="ValidTo"/> (ADR-0007 §9.2, ADR-0011 feature-flag rule 3).
/// </summary>
/// <remarks>
/// <para>
/// Validity is half-open — <c>[valid_from, valid_to)</c> — like every period in the kernel's
/// <c>DateRange</c>, so consecutive subscriptions tile without a day counted twice. A null
/// <see cref="ValidTo"/> is open-ended. The catalog refuses two subscriptions of one tenant whose
/// spans overlap (an exclusion constraint), so "which plan is this tenant on today" has one answer.
/// </para>
/// <para>
/// Calendar dates, not instants: a subscription starts on a day, and which instant that is depends
/// on the customer's time zone, which is billing's concern (ADR-0004 rule 4).
/// </para>
/// </remarks>
internal sealed class Subscription
{
    public const int MaxPlanLength = 64;

    private Subscription()
    {
    }

    public SubscriptionId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public string Plan { get; private set; } = null!;

    public int Seats { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }

    /// <exception cref="ArgumentException">
    /// An identifier is unassigned, <paramref name="plan"/> is blank or too long, or
    /// <paramref name="validTo"/> is not after <paramref name="validFrom"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="seats"/> is not positive.</exception>
    public static Subscription Start(
        SubscriptionId id,
        TenantId tenantId,
        string plan,
        int seats,
        DateOnly validFrom,
        DateOnly? validTo)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("The subscription id is unassigned.", nameof(id));
        }

        if (tenantId.IsEmpty)
        {
            throw new ArgumentException("The tenant id is unassigned.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(plan);
        ArgumentOutOfRangeException.ThrowIfLessThan(seats, 1);

        if (plan.Length > MaxPlanLength || plan != plan.Trim())
        {
            throw new ArgumentException(
                $"Expected 1 to {MaxPlanLength} characters with no leading or trailing whitespace.",
                nameof(plan));
        }

        if (validTo is not null && validTo.Value <= validFrom)
        {
            throw new ArgumentException(
                $"A subscription ends after it starts: valid_to {validTo:yyyy-MM-dd} is not after " +
                $"valid_from {validFrom:yyyy-MM-dd}. Validity is half-open, so a one-day subscription " +
                "ends the day after it starts.",
                nameof(validTo));
        }

        return new Subscription
        {
            Id = id,
            TenantId = tenantId,
            Plan = plan,
            Seats = seats,
            ValidFrom = validFrom,
            ValidTo = validTo,
        };
    }
}
