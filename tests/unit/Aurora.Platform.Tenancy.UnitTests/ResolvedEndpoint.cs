using System;
using System.Collections.Generic;
using Npgsql;

namespace Aurora.Platform.Tenancy.Tests;

/// <summary>
/// The physical endpoint a resolved connection string addresses, and what makes two of them one
/// (ADR-0036 §2.3): the <c>(host, port, database)</c> triple parsed out of the resolver's own
/// output with <see cref="NpgsqlConnectionStringBuilder"/> — <c>host</c> compared ignoring case,
/// because DNS names are case-insensitive and an ordinal comparison under-reports collisions;
/// <c>database</c> compared ordinally, because a PostgreSQL database name is case-sensitive;
/// <c>port</c> exactly.
/// </summary>
/// <remarks>
/// <para>
/// Compiled into both test assemblies, as <c>CatalogSchemaGuard</c> is. The unit tests prove the
/// comparison over synthetic strings (ADR-0036 §2.4 D1) — a proof the catalog's constraints can
/// never disarm, because it never touches the catalog — and the integration tests run the same
/// comparison over the real resolver's output for every non-deleted tenant (D2).
/// </para>
/// <para>
/// A connection string is a serialisation of an intent, not a description of a destination
/// (§2.1): <c>Application Name</c>, <c>Username</c>, <c>Password</c> and every ADR-0007 §5.2 pool
/// setting can differ between two strings that address the same bytes on the same disk, so
/// equality over the whole string cannot fail — <c>Application Name</c> carries the tenant key by
/// construction, and <c>ux_tenant_key</c> makes that unique. This type is the projection that
/// contains the destination and nothing else.
/// </para>
/// </remarks>
public sealed class ResolvedEndpoint : IEquatable<ResolvedEndpoint>
{
    private ResolvedEndpoint(string host, int port, string database)
    {
        Host = host;
        Port = port;
        Database = database;
    }

    /// <summary>The host as the string spelled it; equality folds case, this does not.</summary>
    public string Host { get; }

    public int Port { get; }

    public string Database { get; }

    /// <summary>The triple, read back by the same parser that composed the string.</summary>
    /// <exception cref="ArgumentException">The string names no host or no database, so it addresses nothing.</exception>
    public static ResolvedEndpoint Parse(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);

        if (string.IsNullOrEmpty(parsed.Host) || string.IsNullOrEmpty(parsed.Database))
        {
            throw new ArgumentException("A resolved connection string names a host and a database; this one does not.", nameof(connectionString));
        }

        // A multi-host list is not one endpoint and cannot be projected to a triple (ADR-0036 §6):
        // Npgsql opens whichever of the hosts answers, so a row carrying one is a second name for
        // a server the comparison would otherwise take for a different one. Refused here, so the
        // fleet scan errors on such a row rather than passing over it (PR #18, second review).
        if (parsed.Host.Contains(',', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{parsed.Host}' is a multi-host list, which is not one endpoint and cannot be compared as one (ADR-0036 §6).",
                nameof(connectionString));
        }

        return new ResolvedEndpoint(parsed.Host, parsed.Port, parsed.Database);
    }

    public bool Equals(ResolvedEndpoint? other) =>
        other is not null
        && Port == other.Port
        && string.Equals(Database, other.Database, StringComparison.Ordinal)
        && string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ResolvedEndpoint);

    public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Host), Port, StringComparer.Ordinal.GetHashCode(Database));

    public override string ToString() => $"{Host}:{Port}/{Database}";
}

/// <summary>Every unordered pair over a list, compared by endpoint, with the number of comparisons made.</summary>
/// <param name="PairsCompared">The count the assertion reports: zero is a failure, not a pass (ADR-0036 §2.3).</param>
/// <param name="Collisions">The pairs whose endpoints are one.</param>
public sealed record EndpointComparison<T>(int PairsCompared, IReadOnlyList<(T First, T Second)> Collisions);

/// <summary>The comparison itself, over anything that carries a <see cref="ResolvedEndpoint"/>.</summary>
public static class EndpointCollisions
{
    public static EndpointComparison<T> Find<T>(IReadOnlyList<T> items, Func<T, ResolvedEndpoint> endpointOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(endpointOf);

        int pairs = 0;
        List<(T, T)> collisions = [];
        for (int first = 0; first < items.Count; first++)
        {
            for (int second = first + 1; second < items.Count; second++)
            {
                pairs++;
                if (endpointOf(items[first]).Equals(endpointOf(items[second])))
                {
                    collisions.Add((items[first], items[second]));
                }
            }
        }

        return new EndpointComparison<T>(pairs, collisions);
    }
}
