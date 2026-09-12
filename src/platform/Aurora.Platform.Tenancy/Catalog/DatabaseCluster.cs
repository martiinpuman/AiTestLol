using System;
using Aurora.Platform.Tenancy.Contracts;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// One PostgreSQL cluster tenants are placed on: where it is, how to reach its maintenance
/// database, which secrets unlock its three roles, and how many tenants it may hold
/// (ADR-0007 §3.5, §6, §9.2; ADR-0004 rule 2).
/// </summary>
/// <remarks>
/// <para>
/// The row holds an endpoint and three <see cref="SecretReference"/>s — one per cluster role —
/// and no credential. Role <em>names</em> are not stored either: <c>aurora_admin</c>,
/// <c>aurora_migrator</c> and <c>aurora_app</c> are the same on every cluster (ADR-0004 rule 2), so
/// the resolver composes <c>Host=…;Port=…;Database=…;Username=aurora_app;Password=&lt;from the
/// store&gt;</c> in memory at request time. Stage 2 of ADR-0007 §3.5 (a role per tenant database)
/// changes the resolver, not this row.
/// </para>
/// <para>
/// <c>migrator_secret_ref</c> is not in ADR-0007 §9.2's column list. §7.3 has the migration runner
/// connect as <c>aurora_migrator</c>, and a secret the runner needs per cluster has to be
/// referenced from the cluster's row like the other two; the omission is reported as an ADR gap
/// in the B-05 summary rather than reproduced.
/// </para>
/// <para>
/// <b>The host is what <see cref="CanonicalHost"/> says a host is</b> (ADR-0036 §3): a canonical
/// host name or an IPv4 literal, and nothing else. <see cref="TenantHost"/> keeps a private
/// declaration of essentially the same grammar for <c>tenant_host.host</c>; there are two
/// declarations and nothing compares them — the pattern ADR-0043 chose for the analogous pair is
/// two declarations plus a fitness rule that holds them equal, and that rule is a separate row.
/// <c>ck_database_cluster_host_well_formed</c> evaluates the same grammar in the database beside
/// <c>ck_database_cluster_host_lower_case</c>, so every writer meets it, raw SQL included — the
/// rule here binds only callers of <see cref="Register"/>, of which there is none in production
/// yet. A host name is case-insensitive, so two spellings of one host would be two rows on one
/// endpoint that <c>ux_database_cluster_host_port</c> could not tell apart; and a value that is
/// not one host — a multi-host list, a Unix-socket directory, the two shapes ADR-0036 §6 names —
/// is not an endpoint the triple can project. Why the grammar is ASCII, executed on
/// <c>postgres:17-alpine</c> in <c>CatalogHostGrammarTests</c>: the lower-case check's
/// <c>lower()</c> depends on the database's <c>lc_ctype</c> for a non-ASCII letter — under <c>C</c>
/// <c>lower('pg.Über.internal')</c> is unchanged and <c>host = lower(host)</c> admits the row,
/// under <c>en_US.utf8</c> it folds to <c>pg.über.internal</c> and refuses it — so a catalog could
/// hold two spellings of one such name on one collation and not another. (<c>pg.ÜBER.internal</c>
/// is not that witness: <c>B</c>, <c>E</c> and <c>R</c> are ASCII upper case and fold under both.)
/// An internationalised name is stored in its punycode form, which is what DNS carries. An IP
/// literal beside a host name is the half no spelling rule closes.
/// </para>
/// </remarks>
internal sealed class DatabaseCluster
{
    public const int MaxHostLength = CanonicalHost.MaxLength;
    public const int MinPort = 1;
    public const int MaxPort = 65535;

    private DatabaseCluster()
    {
    }

    public ClusterId Id { get; private set; }

    public Region Region { get; private set; }

    /// <summary>The host name or address the cluster's PostgreSQL listens on.</summary>
    public string Host { get; private set; } = null!;

    public int Port { get; private set; }

