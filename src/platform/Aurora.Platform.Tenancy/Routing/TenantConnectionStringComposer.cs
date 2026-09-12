using System;
using Aurora.Platform.Tenancy.Catalog;
using Npgsql;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// The one place a tenant connection string is spelled out (ADR-0007 §3.5, <c>modules.md</c> §4):
/// the cluster's endpoint, the tenant's database, the <c>aurora_app</c> role with its credential
/// from the secret store, and every pool setting of §5.2.
/// </summary>
/// <remarks>
/// Composed with <see cref="NpgsqlConnectionStringBuilder"/> rather than by string formatting, so
/// that a host name or a password containing a character that means something to a connection
/// string is quoted by the library that will parse it, and so that each §5.2 setting is set
/// through the property Npgsql reads it back from.
/// </remarks>
internal static class TenantConnectionStringComposer
{
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="databaseName"/> or <paramref name="tenantKey"/> is blank.</exception>
    public static string Compose(
        ClusterEndpoint cluster,
        string databaseName,
        string tenantKey,
        string appPassword,
        TenantPoolSettings settings)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantKey);
        ArgumentNullException.ThrowIfNull(appPassword);
        ArgumentNullException.ThrowIfNull(settings);

        return new NpgsqlConnectionStringBuilder
        {
            Host = cluster.Host,
            Port = cluster.Port,
            Database = databaseName,
            Username = ClusterRoles.App,
            Password = appPassword,
            Pooling = true,
            MinPoolSize = TenantPoolSettings.MinimumPoolSize,
            MaxPoolSize = settings.MaximumPoolSize,
            ConnectionIdleLifetime = TenantPoolSettings.ConnectionIdleLifetimeSeconds,
            ConnectionPruningInterval = TenantPoolSettings.ConnectionPruningIntervalSeconds,
            MaxAutoPrepare = TenantPoolSettings.MaxAutoPrepare,
            Timeout = TenantPoolSettings.ConnectionTimeoutSeconds,
            CommandTimeout = settings.CommandTimeoutSeconds,
            ApplicationName = settings.ApplicationNameFor(tenantKey),
        }.ConnectionString;
    }
}
