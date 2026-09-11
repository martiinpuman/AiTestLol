# Security re-review — TASK B-05 (second reviewer, rework of H-1 and H-2)

**Reviewer:** security-reviewer (second security reviewer; did not author the code and did not write
`security-B-05.md`) · **Tier:** FULL (unchanged)
**Worktree:** `/tmp/sec-rereview-B-05`, detached at `0c26ca3` · **Reference standard:** OWASP ASVS L2
**Scope:** the tenant→platform boundary (the catalog is the only shared database) and the
operator boundary. Correctness, tests and style are `docs/reviews/B-05.md`'s pass. H-3 (ADR-0028 §2),
the stage-8 gate floor, the cluster-bootstrap backlog row and the fourth-role question are out of
scope by the brief; where the rework changes what the architect must decide, that is said in Routing.

## Verdict: REJECT

The two findings the rework was asked to close are closed on the axis they were written about. Every
statement the first reviewer used to take a tenant over is now `42501`, and the new oracle is a
genuine improvement: it catches column grants, `MAINTAIN`, `PUBLIC`, `WITH GRANT OPTION` and
two-level role membership, all of which I verified by hand against PostgreSQL 17.11.

It is rejected because the rework fixed the *verbs the first reviewer happened to use* rather than
the property those verbs violated, and because the new oracle has a blind object class whose
PostgreSQL default is the opposite of this design's fail-closed rule.

- **H-4:** I reproduced H-2's cross-tenant routing takeover end to end as `aurora_app` using only
  `INSERT` — the verb the rework kept table-wide on `catalog.tenant` and `catalog.tenant_host`.
  Two statements, no `UPDATE`, no `DELETE`, no DDL.
- **H-5:** a `SECURITY DEFINER` function in schema `catalog` lets `aurora_app` rewrite
  `catalog.tenant.database_name` — the exact column the column-level `UPDATE` grant exists to
  protect — and the new ACL diff reports **zero differences**. Functions need no `GRANT` at all:
  PostgreSQL's default is `EXECUTE TO PUBLIC`. ADR-0028 §2 mechanism 3 puts such a function in
  `catalog` by name.

---

## Gate and integration results, as I observed them

```
./scripts/verify.sh                                        → RESULT: PASS  (exit 0, 25.5s)
   6   Unit tests                  PASS   4.9s   370 test(s) executed
   stages 4 5 7 8 9 10 PENDING (owned by B-11)   ← stage 8 is Integration tests
dotnet test Aurora.sln -c Release --filter 'Category=Integration'
                                                           → Total tests: 47  Passed: 47  (13.0s)
git status --porcelain                                     → empty, before and after every probe
```

Unit 365 → **370** and integration 44 → **47** are as the author reports.

The three counts are real, and I checked each against a cluster I built myself:

| Printed | Verified |
|---|---|
| `Read 13 ACL entries reaching aurora_app across 6 catalog relations` | The oracle SQL run standalone returns **14 rows**: 13 ACL entries (1 `database_cluster` + 2+4 `tenant` + 2 `tenant_host` + 1 `subscription` + 3 `installed_package`) plus the `LEFT JOIN`'s one NULL row for `__EFMigrationsHistory`. 6 relations. Exact. |
| `Tried 8 routing writes … every one refused with 42501` | 8 is `attempts.Length` over a literal array the loop actually executes, each asserting `42501` individually. A real count of executed attempts, not a constant beside an unrelated loop. |
| `Tried DELETE on 6 catalog tables` | Enumerated from `pg_class`, and guarded by `tables.Count.ShouldBe(AppRolePrivileges.Count)` **before** the loop. |

**Can a run that examined nothing print a pass?** No, and I checked the path rather than trusting the
comment. If the ACL query returned no rows, `held.ByRelation` is empty, `Differences` reports all six
recorded relations as "does not exist in the migrated schema", and `ByRelation.Count.ShouldBe(6)`
fails as well. The DELETE test asserts its count before it loops. `CatalogPrivilegeAllowlistTests`
carries `ShouldNotBeEmpty()` or `examined.ShouldBeGreaterThan(0)` on every list it walks. This is the
`CLAUDE.md` self-check #2 property, correctly built.

