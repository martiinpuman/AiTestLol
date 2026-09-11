# ADR-0027 — The DDL path (provisioning, migration, package install) is separate from the application data path

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Supersedes:** ADR-0007 §4.1 (tenant `DbContext` constructor signature) and ADR-0007 §7.5 (which access paths the schema-version skew check gates). Everything else in ADR-0007 stands unchanged.
- **Superseded by:** —
- **Related:** ADR-0004 rule 2 (three database roles), ADR-0007 §3.4, §4, §7.3, §8, §10.1, ADR-0008 §5.2, ADR-0003

## Context

The project-manager's split of B-06/B-07/B-08 (`../BACKLOG.md`, 2026-09-11) surfaced a question that is really a gap in ADR-0007: does the migration runner need the full "no `DbContext` without a `TenantScope`" guarantee, given that ADR-0007 §7.3 already says it connects as `aurora_migrator` **directly, bypassing PgBouncer and `ITenantConnectionResolver`**?

Reading ADR-0007 literally produces a contradiction that would have been discovered mid-implementation:

1. §4.1 says a tenant `DbContext` has exactly one constructor and it takes a `TenantScope`. So applying EF migrations requires a `TenantScope`.
2. §3.4 says a `TenantScope` carries `SchemaVersion` and `InstalledPackages` — two facts that are by definition *unknown or in flux* while DDL is running.
3. §7.5 says `ITenantScopeFactory` **fails the scope open** for a tenant below `MinimumSupportedSchemaVersion` and marks it `SchemaBlocked`. Those are exactly the tenants the migration runner exists to repair. A runner that opens scopes cannot fix a `SchemaBlocked` tenant — it is locked out of the database by the mechanism meant to protect it.
4. §5.2's pool settings (`Maximum Pool Size` 10, PgBouncer in front, transaction pooling) are specified for `aurora_app` request traffic and are the wrong shape for a single long DDL session holding a session-level advisory lock (§7.3).

