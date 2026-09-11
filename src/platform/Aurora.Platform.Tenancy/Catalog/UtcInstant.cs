using System;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// Guards the rule that an event instant in the catalog is UTC (ADR-0004 rule 4).
/// </summary>
/// <remarks>
/// Npgsql refuses a <see cref="DateTimeOffset"/> with a non-zero offset when writing
/// <c>timestamptz</c>, but it refuses it at <c>SaveChanges</c>, far from the code that produced
/// it. Checking at the entity keeps the failure next to its cause. <c>TimeProvider.GetUtcNow()</c>
/// always satisfies this; <c>DateTimeOffset.Now</c> on a machine outside UTC never does, which is
/// the mistake being caught.
/// </remarks>
internal static class UtcInstant
{
    public static DateTimeOffset Require(DateTimeOffset instant, string parameterName)
    {
        if (instant.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                $"An instant stored in the catalog must be UTC (offset zero); this one has offset " +
                $"{instant.Offset}. Take instants from TimeProvider.GetUtcNow() (ADR-0004 rule 4).",
                parameterName);
        }

        return instant;
    }

    public static DateTimeOffset? RequireOrNull(DateTimeOffset? instant, string parameterName) =>
        instant is null ? null : Require(instant.Value, parameterName);
}