    /// <summary>
    /// The database <c>aurora_admin</c> connects to in order to run <c>CREATE DATABASE</c>, which
    /// must be issued from a connection to some <em>other</em> database (ADR-0007 §1, §8 step 2).
    /// </summary>
    public string MaintenanceDatabase { get; private set; } = null!;

    public SecretReference AdminSecretRef { get; private set; }

    public SecretReference MigratorSecretRef { get; private set; }

    public SecretReference AppSecretRef { get; private set; }

    /// <summary>The placement cap. ADR-0007 §6 designs for 1 000 per cluster.</summary>
    public int MaxTenants { get; private set; }

    public DatabaseClusterState State { get; private set; }

    /// <summary>Registers a cluster as accepting tenants.</summary>
    /// <exception cref="ArgumentException">
    /// An identifier is unassigned, <paramref name="host"/> is not what <see cref="CanonicalHost"/>
    /// accepts, <paramref name="maintenanceDatabase"/> is malformed, or a secret reference is
    /// unassigned.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="port"/> is not a TCP port or <paramref name="maxTenants"/> is not positive.
    /// </exception>
    public static DatabaseCluster Register(
        ClusterId id,
        Region region,
        string host,
        int port,
        string maintenanceDatabase,
        SecretReference adminSecretRef,
        SecretReference migratorSecretRef,
        SecretReference appSecretRef,
        int maxTenants)
    {
        RequireSpecified(id.IsSpecified, nameof(id), "cluster id");
        RequireSpecified(region.IsSpecified, nameof(region), "region");
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(maintenanceDatabase);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, MaxPort);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTenants, 1);
        RequireSpecified(adminSecretRef.IsSpecified, nameof(adminSecretRef), "admin secret reference");
        RequireSpecified(migratorSecretRef.IsSpecified, nameof(migratorSecretRef), "migrator secret reference");
        RequireSpecified(appSecretRef.IsSpecified, nameof(appSecretRef), "app secret reference");

        if (!CanonicalHost.IsWellFormed(host))
        {
            throw new ArgumentException(
                $"'{host}' is not a host the registry accepts: a host name in canonical form - labels of lower-case " +
                $"ASCII letters, digits and inner hyphens, 1 to {CanonicalHost.MaxLabelLength} characters each, joined by " +
                $"single dots, {CanonicalHost.MaxLength} characters at most - or an IPv4 literal; one host, not a list, " +
                "not a socket directory, not a scheme, port or path.",
                nameof(host));
        }

        if (!PostgresIdentifier.IsWellFormed(maintenanceDatabase))
        {
            throw new ArgumentException(
                $"'{maintenanceDatabase}' is not a database name PostgreSQL can take unquoted: lower-case " +
                $"ASCII letters, digits and underscores, at most {PostgresIdentifier.MaxLength} characters.",
                nameof(maintenanceDatabase));
        }

        return new DatabaseCluster
        {
            Id = id,
            Region = region,
            Host = host,
            Port = port,
            MaintenanceDatabase = maintenanceDatabase,
            AdminSecretRef = adminSecretRef,
            MigratorSecretRef = migratorSecretRef,
            AppSecretRef = appSecretRef,
            MaxTenants = maxTenants,
            State = DatabaseClusterState.Accepting,
        };
    }

    /// <summary>Stops placing new tenants here. Existing tenants are unaffected.</summary>
    /// <exception cref="InvalidOperationException">The cluster is retired.</exception>
    public void StopAccepting()
    {
        if (State == DatabaseClusterState.Retired)
        {
            throw new InvalidOperationException($"Cluster '{Id}' is retired and cannot change state.");
        }

        State = DatabaseClusterState.Closed;
    }

    private static void RequireSpecified(bool isSpecified, string parameterName, string what)
    {
        if (!isSpecified)
        {
            throw new ArgumentException($"The {what} is unassigned.", parameterName);
        }
    }
}
