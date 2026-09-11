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

            // Least privilege (ADR-0004 rule 2, ADR-0007 section 4.4). The runtime role reads and
            // writes registry rows and nothing else: no DDL, and nothing on the migrations history,
            // which only aurora_migrator maintains. The three cluster roles are a prerequisite of
            // every cluster; a cluster without them fails here, loudly, rather than serving a
            // catalog the application cannot reach. Tables a later migration adds inherit the same
            // grant through the default privileges; an append-only table must revoke UPDATE and
            // DELETE explicitly in the migration that creates it (ADR-0004 rule 5).
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA catalog TO aurora_app;");
            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON catalog.database_cluster, catalog.tenant, " +
                "catalog.tenant_host, catalog.subscription, catalog.installed_package TO aurora_app;");
            migrationBuilder.Sql(
                "ALTER DEFAULT PRIVILEGES FOR ROLE aurora_migrator IN SCHEMA catalog " +
                "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO aurora_app;");
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