---

## What I attacked and what happened

All probes ran against `postgres:17-alpine` (PostgreSQL 17.11) in a throwaway container built the way
`CatalogDatabaseFixture` builds one — three roles, catalog database owned by `aurora_migrator`,
`REVOKE ALL … FROM PUBLIC` over every row of `pg_database` — with the **real migration** applied from
`dotnet ef migrations script`. Nothing was run against the branch; the worktree is untouched.

### 1. The first reviewer's battery, re-run on the reworked branch

Every one refused, as `aurora_app`, from its own connection:

| Attack | Result |
|---|---|
| `DELETE FROM catalog.tenant_host WHERE host='globex.aurora.app'` | `ERROR: permission denied for table tenant_host` |
| `UPDATE catalog.tenant_host SET tenant_id=<acme>` (rebind a hostname) | `permission denied for table tenant_host` |
| `UPDATE catalog.tenant SET database_name='aurora_t_globex'` | `permission denied for table tenant` |
| `UPDATE catalog.database_cluster SET host='attacker.example.net'` | `permission denied for table database_cluster` |
| `UPDATE catalog.tenant SET key=…` / `SET cluster_id=…, residency_region=…` | `permission denied for table tenant` |
| `DELETE FROM` each of the six catalog tables (`WHERE false`) | `permission denied` on all six, `__EFMigrationsHistory` included |
| `TRUNCATE catalog.tenant_host` | `permission denied for table tenant_host` |

And the indirect routes into the protected columns are closed too, which is the part I expected to
break and did not:

| Attack | Result |
|---|---|
| `INSERT … ON CONFLICT (id) DO UPDATE SET database_name=…` | `permission denied for table tenant` — PostgreSQL checks the column `UPDATE` privilege on the conflict action |
| `MERGE … WHEN MATCHED THEN UPDATE SET database_name=…` | `permission denied for table tenant` |
| `CREATE VIEW v AS SELECT * FROM catalog.tenant` (launder the write through an updatable view) | `permission denied for schema public` — `aurora_app` has no `CREATE` anywhere |
| `UPDATE … RETURNING database_name` | allowed, but `SELECT` is granted on that column anyway; no privilege crossed |

The column-level `UPDATE` on `catalog.tenant` is a real mechanism, not a convention. It holds on
every write path I could find that starts from an `UPDATE`.

### 2. The takeover the rework does not stop — `INSERT`

`INSERT` was kept table-wide on `catalog.tenant` and `catalog.tenant_host`, and at the SQL level
`INSERT` writes **every** column of a new row, including all four the column-level `UPDATE` grant
was carved out to protect. There is no unique index on `tenant.database_name` (only `pk_tenant` and
`ux_tenant_key`), and `tenant_host`'s primary key is the host, so a *new* host is always insertable.
See **H-4**. Executed; two statements.

### 3. The new oracle, attacked the way the first reviewer attacked the old one

