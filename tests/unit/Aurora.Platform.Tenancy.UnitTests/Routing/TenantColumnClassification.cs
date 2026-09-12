using System;
using System.Collections.Generic;
using Aurora.Platform.Tenancy.Tests;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>
/// Every column of <c>catalog.tenant</c>, classified: a routing column, the state gate, or outside
/// routing. The three partition <c>CatalogSchemaAllowlist.Columns["tenant"]</c>, and
/// <c>TenantRoutingCacheInvalidatorTests</c> holds them to it both ways with a count.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists (PR #14 M-3): <c>CatalogSchemaAllowlist.RoutingColumns["tenant"]</c> is
/// load-bearing for two security controls — the invalidator's watched list, and the privilege
/// rule that no <c>UPDATE</c> grant to <c>aurora_app</c> names a routing column — and carried no
/// completeness obligation of its own. A routing column dropped from it went unnoticed by every
/// test, and the same edit widened the privilege guard. With the non-routing columns named here,
/// a column of the tenant row that is in no list fails until someone classifies it, and moving
/// one between lists is a visible edit in a record a reviewer reads.
/// </para>
/// <para>
/// This record lives beside the invalidator's tests rather than in <c>CatalogSchemaGuard.cs</c>
/// only because that file is being changed by two other in-flight branches around the very lines
/// this would sit on; it reads <c>RoutingColumns</c> from there rather than repeating it.
/// </para>
/// </remarks>
internal static class TenantColumnClassification
{
    /// <summary>
    /// The one column that gates routing without being a routing column: the request path may
    /// move it (<c>UPDATE(state)</c> is granted; B-06.2 marks <c>SchemaBlocked</c> through it) and
    /// a resolved connection depends on it (only <c>Active</c> and <c>Suspended</c> route).
    /// </summary>
    public const string Gate = "state";

    /// <summary>The columns a tenant is resolved by: the privilege record's list, read rather than repeated.</summary>
    public static IReadOnlySet<string> Routing => CatalogSchemaAllowlist.RoutingColumns["tenant"];

    /// <summary>
    /// The columns of <c>catalog.tenant</c> a resolved connection does not depend on: the primary
    /// key a tenant is looked up by (compared by the routing cache, never composed), the display
    /// name, the plan, the schema version the skew check reads (B-08.3), and the lifecycle dates.
    /// </summary>
    public static readonly IReadOnlySet<string> OutsideRouting = new HashSet<string>(StringComparer.Ordinal)
    {
        "id",
        "display_name",
        "core_schema_version",
        "plan",
        "created_at",
        "activated_at",
        "suspended_at",
        "deletion_due_at",
        "deleted_at",
        "last_activity_at",
    };

    /// <summary>What the invalidator must watch: every routing column and the gate.</summary>
    public static IReadOnlySet<string> Watched => new HashSet<string>(Routing, StringComparer.Ordinal) { Gate };
}
