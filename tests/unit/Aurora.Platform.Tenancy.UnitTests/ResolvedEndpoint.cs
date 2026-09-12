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
/// <para>
/// <b>What the comparison backstops, precisely, and what it leaves to the catalog.</b> Two hosts
/// are refused by <see cref="Parse"/> rather than projected, because neither is one endpoint and a
/// triple would silently name something else: a multi-host list (Npgsql opens whichever host
/// answers) and a Unix-socket directory (a path, whose case is significant and which names no
/// server) — the two shapes ADR-0036 §6 names. Equality folds case and one trailing dot, the two
/// spellings of one name a resolver treats as one. Everything else non-canonical — a leading or
/// doubled dot, a hyphen at a label's edge, a non-ASCII letter — is parsed as given and compared
/// as given, and <c>ck_database_cluster_host_well_formed</c> is what keeps such a value out of the
/// catalog. Executed with that check removed from the migration (PR #18, third review): the list
/// and the socket directory make the fleet scan error, the trailing dot makes it report the
/// collision, and nothing else here would notice a fourth spelling.
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

    /// <summary>The host as the string spelled it; equality folds case and a trailing dot, this does not.</summary>
    public string Host { get; }

    public int Port { get; }

    public string Database { get; }

    /// <summary>The host as compared: one trailing dot removed, case folded by the comparer.</summary>
    private string ComparableHost => Host.EndsWith('.') ? Host[..^1] : Host;

    /// <summary>The triple, read back by the same parser that composed the string.</summary>
    /// <exception cref="ArgumentException">
    /// The string names no host or no database, so it addresses nothing; or its host is a
    /// multi-host list or a Unix-socket directory, which is not one endpoint (ADR-0036 §6).
    /// </exception>
    public static ResolvedEndpoint Parse(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);

        if (string.IsNullOrEmpty(parsed.Host) || string.IsNullOrEmpty(parsed.Database))
        {
            throw new ArgumentException("A resolved connection string names a host and a database; this one does not.", nameof(connectionString));
        }

        if (parsed.Host.Contains(',', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{parsed.Host}' is a multi-host list, which is not one endpoint and cannot be compared as one (ADR-0036 §6).",
                nameof(connectionString));
        }

        if (parsed.Host.StartsWith('/'))
        {
            throw new ArgumentException(
                $"'{parsed.Host}' is a Unix-socket directory, which is not one endpoint and cannot be compared as one (ADR-0036 §6).",
                nameof(connectionString));
        }

        return new ResolvedEndpoint(parsed.Host, parsed.Port, parsed.Database);
    }

    public bool Equals(ResolvedEndpoint? other) =>
        other is not null
        && Port == other.Port
        && string.Equals(Database, other.Database, StringComparison.Ordinal)
        && string.Equals(ComparableHost, other.ComparableHost, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ResolvedEndpoint);

    public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(ComparableHost), Port, StringComparer.Ordinal.GetHashCode(Database));

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