| # | What I tried | Result |
|---|---|---|
| 1 | Column grant, `MAINTAIN`, `PUBLIC`, `WITH GRANT OPTION` — the four H-1 shapes | **Caught.** The drift test asserts all four by message, and the ACL read genuinely produces them. H-1 (a) and (b) are closed. |
| 2 | A grant to a role `aurora_app` inherits **two levels up** (`GRANT DELETE … TO lvl2; GRANT lvl2 TO lvl1; GRANT lvl1 TO aurora_app`) | **Caught.** `pg_has_role(…, 'USAGE')` is transitive; the oracle reported `tenant_host DELETE … lvl2`. The author's own test only proves one level; two works as well. |
| 3 | The same grant with `GRANT catalog_writer TO aurora_app WITH INHERIT FALSE, SET TRUE` | **Missed.** Oracle reports 0 entries; `aurora_app` then `SET ROLE catalog_writer` and ran `UPDATE catalog.tenant SET database_name='aurora_t_globex'` → `UPDATE 1` and `DELETE FROM catalog.tenant_host` → `DELETE 1`. → **M-3** |
| 4 | A **function** in `catalog` — `SECURITY DEFINER`, owned by `aurora_migrator`, no `GRANT` statement at all | **Missed, and it is a full bypass.** `proacl` is `NULL`, which means `EXECUTE TO PUBLIC`. `aurora_app` called it and rewrote `catalog.tenant.database_name`; the oracle still read 14 rows. → **H-5** |
| 5 | A **sequence** in `catalog` (`relkind='S'`, absent from the oracle's kind list) with `GRANT ALL` | **Missed.** `aurora_app` ran `nextval()`. Low impact today (no sequences in the catalog), but the relation is not even reported as needing a decision. → **L-1** |
| 6 | A relation whose `relacl` is **NULL**, whose owner `aurora_app` inherits | **Mis-reported.** `aclexplode(NULL)` yields no rows, so `__EFMigrationsHistory` reads as "holds nothing" while, after `GRANT aurora_migrator TO aurora_app`, `aurora_app` ran `DELETE FROM catalog."__EFMigrationsHistory"` → `DELETE 1`. The overall diff still failed loudly in that scenario (every non-NULL `relacl` produced `… through aurora_migrator` differences), so this is a mis-report inside a test that still goes red. → **L-2** |
| 7 | `GRANT CREATE ON SCHEMA catalog TO aurora_app` (schema ACL, not a relation ACL) | **Invisible to the diff, but caught elsewhere.** `aurora_app` created and owned `catalog.mine`; `The_app_role_cannot_change_the_schema` asserts `CREATE TABLE catalog.smuggled` is `42501`, so that grant fails the suite. Fine. |
| 8 | Re-introduce `ALTER DEFAULT PRIVILEGES … GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO aurora_app` | **Invisible to the diff while no table exists** (`pg_default_acl` has a row, oracle still reads 14), **but caught the moment one does**: the next table came out `aurora_app=arwd/aurora_migrator` and my `DELETE` on it succeeded — which is exactly what `A_table_created_without_a_grant_of_its_own_is_closed_to_the_app_role` creates a table to assert is `42501`. The "no default privileges" invariant has a real mechanism behind it. Good. |

### 4. What else I attacked and found nothing wrong with

- **No secrets, no personal data, no connection strings anywhere new.** Every `_output.WriteLine`
  added by this rework emits counts, role names, table names and database names only. No
  `EnableSensitiveDataLogging`, no `EnableDetailedErrors`, no logging in `src/` at all.
- **The `differences.Count.ShouldBe(7)` exact count** in the drift test is right, not brittle in the
  wrong direction: an eighth difference fails loudly rather than passing.
- **L-2 of the first review is fixed** — the drift test mints `Unique.Identifier("drift")` instead of
  colliding with the real `operator_audit_event`.
- **The stage-6 half is a real control now.** `CatalogPrivilegeAllowlistTests` holds the *record* to
  four rules (no `DELETE`/`TRUNCATE`, `database_cluster` read-only, no `UPDATE` on a routing column,
  a named writer on every grant) with Docker stopped, and every list it walks is asserted non-empty.
  That is the half of M-1 the first reviewer asked for, and it runs in the gate.

---

## High

### H-4 — `INSERT` writes every routing column, so H-2's cross-tenant takeover reproduces with the two grants the rework kept

`src/platform/Aurora.Platform.Tenancy/Migrations/20260911172124_InitialCatalog.cs:246,248` ·
`tests/unit/Aurora.Platform.Tenancy.UnitTests/CatalogSchemaGuard.cs:291-297,345-358` ·
`tests/unit/Aurora.Platform.Tenancy.UnitTests/Catalog/CatalogPrivilegeAllowlistTests.cs:117-139` ·
`src/platform/Aurora.Platform.Tenancy/README.md` ("Downwards")

The rework's containment argument is that `key`, `cluster_id`, `database_name` and
`residency_region` are unwritable from the request path. They are unwritable **by `UPDATE`**. They
are fully writable by `INSERT`, which is granted table-wide, and a routing decision does not care
whether the row is old or new. As `aurora_app`, against the real migration, no DDL, no superuser,
no `UPDATE`, no `DELETE`:

```sql
INSERT INTO catalog.tenant (id,key,display_name,state,cluster_id,database_name,
                            residency_region,core_schema_version,plan,created_at)
SELECT '9999…','attacker','Attacker','Active',
       t.cluster_id, t.database_name, t.residency_region, t.core_schema_version, 'std', now()
FROM catalog.tenant t WHERE t.key='globex';            -- INSERT 0 1
INSERT INTO catalog.tenant_host
VALUES ('attacker.aurora.app','9999…',true,now());     -- INSERT 0 1
```

What the resolver of ADR-0007 §3.2 strategy 1 now computes, read back from the catalog:

```
    request_host     | resolves_to_tenant |   connects_to
---------------------+--------------------+-----------------
 globex.aurora.app   | globex             | aurora_t_globex
 attacker.aurora.app | attacker           | aurora_t_globex     <-- my host, Globex's database
```

Note the `SELECT … FROM catalog.tenant WHERE key='globex'`: `SELECT` is granted, so the victim's
`cluster_id`, `residency_region` and `core_schema_version` are copied rather than guessed, which
satisfies `fk_tenant_cluster_in_region`, `ck_tenant_routing_present_unless_deleted` and B-08.3's
schema-skew check at the same time. `ux_tenant_key` and `pk_tenant_host` do not bite, because
nothing is being overwritten — the row is new. There is **no unique index on
`catalog.tenant.database_name`**; I checked `pg_indexes`.

This is H-2's crossing #2 ("cross-tenant routing") verbatim, and the mitigation position is
unchanged from the first review: the only thing between it and cross-tenant reads is §4.3's
connected-database identity check, which is B-06.2 and **does not exist yet**. It would catch this
particular row, because §4.3 compares the stamped `tenant_id` with the expected one and the attacker
cannot insert a duplicate `pk_tenant`. So the honest statement is: the takeover works today against
nothing, and will work tomorrow against one runtime check in another task — not against the grant.

A second shape needs no fake tenant at all and §4.3 cannot see it, because the tenant is genuine:

```sql
INSERT INTO catalog.tenant_host VALUES ('acme-mail.aurora.app', <acme's real id>, false, now());
```
```
           host            |              tenant_id               | is_primary | verified
---------------------------+--------------------------------------+------------+----------
 acme-mail.aurora.app      | 11111111-…                           | f          | t
```

`verified_at` is stamped by the inserter. The author's recorded decision — no write privilege on
`tenant_host.verified_at` or `is_primary` — is true only for rows that already exist; for a new row
the request path sets both, for **any** tenant id, and a tenant with no primary host yet (i.e. one
mid-provisioning) can have its primary claimed. Strategy 3's `tid` cross-check means this is not by
itself an authentication bypass, but it is custom-domain verification forged from the request path,
and `tenant_host` is a routing table.

**Fix.** `INSERT` is the write the column mechanism cannot express — provisioning must supply exactly
the routing columns, so there is no useful column-scoped `INSERT` to grant. The decision that is
actually open is therefore not "which columns" but **which principal**:

1. Move `ReserveTenant` (B-07.1) and `RegisterRouting` (B-07.4) off the request-path role. They are
   saga steps in a provisioning process, not request-path writes; giving them their own catalog role
   (the architect's fourth-role question, now with a concrete answer) removes `INSERT` on both
   routing tables from `aurora_app` entirely and leaves it `SELECT`-only on the routing decision.
2. If the fleet cannot yet be split by process, at minimum record in `AppRolePrivileges` that
   `INSERT` on a table listed in `RoutingColumns` writes those columns, and extend
   `No_grant_lets_the_request_path_update_a_column_a_tenant_or_a_host_resolves_by` to cover `INSERT`
   as well as `UPDATE` — today it filters on `Privilege.StartsWith("UPDATE")` and so cannot see the
   grant that carries the risk. Then state the residual risk beside the grant instead of stating a
   containment that the grant does not provide.
3. Add the two-`INSERT` takeover to `The_app_role_cannot_repoint_where_a_tenant_or_a_host_resolves`.
   That test makes eight writes and not one of them is an `INSERT` on `tenant` or `tenant_host`; the
   verb that works is the one the test does not try.
4. Whatever the outcome, correct the README's "Downwards" bullet and `RoutingColumns`' remarks. They
   currently tell B-06 and B-07's implementers that the three writes of the first review are what the
   grants prevent. Two of the three are prevented; the third is reachable by another verb.

### H-5 — the ACL diff does not look at functions, and PostgreSQL's default for a function is `EXECUTE TO PUBLIC`; I rewrote a routing column through one with zero differences reported

`tests/integration/Aurora.Platform.Tenancy.IntegrationTests/CatalogPrivilegeTests.cs:74-91`
(`AclEntriesSql`), `:62-72` (the doc comment) · `docs/decisions/ADR-0028-…:41`

The oracle reads `pg_class.relacl` and `pg_attribute.attacl` for `relkind IN ('r','p','v','m','f')`.
It never reads `pg_proc.proacl`. That would be a scoping decision rather than a hole, except that
the fail-closed rule the whole design rests on — *a relation created without a grant of its own is
closed to the app role* — **inverts** for functions: a new function's `proacl` is `NULL`, which means
`EXECUTE` is granted to `PUBLIC`. There is no `GRANT` statement for a reviewer to notice in the diff,
because none is needed.

Executed, as `aurora_migrator` then as `aurora_app`:

```sql
CREATE FUNCTION catalog.touch_activity(uuid) RETURNS void LANGUAGE sql SECURITY DEFINER AS $$
  UPDATE catalog.tenant SET database_name = 'aurora_t_globex' WHERE id = $1 $$;
-- proacl: NULL   (= EXECUTE TO PUBLIC)
-- oracle: 14 rows, unchanged. Zero differences.
```
```
aurora_app=> SELECT catalog.touch_activity('1111…');
aurora_app=> SELECT key, database_name FROM catalog.tenant WHERE key='acme';
 acme|aurora_t_globex
```

That is the column-level `UPDATE` of H-2's fix walked around completely, by the role it was written
to constrain, with the control that replaced H-1's oracle reporting a perfect match — H-1's exact
failure mode, in the mechanism installed to prevent H-1.

This is not hypothetical machinery. ADR-0028 §2 mechanism 3 is "a `BEFORE UPDATE OR DELETE` trigger
that raises", and §2's closing sentence says "the same three-way enforcement is retrofitted to
`catalog.operator_audit_event` and `catalog.erasure_replay_log`". The first function in schema
`catalog` is already specified, and it arrives on the append-only tables — the ones where an
`EXECUTE`-to-`PUBLIC` surprise matters most. There are **0** functions in `catalog` today, which is
why this is high and not critical: the control is blind before the object class exists rather than
after.

**Fix.**
1. Add `pg_proc.proacl` to the oracle, with the same `aclexplode` / `PUBLIC` / membership shape, and
   record function decisions in `AppRolePrivileges` (or a sibling map) spelled `EXECUTE`. A `NULL`
   `proacl` must read as `EXECUTE through PUBLIC`, not as "no entries" — this is the one place where
   the implicit default must be materialised rather than skipped.
2. Make the migration `REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA catalog FROM PUBLIC` (harmless
   today, correct forever), and state in the migration comment that a function in `catalog` is
   granted to `PUBLIC` unless revoked — the inverse of the table rule the comment above it explains
   so well.
3. Add a drift shape for it: create a function in the transaction and assert the difference. Today's
   drift test proves seven shapes and would prove an eighth in one line.
4. Narrow the doc comment at `:62-72` until (1) is done. It says "Every ACL entry on every relation
   in `catalog` that reaches the app role"; a reader takes that as "everything `aurora_app` can do in
   `catalog`", and the first thing it will not cover is the trigger function ADR-0028 has already
   ordered. `CLAUDE.md` self-check #1: the claim and the mechanism must sit together.

---

## Medium

### M-3 — the oracle asks `pg_has_role(…, 'USAGE')`, which is blind to a membership reachable by `SET ROLE`

`tests/integration/Aurora.Platform.Tenancy.IntegrationTests/CatalogPrivilegeTests.cs:91`

`'USAGE'` means "inherits the privileges of". Since PostgreSQL 16 a membership can be granted
`WITH INHERIT FALSE, SET TRUE`: the login does not inherit the privileges, but it may `SET ROLE` into
them at will, which is the same thing one statement later. Executed:

```sql
-- as superuser
CREATE ROLE catalog_writer; GRANT USAGE ON SCHEMA catalog TO catalog_writer;
GRANT SELECT, UPDATE, DELETE ON catalog.tenant TO catalog_writer;
GRANT SELECT, DELETE ON catalog.tenant_host TO catalog_writer;
GRANT catalog_writer TO aurora_app WITH INHERIT FALSE, SET TRUE;
-- pg_has_role('aurora_app','catalog_writer','USAGE') = f   → oracle reports 0 entries
-- as aurora_app
SET ROLE catalog_writer;
UPDATE catalog.tenant SET database_name='aurora_t_globex' WHERE key='acme';   -- UPDATE 1
DELETE FROM catalog.tenant_host WHERE host='globex.aurora.app';               -- DELETE 1
```

H-2's takeover, through an oracle that reports a perfect match. The author was right that membership
is an axis worth testing — `A_privilege_that_reaches_the_app_role_through_another_role_…` injects
exactly this fault as the superuser and it passes because the membership uses the default `INHERIT`.
One flag on the same `GRANT` and it goes blind. Medium rather than high because injecting it needs
cluster-level role DDL that none of the three roles hold; but that is precisely the M-2 threat the
first review raised and nobody has closed — nothing in the product asserts anything about the cluster
it is installed on.

**Fix.** Use `pg_has_role(@role, e.grantee, 'MEMBER')` — the predicate that matches what a login can
reach — and label the entry `through <role> (SET ROLE)` when `USAGE` is false, so the two are
distinguishable in the message. Add the `WITH INHERIT FALSE` variant to the membership test.

### M-4 — the grant record has a column axis and no row axis: every catalog write grant is a write on every other tenant's row

`tests/unit/Aurora.Platform.Tenancy.UnitTests/CatalogSchemaGuard.cs:345-368`

The catalog is shared by design, so a privilege on `catalog.tenant` is a privilege on *every* tenant's
row. `AppRolePrivileges` records, per grant, the component that needs it — a real improvement — but
the record and the tests reason entirely in columns, and a reviewer reading them will conclude the
blast radius is bounded. It is bounded in one dimension only. Executed as `aurora_app`:

```sql
UPDATE catalog.tenant SET state='Suspended'      WHERE key='globex';  -- UPDATE 1
UPDATE catalog.tenant SET state='PendingDeletion' WHERE key='globex'; -- UPDATE 1
INSERT INTO catalog.installed_package
  VALUES ('<globex id>','se_vat','../../../evil/1.0','Active',now(),'aurora_app');  -- INSERT 0 1
UPDATE catalog.installed_package
  SET version='0.0.1-attacker', state='Active', installed_by='nobody'
  WHERE tenant_id='<globex id>';                                      -- UPDATE 1
```

Three consequences worth naming:

1. **`UPDATE(state)` reaches any tenant's row, and `state` drives §11.4.** `Suspended` is "read-only,
   end-of-service banner, jobs skipped"; `PendingDeletion` is "database renamed to
   `deleted_<key>_<date>`, `CONNECT` revoked from everyone but `aurora_admin`". A destructive fleet
   transition is one granted `UPDATE` away from the request path, for a tenant the request has
   nothing to do with. The named writers (quarantine, reaper, identity check) are all legitimate;
   none of them needs to write another tenant's row, and nothing stops them.
2. **`installed_package` is table-wide `INSERT` + `UPDATE`, and it is the core↔Country-Package
   boundary.** A request-path caller can write another tenant's installed package set, including an
   arbitrary `version` string that B-12/B-13's loader will resolve to an assembly. Whatever that
   resolution does with a caller-controlled version belongs in B-13's threat model, and B-13 should
   be told the value is not trustworthy.
3. **`installed_by` is free text written by the same role**, so package-install attribution is
   forgeable. `CLAUDE.md` requires every financial change to be audit-logged with who; this is not
   financial, but it is the same shape, and it is the column an incident review would read.

**Fix.** No grant can fix this — the row axis has to be enforced above the database, so say so where
it will be read: add a paragraph to `AppRolePrivileges`' remarks and the README stating that a
catalog write grant is unscoped by tenant and that the scoping is the caller's, and hand B-13 and
B-07 the requirement that a catalog write names the tenant it is acting for and is checked against
the resolved scope. Consider narrowing `installed_package` to `INSERT` + `UPDATE(state, version)` so
`installed_at`/`installed_by` are append-only, which is cheap and matches ADR-0004 rule 5's spirit.

### M-5 (carried forward, unfixed) — the two High fixes are both enforced by tests the gate does not run

The first review's M-1. Stage 8 is still `PENDING (owned by B-11)`, so `CatalogPrivilegeTests` —
which now contains the entire H-1 oracle and the entire H-2 proof — never executes in
`scripts/verify.sh`. The stage-6 half is genuinely better than it was (`CatalogPrivilegeAllowlistTests`
holds the record to four rules and reports what it examined), and that is the half the first reviewer
asked for, so this is weaker than it was. But a migration that changes a `GRANT` line without
touching the record still passes the only gate the project has. The stage-8 floor is B-11's and out
of scope here; the first reviewer's other half of the fix — one line in the module README saying the
privilege record is enforced by a test the gate does not run — costs nothing and is still absent.

### M-6 (carried forward, unfixed) — nothing asserts anything about the cluster the catalog is installed on

The first review's M-2. There is still no `DO $$` block in the migration checking that `aurora_app`
is not `rolsuper`/`rolcreatedb`/`rolcreaterole`/`rolbypassrls`, that `aurora_migrator` owns
`current_database()`, or that `PUBLIC` has no `CONNECT`. I re-confirmed the underlying point in
passing: every privilege claim in this task is a property of a cluster the fixture builds, and the
repository contains no artifact that builds a real one. Raised once, unfixed, and it is the premise
M-3 relies on — noted here so it is raised twice rather than silently dropped.

---

## Low

**L-1 — sequences are outside the oracle's relation kinds.** `relkind IN ('r','p','v','m','f')`
omits `'S'`. `GRANT ALL ON SEQUENCE catalog.smuggled_seq TO aurora_app` is invisible and `nextval()`
worked. No sequences exist in the catalog today (uuid keys), and the impact of a sequence grant is
small, but the relation is not even reported as needing a decision. Add `'S'` and spell the
privileges `USAGE`/`SELECT`/`UPDATE`.

**L-2 — a `NULL` `relacl` reads as "holds nothing" even when the role holds everything.**
`aclexplode(NULL)` returns no rows, so the implicit owner default is never materialised. Demonstrated
on `__EFMigrationsHistory` (`relacl` `NULL`, recorded as `[]`): after `GRANT aurora_migrator TO
aurora_app` the oracle still reported nothing for it while `DELETE FROM
catalog."__EFMigrationsHistory"` returned `DELETE 1`. The scenario is caught overall — every other
relation screamed `… through aurora_migrator` — and that table has its own direct probe test, so this
is a mis-report inside a red test rather than a green one. Either read the owner into the entry set
when `relacl IS NULL`, or assert the owner is never a role `aurora_app` reaches.

**L-3 — three unowned future obligations are recorded in doc comments with no backlog row.** The
§11.4 offboarding columns, `subscription`'s absent writer, and the fourth-role question are all
written into `CatalogSchemaGuard.cs` remarks and the README. The *decision* (grant nothing until a
task needs it) is the right one and I would not change it. But `CLAUDE.md` forbids a TODO without a
backlog ID, and three of them in an XML doc comment are still TODOs. Ask the project manager for the
rows; the comments can then point at them.

**L-4 — the first review's L-1, L-3 and L-6 are untouched** (the same-transaction `GRANT` invariant,
`Down()` leaving `aurora_app=U` on the schema, `MintPassword() => Guid.NewGuid()`). All three remain
correct as judgements; L-6 is the one worth a line of comment, because B-07/FOLLOWUP-001 will copy it
when they mint real per-tenant role passwords.

---

## The three choices beyond the brief, judged

**Column-level `UPDATE` on `catalog.tenant` rather than table-wide — right, and better than I
expected.** I attacked it through `ON CONFLICT DO UPDATE`, `MERGE`, an updatable view and `RETURNING`,
and PostgreSQL enforces the column ACL on every one of them. The four granted columns are the four
that have named writers. The argument fails only because `INSERT` is a write too (H-4), which is a
gap in the *decision*, not in the mechanism.

**No write on `subscription`, none on `tenant_host.verified_at`/`is_primary` — half right.**
`subscription` read-only is correct and well argued: no backlog row bills anyone, so there is no
writer to name, and granting nothing is the fail-closed answer. The `tenant_host` half is stated more
strongly than the grants support: with `INSERT` table-wide, a request-path caller sets `verified_at`
and `is_primary` on a new row for any tenant, which I executed. Fix the sentence with H-4.

**§11.4's offboarding columns left ungranted with no owning backlog row — right on the security
question, loose on the process one.** Not granting a privilege that has no writer is exactly the rule
this task set itself, and it is the opposite of the "all four on everything" the first review
rejected. The looseness is only L-3: the obligation lives in a doc comment rather than in
`docs/BACKLOG.md`, and doc comments are not a work queue.

---

## Routing

- **Architect — the open question from the first review is now answered, and the answer changed.**
  H-2 left it as "a fourth catalog role, or grants to `aurora_app` with the writer named; either is
  fine". The author chose the second, and H-4 shows it is not sufficient for `catalog.tenant` and
  `catalog.tenant_host`: `INSERT` cannot be usefully column-scoped (provisioning must write exactly
  the routing columns), so as long as the provisioning saga runs as the request-path role, the
  request path can create a routing decision pointing anywhere. The decision to make is narrower and
  concrete: **do the provisioning-saga catalog writes (B-07.1 `ReserveTenant`, B-07.4
  `RegisterRouting`) get their own principal?** If yes, `aurora_app` becomes `SELECT`-only on the
  entire routing decision and H-4 closes structurally. If no, the residual risk must be written into
  ADR-0007 §4.4 rather than into a test comment.
- **Architect (second) — the fail-closed rule needs a sentence about functions.** ADR-0004 rule 2 and
  ADR-0007 §4.4 are written about tables. ADR-0028 §2 mechanism 3 puts a trigger function in
  `catalog`. PostgreSQL's default there is `EXECUTE TO PUBLIC`, which is the opposite of the property
  the rest of the design assumes (H-5). This is adjacent to H-3 and should be decided with it.
- **B-06.2** — §4.3's identity check is now the *only* thing standing between H-4's inserted routing
  row and cross-tenant reads. Two requirements follow: its mis-route test should use a row inserted
  by `aurora_app` rather than one hand-edited by the fixture; and the failure path must mark
  **the expected tenant** `SchemaBlocked`, never the tenant whose database was reached — otherwise
  the mismatch itself becomes a way to suspend another tenant.
- **B-13 / B-12** — `catalog.installed_package.version` is writable, for any tenant, by the
  request-path role (M-4). Treat it as untrusted input to package resolution.
- **B-11** — unchanged from the first review: stage 8 must run this project's `Category=Integration`
  tests with an executed-test floor. It now carries the enforcement of both High fixes.
- **Project manager** — backlog rows for the three obligations in L-3, plus the cluster-bootstrap row
  the first review asked for (M-6) and nobody has created.

## Tier

FULL was correct and remains correct. H-4 changes what the request path may create and H-5 changes
the object class the oracle judges, so the next rework must come back to a security reviewer rather
than to the orchestrator alone.