So the DDL path is already a different path in every respect that matters — role, pooling, proxy, lifetime, failure handling — and only the type system pretended otherwise. Two further ownership questions arrived with the same split (who owns the §7.5 check, who computes ADR-0008 §5.2's package upgrade plan); both are answered here because both are consequences of the same seam.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **A. Two explicit access types: `TenantScope` (app) and `TenantDatabaseHandle` (DDL), sharing one abstract `TenantAccess` base** *(chosen)* | The guarantee is unchanged in substance — a tenant `DbContext` still cannot exist without a proven tenant identity, and neither proof type is constructible outside `Aurora.Platform.Tenancy`; the §7.5 gate has no bypass branch because the DDL path has no scope factory to bypass; DDL pooling, credentials and lifetime are free to differ; provisioning and migration stop waiting on the app-path machinery they never use | A second proof type to learn and a second fitness rule; `TenantDatabaseHandle` is a powerful type and needs its call sites restricted mechanically |
| B. DDL opens a `TenantScope` with `TenantAccessReason.Migration` and the §7.5 check gains a bypass branch | One type, one factory | The guarantee acquires an exception whose name ("Migration") is exactly what an attacker or a careless developer would choose; a scope must be manufactured carrying a `SchemaVersion` that is about to stop being true and an `InstalledPackages` set that the same run may change; the check that protects every tenant grows a branch that disables it |
| C. Migrations shipped as pre-generated idempotent SQL scripts; no `DbContext` at migration time | No EF model load at migration time; the SQL is reviewable and is what B-09 already scans | Build-time script generation in every pipeline, developer friction in the inner loop, and it changes B-09 and B-14 now. Kept as a revisit trigger, not adopted today |

## Decision

### 1. Two proofs of tenant identity, one base type

In `Aurora.Platform.Tenancy.Contracts`:

- `abstract class TenantAccess` — internal constructor, carries `TenantId` and `TenantKey`. `[InternalsVisibleTo]` is granted to `Aurora.Platform.Tenancy` and the tenancy test assembly only, exactly as ADR-0007 §3.4 grants it today.
- `sealed class TenantScope : TenantAccess` — unchanged from ADR-0007 §3.4 (`ResidencyRegion`, `SchemaVersion`, `Packages`, `Reason`, `IsActive`, `IAsyncDisposable`). The **application** path: `aurora_app`, through PgBouncer, pooled per §5.2, identity-checked per §4.3, skew-checked per §7.5.
- `sealed class TenantDatabaseHandle : TenantAccess` — the **DDL** path: `ClusterId`, `DatabaseName`, `Role` (`Admin | Migrator`), the open `NpgsqlConnection`, `IdentityVerified`. Direct connection, no PgBouncer, no LRU data-source cache, no schema-version gate, one connection per run, disposed with the run.

**ADR-0007 §4.1's constructor rule is restated, not weakened:** a tenant `DbContext` has exactly one constructor, it is `internal`, and its second parameter is a non-nullable `TenantAccess`. The property "you cannot obtain a tenant `DbContext` without first holding a tenant proof that only `Aurora.Platform.Tenancy` can construct" is preserved verbatim. What changes is that the proof has two forms.

Two factories, one guarantee. `ITenantDbContextFactory<TContext>.CreateAsync(TenantScope, ct)` is unchanged and is the only one a module developer ever sees. `ITenantMigrationContextFactory<TContext>.CreateAsync(TenantDatabaseHandle, ct)` is its DDL-path sibling: implemented **only** in `Aurora.Platform.Tenancy` (same fitness rule as ADR-0007 §4.2 applies to it), never registered for a module's own use, and consumed only by `IModuleSchemaMigrator` implementations (§3).

Fitness rules (extend B-04's set, `modules.md` §12.3 list):
- No tenant `DbContext` has a public or protected constructor (unchanged).
- Every tenant `DbContext` constructor takes exactly one `TenantAccess`-derived parameter.
- **No type in `Aurora.Modules.*`, `Aurora.Web` or `Aurora.Countries.*` may reference `TenantDatabaseHandle`** in any signature or body. Its permitted assemblies are `Aurora.Platform.Tenancy`, the provisioning/migration runner and their test assemblies — a named allow-list, asserted, with a deliberately-violating fixture.

### 2. `ITenantAdminConnectionFactory` — the only way to open a DDL connection

Lives in `Aurora.Platform.Tenancy`; it is the DDL-path sibling of `ITenantConnectionResolver`, and it reuses that resolver's catalog read (`catalog.tenant` + `catalog.database_cluster`) so there is still exactly one place that knows how a tenant maps to physical storage (ADR-0007 §3.5 stands).

```
ValueTask<TenantDatabaseHandle> OpenMaintenanceAsync(ClusterId cluster, CancellationToken ct);          // aurora_admin, maintenance database
ValueTask<TenantDatabaseHandle> OpenAsMigratorAsync(TenantId tenant, StampAssertion assertion, CancellationToken ct);
```

`StampAssertion` is a required, non-defaulted enum with exactly two values:

- `RequireStampMatchesTenant` — reads `platform.tenant_identity` and throws `TenantRoutingViolationException` (alert, tenant → `SchemaBlocked`) before returning the handle. **Every caller except the three named below uses this.**
- `AllowUnstampedDuringProvisioning` — legal only inside provisioning steps 2, 3 and 4, where the stamp does not exist yet. A fitness test restricts its call sites to the provisioning saga's step types by name.

This is the DDL-path equivalent of ADR-0007 §4.3 and carries the same weight: §4.3's per-physical-connection initializer belongs to the app data source and does **not** protect the migrator connection. Without this the DDL path would be the one path in the system that can run `DROP` against the wrong database undetected. Each of B-07.1, B-07.2, B-08.1 and B-13.2 proves it with a deliberate mis-route test of its own.

The stamp definition and the assertion query exist **once**, as `TenantIdentityStamp` in `Aurora.Platform.Tenancy` (the DDL of ADR-0007 §4.3's table plus `AssertAsync(NpgsqlConnection, TenantId, ct)`). The app-path initializer (§4.3), the DDL factory, provisioning step 4 and the compensation guard all call it. Three hand-written copies of this query is how they drift.

### 3. One single-tenant migration executor, one fleet orchestrator

- `ITenantSchemaMigrator.MigrateAsync(TenantDatabaseHandle, SchemaVersion target, ct)` — **migrate one tenant**: take `pg_advisory_lock(hashtext('aurora.migration'))` on that session, assert the stamp, run `platform` → module schemas in `../architecture/modules.md` dependency order → `pkg_*`, report `from`/`to` version. This is ADR-0007 §8 step 5 and the per-tenant half of §7.3, and it is **one component**, not two: building it twice, once in the saga and once in the runner, would give the most dangerous DDL path in the system two implementations that drift.
- The fleet runner owns everything about *which* tenants and *when*: `catalog.migration_run*`, `FOR UPDATE SKIP LOCKED` claiming, leases and heartbeats, resumability, quarantine, failure budget, waves. It calls `ITenantSchemaMigrator` per tenant.
- Modules contribute migrations through `IModuleSchemaMigrator` implementations registered by their `AddXModule()` extension in `Aurora.Composition`. The runner assembly references **no** module assembly, so ADR-0007 §10.1's rule that an `IPlatformJob` may not reference a module's `.Domain`, `.Application` or `.Infrastructure` holds unchanged — which it would not if the runner named module `DbContext` types directly.

### 4. The schema-version skew check belongs to Tenancy

ADR-0007 §7.5's check is `ITenantScopeFactory` behaviour: it runs on scope open, it is tested by stamping a version row and opening a scope, and it needs no migration runner to exist. **It is Tenancy's, and it ships with the scope factory.** The migration runner is a consumer of the constants, never the owner of the check.

The constants live in `Aurora.Platform.Tenancy.Contracts` as `CoreSchemaVersion.Current` and `CoreSchemaVersion.MinimumSupported` — the assembly both the scope factory and the runner already reference, and the same assembly as `SchemaVersion` itself. Two tests keep them honest, because a hand-maintained constant that nothing checks is a comment:

- `Current` equals the highest ordinal across the registered `IModuleSchemaMigrator` set (fails when someone adds a migration and forgets the constant);
- `Current - MinimumSupported <= 1` (§7.5 defines `MinimumSupported` as N−1).

**The check applies only to `TenantScope` opens.** There is no "skip for migration" branch, and there cannot be one: the DDL path holds a `TenantDatabaseHandle` and never touches the scope factory. That is the property option B could not give us.

### 5. The package upgrade plan is a pure function owned by Countries

ADR-0008 §5.2 step 1 — "for each installed package, the lowest version compatible with both the current and the target core contract version" — is a **pure function** over (the tenant's installed set from `catalog.installed_package`, the package catalogue, the current core contract version, the target core contract version). It reads no tenant database and needs no installer.

- `IPackageUpgradePlanner` is declared in `Aurora.Countries.Contracts` and implemented in `Aurora.Countries.Hosting`, next to the manifest and `coreContractRange` semantics that define the version algebra. It is unit-tested with no database at all.
- **Callers:** the release-build fleet compatibility report, and the migration runner's per-tenant gate that sets `Skipped` / `PackageIncompatible` (ADR-0008 §5.2). The runner orchestrates; it does not re-implement the algebra.
- **The installer executes a plan; it never computes one.** ADR-0008 §5.2's promise — an incompatibility is discovered on our build server, not on a customer's tenant — depends on this: a core upgrade is planned entirely from the catalog plus the package catalogue, without touching a single tenant database or instantiating a single tenant's installer.

## Consequences

- Positive: the migration runner can repair a `SchemaBlocked` tenant. Under ADR-0007 as written it could not, and that would have been found the first time a wave quarantined a tenant.
- Positive: the §7.5 gate and the §4 guarantee have no bypass branch. A DDL-capable proof type exists, is named, is restricted to an asserted allow-list of assemblies, and is additionally powerless in production without `aurora_migrator` credentials (ADR-0004 rule 2).
- Positive: provisioning and the migration runner no longer depend on the app-path machinery (`TenantScope` lifetime semantics, the LRU data-source cache, `ITenantScopeAccessor`). They depend on the tenancy kernel types and the connection resolver only, which frees the B-07/B-13/B-10/B-14/B-15 spine to run in parallel with B-06.2/B-06.3 instead of behind them.
- Negative: two proof types. Mitigated by the shared base, the allow-list fitness rule and the fact that a module developer only ever sees `TenantScope` (ADR-0007 §4.5 is unchanged).
- Negative: the DDL path does not inherit §4.3's per-physical-connection initializer, so the stamp assertion is explicit and each DDL task must prove it with its own mis-route test. Stated as an acceptance criterion rather than left to memory.
- Negative: `AllowUnstampedDuringProvisioning` is a hole by construction, bounded by three named call sites and a fitness test. It exists because a database that has not been stamped yet genuinely cannot prove anything; provisioning step 2's ownership-and-emptiness rule (ADR-0007 §8) is what covers that window.

## Revisit when

Migrations move to pre-generated idempotent SQL scripts (option C), which would remove `ITenantMigrationContextFactory` and most of §1 above; or the DDL path needs pooling because fleet migration time is dominated by connection setup; or a second DDL-capable role appears (for example a read-only schema inspector for support), at which point `TenantDatabaseHandle.Role` stops being a two-value enum and this ADR should be re-read before it is extended.
