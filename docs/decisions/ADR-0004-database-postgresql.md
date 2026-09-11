# ADR-0004 — Database: PostgreSQL

- **Status:** Accepted (2026-09-10) — **locked by the product owner**
- **Deciders:** product owner (choice), architect (consequences)

## Context

The database is the isolation boundary for tenancy (ADR-0007), the store of record for immutable financial postings, and the unit of backup, restore and deletion for one customer. Test image verified in this environment: `postgres:17-alpine`.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **PostgreSQL 17** *(chosen)* | Permissive PostgreSQL licence, no per-core cost, runs anywhere including a laptop and a test container; exact `numeric` arithmetic; `EXCLUDE USING gist` constraints (used to make overlapping effective-dated tax rates impossible, ADR-0008 §6); `FOR UPDATE SKIP LOCKED` (used by the outbox and the migration runner); schemas as a first-class namespace, which is how module boundaries become visible in the database; mature logical and physical backup tooling; row-level security available as the escape hatch if the tenancy model must change | Each connection is an OS process, so connection fan-out across thousands of databases is the dominant scaling constraint (`scalability.md` §4); `CREATE DATABASE` cannot run inside a transaction; no cross-database queries |
| SQL Server | Excellent .NET integration; strong tooling | Licence cost per core is significant at database-per-tenant density, and it is the wrong shape for a cloud-agnostic, container-first deployment |
| MySQL / MariaDB | Ubiquitous, cheap | Weaker constraint vocabulary (no exclusion constraints), historically weaker transactional DDL and CTE story; less pleasant for a ledger |
| A distributed SQL database (CockroachDB, Yugabyte) | Horizontal scale without sharding work | Novelty cost an SMB ERP does not need; database-per-tenant already gives us a natural shard key; operational and licensing complexity |

## Decision

**PostgreSQL 17** is the only database. Rules:

1. Within a tenant database, **each module owns a schema** and may only create objects in it. Country Packages own `pkg_<id>` schemas (ADR-0008 §4).
2. **Three database roles per cluster, least privilege:**
   - `aurora_admin` — `CREATEDB`; used only by the provisioner, only against the maintenance database; never used by request-serving code.
   - `aurora_migrator` — owns the schemas; used only by the migration runner and package installer.
   - `aurora_app` — `SELECT`/`INSERT`/`UPDATE`/`DELETE` only; **no DDL**; the runtime cannot create, alter or drop anything, including a database.
   The application's connection string uses `aurora_app`. This is what makes "a bug cannot drop a tenant's table" a property of the deployment rather than of our care.
3. Amounts are `numeric(19,4)`, unit prices `numeric(19,6)`, exchange rates `numeric(19,10)`. Never `float`/`double precision`.
4. Accounting *dates* are `date`; event *instants* are `timestamptz` stored in UTC. A posting date is a calendar date in the company's time zone, not an instant, and conflating the two produces period-boundary bugs that are painful to unwind.
5. Append-only tables (`platform.audit_event`, `ledger.journal_entry_line`) have `UPDATE` and `DELETE` revoked from `aurora_app` and a trigger that raises on either — belt and braces, because immutability is a ranked quality attribute.

## Consequences

- **The architect agrees with this choice.**
- The one-process-per-connection model is the reason `scalability.md` treats connection fan-out as bottleneck #1 and why PgBouncer in transaction-pooling mode appears in the design at 1 000 tenants rather than being deferred.
- Transaction pooling forbids session-scoped features. The application therefore may not use `LISTEN`/`NOTIFY`, session-level advisory locks, or `SET` outside a transaction, and Npgsql must run with server-side automatic preparation off (its default) or with PgBouncer ≥ 1.21 prepared-statement support enabled. Recorded here because a developer who reaches for `LISTEN/NOTIFY` will find it works perfectly in development and fails in production.
- `CREATE DATABASE` outside a transaction makes provisioning a **saga with idempotent steps and a reaper**, not a transaction (ADR-0007 §8).
- No cross-database queries means the platform operator cannot write one SQL statement across all tenants. Aggregate operator reporting is an explicit fan-out job that writes into the catalog, not an ad-hoc query. This is a cost of the isolation model and it is worth paying.
- Postgres 17 is the pinned test image. Production must run the same major version; a major-version upgrade is a planned operation per cluster and is recorded in `scalability.md`'s stage table.

## Revisit when

The tenant count per cluster approaches the limits in ADR-0007 §6, or a jurisdiction's data-residency rule requires a database we cannot self-host.
