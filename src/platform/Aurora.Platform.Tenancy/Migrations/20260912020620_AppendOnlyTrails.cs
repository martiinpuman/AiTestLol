using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurora.Platform.Tenancy.Migrations
{
    /// <summary>
    /// The catalog's two append-only trails — <c>catalog.operator_audit_event</c> (ADR-0007 §9.2)
    /// and <c>catalog.erasure_replay_log</c> (ADR-0007 §11.5, ADR-0018 §6) — with the grants and
    /// the guards that make "append-only" a mechanism rather than a name (ADR-0004 rule 5; ADR-0028
    /// §2 as amended; <c>solution-layout.md</c> §6.4 item 5). Tables, grants and guards only: no
    /// writer. The provisioning saga (B-07.1, B-07.4), ADR-0010 rule 8's support-access path and
    /// ADR-0007 §11.5's erasure path write to what this creates.
    /// </summary>
    public partial class AppendOnlyTrails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Raw SQL throughout, on purpose. Neither table is mapped by an entity - no row that
            // writes them has landed - and the guard below is nothing the model can express. The
            // model snapshot therefore does not know these tables, which is expected, and is why
            // every assertion about them is made against the migrated database
            // (CatalogAppendOnlyTests, CatalogAppendOnlyGuardTests, CatalogPrivilegeTests): the
            // B-05 security review (M-1) landed a raw-SQL catalog table with a blanket grant while
            // the model-agreement test and the gate both reported a pass. The columns are derived
            // from the ADRs' prose, not transcribed - CatalogSchemaAllowlist.AppendOnlyColumns
            // records the derivation, CatalogAppendOnlyTests holds the result to
            // information_schema. Writers supply every value, id and instant included: no column
            // defaults, so a row's "when" is the writer's clock and never this database's.

            // The operator trail: every operator action on the platform side of the tenant boundary
            // (ADR-0028 section 6) - a provisioning attempt that failed or was retried and the
            // successful platform-side event (SPEC-001 BR-7), a time-boxed, reason-coded support-
            // access grant (ADR-0010 rule 8), and the daily record of each tenant's audit chain head
            // (ADR-0018 section 1). tenant_id admits null because a reservation can fail before a
            // tenant row exists and a fleet-wide action has no tenant; where present it references
            // the registry, and a referenced tenant cannot be removed (section 11.4 tombstones it).
            // actor_id is required for both actor types - an Operator's platform identity
            // reference, or the component a System action ran as - because "who" is not optional
            // in an audit record. detail carries what the action needs and never personal data or
            // an erased value (ADR-0018 section 4); that is the writer's obligation, which no column
            // can enforce and no test here claims to.
            migrationBuilder.Sql(
                """
                CREATE TABLE catalog.operator_audit_event (
                    id uuid NOT NULL,
                    occurred_at timestamp with time zone NOT NULL,
                    tenant_id uuid NULL,
                    actor_type character varying(32) NOT NULL,
                    actor_id character varying(128) NOT NULL,
                    action character varying(64) NOT NULL,
                    reason_code character varying(64) NULL,
                    correlation_id uuid NULL,
                    detail jsonb NULL,
                    CONSTRAINT pk_operator_audit_event PRIMARY KEY (id),
                    CONSTRAINT ck_operator_audit_event_actor_type CHECK (actor_type IN ('Operator', 'System')),
                    CONSTRAINT fk_operator_audit_event_tenant FOREIGN KEY (tenant_id)
                        REFERENCES catalog.tenant (id) ON DELETE RESTRICT
                );
                """);
            migrationBuilder.Sql(
                "CREATE INDEX ix_operator_audit_event_tenant_id_occurred_at ON catalog.operator_audit_event (tenant_id, occurred_at);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_operator_audit_event_occurred_at ON catalog.operator_audit_event (occurred_at);");

            // The erasure replay log: one row per erasure executed in a tenant - subject reference,
            // tenant, requester, when, which fields - so that after any restore every erasure since
            // the backup is re-applied (ADR-0007 section 11.5; ADR-0018 section 6 point 5). Never
            // the erased values (point 6): affected is the list of entity and field names, held to
            // a JSON array by the check constraint. Executed erasures only: the request and its
            // deferral date live in the tenant database (ADR-0018 section 6 point 3), where a row
            // may change; a row here is final.
            migrationBuilder.Sql(
                """
                CREATE TABLE catalog.erasure_replay_log (
                    id uuid NOT NULL,
                    tenant_id uuid NOT NULL,
                    subject_ref character varying(128) NOT NULL,
                    requested_by character varying(128) NOT NULL,
                    executed_at timestamp with time zone NOT NULL,
                    affected jsonb NOT NULL,
                    correlation_id uuid NULL,
                    CONSTRAINT pk_erasure_replay_log PRIMARY KEY (id),
                    CONSTRAINT ck_erasure_replay_log_affected_is_a_list CHECK (jsonb_typeof(affected) = 'array'),
                    CONSTRAINT fk_erasure_replay_log_tenant FOREIGN KEY (tenant_id)
                        REFERENCES catalog.tenant (id) ON DELETE RESTRICT
                );
                """);
            migrationBuilder.Sql(
                "CREATE INDEX ix_erasure_replay_log_tenant_id_executed_at ON catalog.erasure_replay_log (tenant_id, executed_at);");

            // Least privilege in its append-only shape (ADR-0004 rule 5): aurora_app holds exactly
            // SELECT and INSERT on each table, recorded with its writers in
            // CatalogSchemaAllowlist.AppRolePrivileges, and nothing else - no UPDATE, no DELETE, no
            // TRUNCATE, no column grant. Issued in the same transaction as the CREATE TABLE (EF
            // Core wraps a migration in one), so there is no moment at which the table exists with
            // privileges nobody decided. What holds this true is CatalogPrivilegeTests, which
            // compares the ACL PostgreSQL holds - pg_class.relacl and pg_attribute.attacl, for the
            // role and for PUBLIC - with the record, entry by entry, both ways; the column-level
            // UPDATE the security review walked past has_table_privilege with is an entry like any
            // other. No ALTER DEFAULT PRIVILEGES, in either direction: the GRANT form would hand
            // every later catalog table a privilege before anyone decided, the REVOKE form is a
            // no-op on PostgreSQL 17 (ADR-0028 Amendment 1), and CatalogAppendOnlyTests reads
            // pg_default_acl back as zero rows for this schema.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON catalog.operator_audit_event TO aurora_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON catalog.erasure_replay_log TO aurora_app;");

            // The guard function (ADR-0028 section 2 mechanism 3): what the row-level and the
            // statement-level guard on an append-only table both raise with - TG_OP names the verb,
            // so one function serves UPDATE, DELETE and TRUNCATE. 42501 is the SQLSTATE a privilege
            // refusal carries, so a caller sees one kind of "no" whether the ACL or the guard
            // refused, and the owner - whom privileges alone would not restrain - is refused the
            // same as aurora_app. Created by aurora_migrator, so InitialCatalog's one default
            // privilege closes it to PUBLIC: aurora_app cannot call it and does not need to,
            // because PostgreSQL checks EXECUTE when a trigger is created and not when it fires
            // (A_trigger_function_closed_to_the_app_role_still_fires_for_it), and it is recorded
            // as [] in CatalogSchemaAllowlist.AppRoleObjectPrivileges.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION catalog.refuse_append_only_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION USING
                        ERRCODE = '42501',
                        MESSAGE = format('%I.%I is append-only: %s refused', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP),
                        HINT = 'ADR-0004 rule 5: an audit trail is corrected by a new row, never by rewriting one.';
                END
                $$;
                """);

            // Two guards per table, in the same migration as the table, both enabled ALWAYS
            // (ADR-0028 Amendment 2): a row-level BEFORE UPDATE OR DELETE guard, and a
            // statement-level BEFORE TRUNCATE guard. The second is here although solution-
            // layout.md section 6.4 item 5 reads the truncate question as one that "does not
            // arise" for an unpartitioned table: that reading is about cloning - there is no
            // partition for a truncate trigger to fail to reach - and the TRUNCATE statement does
            // not care whether a table is partitioned. Without it the owner empties either trail
            // in one statement, the hole Amendment 2 found on partitions arriving by another door;
            // the orchestrator widened this row's scope to close it (B-19 rework). CREATE TRIGGER
            // yields an ordinary trigger ('O'), which session_replication_role = 'replica'
            // suppresses with pg_trigger unchanged; the GUC is superuser-only on PostgreSQL 17 and
            // one GRANT SET ON PARAMETER away, and ENABLE ALWAYS costs one statement.
            //
            // What is asserted of the guards is that they refuse, never that they exist:
            // CatalogAppendOnlyGuardTests executes UPDATE, DELETE and TRUNCATE as aurora_migrator
            // inside a transaction it rolls back and requires 42501 and this function's message
            // from each, because CREATE OR REPLACE FUNCTION with a hollow body - RETURN NULL, which
            // a statement-level trigger ignores - is one statement that leaves every pg_trigger
            // column byte-identical while the TRUNCATE lands. The pg_trigger enumeration is kept as
            // the locator that says which table and why, and it is blind to that fault by
            // construction.
            //
            // Neither table is partitioned, so Amendment 2's partition clause - a guard created on
            // every partition, because a truncate guard is never cloned to one - does not arise
            // here. What the guards leave, stated rather than implied: aurora_migrator, as the
            // DDL-path role, can still disable or drop either guard or replace this function's
            // body - the detection-not-prevention boundary ADR-0028 section 2 draws, whose cover
            // is the chain head recorded in operator_audit_event by FOLLOWUP-031's job
            // (FOLLOWUP-026). aurora_app never reaches either guard: its UPDATE, DELETE and
            // TRUNCATE are 42501 from the privilege check, and the ACL comparison keeps it so.
            foreach (string table in new[] { "operator_audit_event", "erasure_replay_log" })
            {
                migrationBuilder.Sql(
                    $"CREATE TRIGGER trg_{table}_append_only BEFORE UPDATE OR DELETE ON catalog.{table} " +
                    "FOR EACH ROW EXECUTE FUNCTION catalog.refuse_append_only_change();");
                migrationBuilder.Sql($"ALTER TABLE catalog.{table} ENABLE ALWAYS TRIGGER trg_{table}_append_only;");
                migrationBuilder.Sql(
                    $"CREATE TRIGGER trg_{table}_append_only_truncate BEFORE TRUNCATE ON catalog.{table} " +
                    "FOR EACH STATEMENT EXECUTE FUNCTION catalog.refuse_append_only_change();");
                migrationBuilder.Sql($"ALTER TABLE catalog.{table} ENABLE ALWAYS TRIGGER trg_{table}_append_only_truncate;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DROP TABLE takes the table's grants, indexes, constraints and triggers with it, so
            // Up and Down are symmetric about privileges as well as about tables.
            migrationBuilder.Sql("DROP TABLE catalog.erasure_replay_log;");
            migrationBuilder.Sql("DROP TABLE catalog.operator_audit_event;");
            migrationBuilder.Sql("DROP FUNCTION catalog.refuse_append_only_change();");
        }
    }
}
