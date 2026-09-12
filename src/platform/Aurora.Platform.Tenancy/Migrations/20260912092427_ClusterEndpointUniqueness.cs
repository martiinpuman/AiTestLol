using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurora.Platform.Tenancy.Migrations
{
    /// <summary>
    /// ADR-0034 §3.1: the routing-uniqueness axis is the physical endpoint, so
    /// <c>catalog.database_cluster (host, port)</c> is unique — and, beside it, the host is held to
    /// its canonical lower-case spelling (PR #18 M-2). Two statements, expand-only, against a table
    /// B-05 created; no column changes, no data changes:
    /// <c>CREATE UNIQUE INDEX ux_database_cluster_host_port ON catalog.database_cluster (host, port)</c>
    /// and <c>ALTER TABLE catalog.database_cluster ADD CONSTRAINT ck_database_cluster_host_lower_case CHECK (host = lower(host))</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What it closes.</b> The third executed variant of the tenant-takeover finding: two
    /// cluster rows on one server, one tenant each, the attacker's <c>database_name</c> copied from
    /// the victim's, both resolving to one connection string. With the index
    /// <c>cluster_id → (host, port)</c> is injective; <c>fk_tenant_cluster_in_region</c> makes it
    /// total for every non-deleted tenant; <c>ux_tenant_cluster_id_database_name</c> makes
    /// <c>(cluster_id, database_name)</c> unique; so <c>(host, port, database_name)</c> — what the
    /// resolver composes — is unique across <c>catalog.tenant</c>. The check closes the spelling
    /// half of the aliasing the index cannot see: a host name is case-insensitive, so
    /// <c>PG.INTERNAL</c> beside <c>pg.internal</c> would be two rows on one endpoint that the
    /// index takes for two (PR #18 M-1). The property, not the index, is what the tests assert
    /// (<c>CatalogRoutingUniquenessTests</c>, ADR-0034 §3.3).
    /// </para>
    /// <para>
    /// <b>What it cannot close.</b> Two different names for one server — an IP literal beside a
    /// host name, a CNAME, a second DNS record, a failover alias. The catalog stores what it was
    /// told; a constraint over a name narrows what can be stored and never establishes identity.
    /// That is <c>TenantIdentityStamp</c>'s job on every physical connection (ADR-0034 §4). And two
    /// shapes this index is wrong for if they ever become rows: a read replica, a pooler endpoint —
    /// a PgBouncer endpoint is a second column pair on one row with its own unique index, never a
    /// second row (§3.2, §9).
    /// </para>
    /// <para>
    /// <b>Transactional on purpose, not <c>CONCURRENTLY</c>.</b> ADR-0007 §7.2 reserves
    /// <c>CREATE INDEX CONCURRENTLY</c> for a table a live tenant writes to; this is the catalog's
    /// operator seed data, a handful of rows, in one database. Inside EF's migration transaction a
    /// catalog that already holds two rows on one endpoint fails with SQLSTATE <c>23505</c> naming
    /// the index and the duplicated key, and one that holds a host in another case fails with
    /// <c>23514</c> naming the constraint; either way nothing of the migration survives — not the
    /// index, not the check, not the history row, and no row is touched (ADR-0034 §5.1; proven by
    /// <c>ClusterEndpointUniquenessMigrationTests</c>). <c>CONCURRENTLY</c> would leave an
    /// <c>INVALID</c> index behind on that failure, and cannot run in a transaction at all. No
    /// production catalog existed when this landed, so there is no de-duplication or re-spelling
    /// procedure; a catalog that fails here is resolved by an operator, not by this migration.
    /// </para>
    /// </remarks>
    public partial class ClusterEndpointUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_database_cluster_host_port",
                schema: "catalog",
                table: "database_cluster",
                columns: new[] { "host", "port" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_database_cluster_host_lower_case",
                schema: "catalog",
                table: "database_cluster",
                sql: "host = lower(host)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_database_cluster_host_port",
                schema: "catalog",
                table: "database_cluster");

            migrationBuilder.DropCheckConstraint(
                name: "ck_database_cluster_host_lower_case",
                schema: "catalog",
                table: "database_cluster");
        }
    }
}
