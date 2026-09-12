using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurora.Platform.Tenancy.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "database_cluster",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    region = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    maintenance_database = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    admin_secret_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    migrator_secret_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    app_secret_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    max_tenants = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_database_cluster", x => x.id);
                    table.UniqueConstraint("ak_database_cluster_id_region", x => new { x.id, x.region });
                    table.CheckConstraint("ck_database_cluster_id_well_formed", "id ~ '^[a-z][a-z0-9]*(-[a-z0-9]+)*$'");
                    table.CheckConstraint("ck_database_cluster_max_tenants_positive", "max_tenants > 0");
                    table.CheckConstraint("ck_database_cluster_port_is_tcp_port", "port BETWEEN 1 AND 65535");
                    table.CheckConstraint("ck_database_cluster_secret_refs_are_references", "admin_secret_ref !~ '[=;[:space:]]' AND migrator_secret_ref !~ '[=;[:space:]]' AND app_secret_ref !~ '[=;[:space:]]'");
                    table.CheckConstraint("ck_database_cluster_state", "state IN ('Accepting', 'Closed', 'Retired')");
                });

            migrationBuilder.CreateTable(
                name: "tenant",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cluster_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    database_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    residency_region = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    core_schema_version = table.Column<int>(type: "integer", nullable: true),
                    plan = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deletion_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant", x => x.id);
                    table.CheckConstraint("ck_tenant_deleted_at_matches_state", "(state = 'Deleted') = (deleted_at IS NOT NULL)");
                    table.CheckConstraint("ck_tenant_key_well_formed", "key ~ '^[a-z][a-z0-9]*(-[a-z0-9]+)*$' AND length(key) >= 3");
                    table.CheckConstraint("ck_tenant_routing_present_unless_deleted", "state = 'Deleted' OR (display_name IS NOT NULL AND cluster_id IS NOT NULL AND database_name IS NOT NULL AND residency_region IS NOT NULL AND plan IS NOT NULL)");
                    table.CheckConstraint("ck_tenant_state", "state IN ('Provisioning', 'ProvisioningFailed', 'Active', 'Suspended', 'SchemaBlocked', 'Exporting', 'PendingDeletion', 'Deleted')");
                    table.ForeignKey(
                        name: "fk_tenant_cluster_in_region",
                        columns: x => new { x.cluster_id, x.residency_region },
                        principalSchema: "catalog",
                        principalTable: "database_cluster",
                        principalColumns: new[] { "id", "region" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "installed_package",
                schema: "catalog",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    installed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    installed_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installed_package", x => new { x.tenant_id, x.package_id });
                    table.CheckConstraint("ck_installed_package_id_well_formed", "package_id ~ '^[a-z][a-z0-9_]*$'");
                    table.CheckConstraint("ck_installed_package_state", "state IN ('Installing', 'Active', 'Deactivated', 'UpgradePending', 'Failed')");
                    table.ForeignKey(
                        name: "fk_installed_package_tenant",
                        column: x => x.tenant_id,
                        principalSchema: "catalog",
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    seats = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription", x => x.id);
                    table.CheckConstraint("ck_subscription_ends_after_it_starts", "valid_to IS NULL OR valid_to > valid_from");
                    table.CheckConstraint("ck_subscription_seats_positive", "seats > 0");
                    table.ForeignKey(
                        name: "fk_subscription_tenant",
                        column: x => x.tenant_id,
                        principalSchema: "catalog",
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_host",
                schema: "catalog",
                columns: table => new
                {
                    host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_host", x => x.host);
                    table.CheckConstraint("ck_tenant_host_lower_case", "host = lower(host)");
                    table.ForeignKey(
                        name: "fk_tenant_host_tenant",
                        column: x => x.tenant_id,
                        principalSchema: "catalog",
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_database_cluster_region_state",
                schema: "catalog",
                table: "database_cluster",
                columns: new[] { "region", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_tenant_id",
                schema: "catalog",
                table: "subscription",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_cluster_id_residency_region",
                schema: "catalog",
                table: "tenant",
                columns: new[] { "cluster_id", "residency_region" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_state_last_activity_at",
                schema: "catalog",
                table: "tenant",
                columns: new[] { "state", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_cluster_id_database_name",
                schema: "catalog",
                table: "tenant",
                columns: new[] { "cluster_id", "database_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tenant_key",
                schema: "catalog",
                table: "tenant",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_host_tenant_id",
                schema: "catalog",
                table: "tenant_host",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_tenant_host_primary",
                schema: "catalog",
                table: "tenant_host",
                column: "tenant_id",
                unique: true,
                filter: "is_primary");

            // Hand-written from here down: what EF Core's model cannot express, kept in the same
            // migration as the tables it belongs to so the schema stays one versioned artefact.
            // The model snapshot does not know about these statements, which is expected.

            // One tenant holds at most one subscription on any given day. Validity is half-open,
            // [valid_from, valid_to), a null valid_to meaning open-ended. btree_gist is what lets a
            // GiST index compare the uuid with '=' alongside the range with '&&'.
            migrationBuilder.Sql(
                "ALTER TABLE catalog.subscription ADD CONSTRAINT ex_subscription_no_overlap " +
                "EXCLUDE USING gist (tenant_id WITH =, daterange(valid_from, valid_to, '[)') WITH &&);");

            // Least privilege (ADR-0004 rule 2, ADR-0007 section 4.4): the runtime role holds, on
            // each table, the smallest set a named component needs, and CatalogSchemaAllowlist
            // .AppRolePrivileges records every grant below with the task, saga step or module that
            // issues it. No DDL, and nothing on the migrations history, which only aurora_migrator
            // maintains. The three cluster roles are a prerequisite of every cluster; a cluster
            // without them fails here, loudly, rather than serving a catalog the application
            // cannot reach.
            //
            // One grant per table, and no ALTER DEFAULT PRIVILEGES that grants, on purpose. Such a
            // default would hand every table a later migration creates the same privileges before
            // anyone decided - on the append-only tables of ADR-0007 section 9.2 that is DELETE on
            // an audit trail, which ADR-0004 rule 5 forbids - and it is permanent: a table created
            // while the default was in force keeps the grant after the default is removed. So each
            // future catalog table grants exactly what it means in the migration that creates it
            // and records the decision in CatalogSchemaAllowlist.AppRolePrivileges; a forgotten
            // grant is a 42501 at first use, a forgotten record fails CatalogPrivilegeTests.
            //
            // Where the grants stop, and why. The rows in these tables are the routing decision:
            // which tenant a host resolves to, which cluster and database a tenant resolves to,
            // which host a cluster is. A request that could write them could rebind another
            // tenant's hostname, send one tenant's requests at another tenant's database, or point
            // the resolver at a host of its own choosing carrying the real cluster credentials -
            // and INSERT is such a write. It fills every column of a new row, so a table-wide
            // INSERT on tenant or tenant_host lets a request create a tenant of its own that
            // resolves to another tenant's database, or a self-verified host for a tenant it does
            // not own; and no column list narrows it, because provisioning has to supply exactly
            // those columns (the second security re-review, H-4). So the request path only reads
            // the routing decision: database_cluster, tenant and tenant_host are SELECT, and on
            // tenant only the lifecycle columns a named request-path component moves are
            // updatable, never the columns a tenant resolves by. Creating a tenant and its host
            // and activating it are the provisioning saga's writes (ADR-0007 section 8 steps 1 and
            // 8), issued as the saga's own principal, not this role: which principal is the
            // architect's decision, and its grants arrive with B-07 in a migration that names it.
            // No table grants DELETE - section 11.4 tombstones a tenant, a subscription closes with
            // valid_to, and DROP DATABASE is aurora_admin's. A write privilege with no component to
            // name is not granted: the task that needs it grants it in its own migration, naming
            // itself.
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA catalog TO aurora_app;");
            migrationBuilder.Sql("GRANT SELECT ON catalog.database_cluster TO aurora_app;");
            migrationBuilder.Sql("GRANT SELECT ON catalog.tenant TO aurora_app;");
            migrationBuilder.Sql("GRANT UPDATE (state, core_schema_version, last_activity_at) ON catalog.tenant TO aurora_app;");
            migrationBuilder.Sql("GRANT SELECT ON catalog.tenant_host TO aurora_app;");
            migrationBuilder.Sql("GRANT SELECT ON catalog.subscription TO aurora_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON catalog.installed_package TO aurora_app;");

            // Functions invert the table rule. PostgreSQL grants EXECUTE on a new function to
            // PUBLIC, so a function a later migration adds to catalog - ADR-0028 section 2
            // mechanism 3 puts the append-only trigger function here - is open to aurora_app with
            // no GRANT statement for a reviewer to notice, and a SECURITY DEFINER body runs as the
            // schema owner, which walks around every grant above (the second security re-review,
            // H-5). This is the one default privilege the catalog sets, and it is the opposite
            // kind from the one the paragraph above refuses: it revokes. Every function
            // aurora_migrator creates in this database from now on starts closed to PUBLIC, the
            // way a table does, and stays closed if this default is ever removed. A migration that
            // means to open one grants EXECUTE to aurora_app by name and records it in
            // CatalogSchemaAllowlist.AppRoleObjectPrivileges. A trigger function needs no such
            // grant: PostgreSQL checks EXECUTE when the trigger is created, not when it fires, so
            // the trigger fires for aurora_app while aurora_app cannot call the function directly.
            // A function created by any other role keeps PostgreSQL's default, and
            // CatalogPrivilegeTests reads a null ACL as that default rather than as nothing, so it
            // reports EXECUTE through PUBLIC and fails until the function is closed or decided.
            //
            // Database-wide, not IN SCHEMA catalog, and not by oversight: a per-schema default is
            // added to the global one, so a per-schema REVOKE can only undo a per-schema GRANT and
            // leaves the built-in EXECUTE to PUBLIC exactly where it was - the statement succeeds,
            // stores nothing, and changes nothing (PostgreSQL, ALTER DEFAULT PRIVILEGES, Notes).
            // Default privileges are per database, so this reaches every function aurora_migrator
            // creates in the catalog database and nothing on any other. The_catalog_sets_exactly_
            // one_default_privilege_and_it_closes_new_functions_to_PUBLIC reads the row back.
            migrationBuilder.Sql("ALTER DEFAULT PRIVILEGES FOR ROLE aurora_migrator REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installed_package",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "subscription",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "tenant_host",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "tenant",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "database_cluster",
                schema: "catalog");
        }
    }
}
