using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurora.Platform.Tenancy.Migrations
{
    /// <summary>
    /// ADR-0034 §3.1: the routing-uniqueness axis is the physical endpoint, so
    /// <c>catalog.database_cluster (host, port)</c> is unique — and, beside it, the host is held to
    /// one canonical spelling (ADR-0036 §3, ADR-0034 §3.4) and to being one host at all
    /// (<see cref="Catalog.CanonicalHost"/>; PR #18, second review). Three statements, expand-only,
    /// against a table B-05 created; no column changes, no data changes:
    /// <c>CREATE UNIQUE INDEX ux_database_cluster_host_port ON catalog.database_cluster (host, port)</c>,
    /// <c>ADD CONSTRAINT ck_database_cluster_host_lower_case CHECK (host = lower(host))</c> and
    /// <c>ADD CONSTRAINT ck_database_cluster_host_well_formed CHECK (host ~ …)</c>, the grammar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What it closes.</b> The third executed variant of the tenant-takeover finding: two
    /// cluster rows on one server, one tenant each, the attacker's <c>database_name</c> copied from
    /// the victim's, both resolving to one endpoint. With the index
    /// <c>cluster_id → (host, port)</c> is injective; <c>fk_tenant_cluster_in_region</c> makes it
    /// total for every non-deleted tenant; <c>ux_tenant_cluster_id_database_name</c> makes
    /// <c>(cluster_id, database_name)</c> unique; so <c>(host, port, database_name)</c> — what the
    /// resolver composes — is unique across <c>catalog.tenant</c>. The index is byte-exact, so it
    /// is injective on the endpoint only if one endpoint has one spelling: the lower-case check
    /// closes the case half (<c>PG.INTERNAL</c> beside <c>pg.internal</c>), and the grammar closes
    /// what a lower-case rule cannot see — a multi-host list (<c>pg-1.internal,pg-2.internal</c>
    /// walked past the index, the lower-case check and the endpoint comparison as a fifth variant),
    /// a Unix-socket directory (for which <c>lower()</c> would be wrong: ADR-0036 §6 names both), a
    /// non-ASCII letter (whose <c>lower()</c> depends on the catalog's collation), a stray dot. The
    /// grammar is evaluated here so that raw SQL meets it, not only the entity (ADR-0036 §4.2), and
    /// it is written with explicit ranges — <c>[a-z0-9-]</c>, never <c>[[:alpha:]]</c> — because a
    /// POSIX class follows the database's <c>lc_ctype</c> while a range is matched by code point,
    /// and this check must mean the same thing on a <c>C</c>-collated catalog as on the
    /// <c>en_US.utf8</c> one the tests run on; <c>CatalogHostGrammarTests</c> executes its cases
    /// under both. The
    /// property, not the constraints, is what the tests assert (<c>CatalogRoutingUniquenessTests</c>,
    /// ADR-0036 §2.3).
    /// </para>
    /// <para>
    /// <b>What it cannot close.</b> Two different names for one server — an IP literal beside a
    /// host name, a CNAME, a second DNS record, a failover alias. The catalog stores what it was
    /// told; a constraint over a name narrows what can be stored and never establishes identity.
    /// That is <c>TenantIdentityStamp</c>'s job on every physical connection (ADR-0034 §4). And two
    /// shapes the index is wrong for if they ever become rows: a read replica, a pooler endpoint —
    /// a PgBouncer endpoint is a second column pair on one row with its own unique index, never a
    /// second row (ADR-0034 §3.2, §9).
    /// </para>
    /// <para>
    /// <b>Transactional on purpose, not <c>CONCURRENTLY</c></b> (ADR-0037 §5 owns the question).
    /// This is the catalog's operator seed data, a handful of rows, in one database. Inside EF's
    /// migration transaction a catalog that already holds two rows on one endpoint fails with
    /// SQLSTATE <c>23505</c> naming the index and the duplicated key; one that holds a host in
    /// another case, or a value that is not one host, fails with <c>23514</c> naming the
    /// constraint; either way nothing of the migration survives — not the index, not either check,
    /// not the history row, and no row is touched (ADR-0034 §5.1; proven by
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

            migrationBuilder.AddCheckConstraint(
                name: "ck_database_cluster_host_well_formed",
                schema: "catalog",
                table: "database_cluster",
                sql: "host ~ '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$'");
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

            migrationBuilder.DropCheckConstraint(
                name: "ck_database_cluster_host_well_formed",
                schema: "catalog",
                table: "database_cluster");
        }
    }
}
