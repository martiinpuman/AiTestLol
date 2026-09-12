using System;
using System.ComponentModel;
using Aurora.Platform.Tenancy.Contracts;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// What the catalog says about where one tenant lives: the row of <c>catalog.tenant</c> joined to
/// its <c>catalog.database_cluster</c>, as read by <see cref="ITenantRoutingReader"/> and held for
/// 60 s by <see cref="TenantRoutingCache"/> (ADR-0007 §3.5, ADR-0012 §3).
/// </summary>
/// <param name="TenantId">The tenant's identity, as <c>catalog.tenant.id</c>.</param>
/// <param name="TenantKey">The tenant's key, which names its pool in <c>pg_stat_activity</c>.</param>
/// <param name="State">Where the tenant is in its life; decides whether it may be routed at all.</param>
/// <param name="Cluster">
/// The cluster the tenant is placed on, or <see langword="null"/> for a tombstone, whose routing
/// columns ADR-0007 §11.4 blanks.
/// </param>
/// <param name="DatabaseName">The tenant's database on that cluster; <see langword="null"/> for a tombstone.</param>
/// <param name="ResidencyRegion">The region the tenant's data may live in; <see langword="null"/> for a tombstone.</param>
/// <remarks>
/// <para>
/// <b>Holds a secret reference, never a secret.</b> The credential is read from the host's secret
/// store on every resolve and composed into the connection string in memory, so nothing that is
/// cached — and later, with an L2 backend (ADR-0012), nothing that leaves the process — carries a
/// password.
/// </para>
/// <para>
/// Primitives only, so that the record round-trips through <c>System.Text.Json</c> unchanged the
/// day an L2 arrives; and immutable, so that the L1 cache keeps the instance rather than a
/// serialised copy of it. <c>TenantId</c>, <c>ClusterId</c> and <c>Region</c> are re-typed at the
/// edge by the resolver.
/// </para>
/// </remarks>
[ImmutableObject(true)]
internal sealed record TenantRouting(
    Guid TenantId,
    string TenantKey,
    TenantState State,
    ClusterEndpoint? Cluster,
    string? DatabaseName,
    string? ResidencyRegion);

/// <summary>
/// One <c>catalog.database_cluster</c> row as the resolver and the DDL path need it: the endpoint
/// and the three secret <em>references</em> (ADR-0007 §9.2, ADR-0027 §2).
/// </summary>
/// <remarks>
/// The admin and migrator references are read alongside the app reference so that
/// <c>ITenantAdminConnectionFactory</c> (B-07.1) reuses this read instead of writing a second
/// query over the same two tables — "so there is still exactly one place that knows how a tenant
/// maps to physical storage" (ADR-0027 §2).
/// </remarks>
[ImmutableObject(true)]
internal sealed record ClusterEndpoint(
    string ClusterId,
    string Host,
    int Port,
    string MaintenanceDatabase,
    string AdminSecretRef,
    string MigratorSecretRef,
    string AppSecretRef);
