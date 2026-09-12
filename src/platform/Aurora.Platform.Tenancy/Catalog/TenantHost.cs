using System;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// A host name that resolves to a tenant — <c>acme.aurora.example</c>, or a custom domain — the
/// first tenant-resolution strategy of ADR-0007 §3.2.
/// </summary>
/// <remarks>
/// A tenant has one primary host and any number of others; the catalog keeps "one primary per
/// tenant" true with a partial unique index. A custom domain is unverified until its owner proves
/// control of it (<see cref="VerifiedAt"/>); the default host under the platform's domain is
/// verified at creation. Hosts are stored in canonical lower case and the row refuses any other
/// spelling, because two spellings of one host would be two rows.
/// </remarks>
internal sealed class TenantHost
{
    public const int MaxHostLength = 253;
    private const int MaxLabelLength = 63;

    private TenantHost()
    {
    }

    public string Host { get; private set; } = null!;

    public TenantId TenantId { get; private set; }

    public bool IsPrimary { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <exception cref="ArgumentException">
    /// <paramref name="host"/> is not a lower-case DNS name, <paramref name="tenantId"/> is
    /// unassigned, or <paramref name="verifiedAt"/> is not UTC.
    /// </exception>
    public static TenantHost Register(string host, TenantId tenantId, bool isPrimary, DateTimeOffset? verifiedAt)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (tenantId.IsEmpty)
        {
            throw new ArgumentException("The tenant id is unassigned.", nameof(tenantId));
        }

        if (!IsWellFormedHost(host))
        {
            throw new ArgumentException(
                $"'{host}' is not a host name the registry accepts: lower-case DNS labels of letters, " +
                $"digits and inner hyphens, joined by single dots, at most {MaxHostLength} characters, " +
                "with no port, scheme or path.",
                nameof(host));
        }

        return new TenantHost
        {
            Host = host,
            TenantId = tenantId,
            IsPrimary = isPrimary,
            VerifiedAt = UtcInstant.RequireOrNull(verifiedAt, nameof(verifiedAt)),
        };
    }

    private static bool IsWellFormedHost(string host)
    {
        if (host.Length is 0 or > MaxHostLength)
        {
            return false;
        }

        foreach (string label in host.Split('.'))
        {
            if (!IsWellFormedLabel(label))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWellFormedLabel(string label)
    {
        if (label.Length is 0 or > MaxLabelLength || label[0] == '-' || label[^1] == '-')
        {
            return false;
        }

        foreach (char character in label)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }
}
