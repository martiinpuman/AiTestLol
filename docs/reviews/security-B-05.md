# Security review — TASK B-05 (re-review of the M-1 / M-2 rework)

**Reviewer:** security-reviewer (first security pass on this repository) · **Tier:** FULL (unchanged)
**Branch:** `task/B-05` @ `372451e`, rebased · **Reference standard:** OWASP ASVS L2
**Scope:** the trust boundary between a tenant and the platform (the catalog is the only shared
database), and between the system and its operators. Correctness, tests and style are
`docs/reviews/B-05.md`'s pass and are not re-reviewed here.

## Verdict: CHANGES_REQUESTED

Two high findings, both in this task's own diff, both executed rather than reasoned about. One
further high is an **architect finding in ADR-0028** and is explicitly *not* a B-05 merge blocker.

The M-1 and M-2 rework is honest work and most of it holds up under attack. `ALTER DEFAULT
PRIVILEGES` is genuinely gone, the fail-closed default is real (I confirmed a new table is a `42501`
for every verb), and the M-2 prose is now true where it used to be false. What does not hold up is
the *replacement* control: the oracle it is built on can be bypassed by a one-line grant while
reporting green, and the per-table "decision" it records is "all four privileges on everything",
which is the blanket grant M-1 removed, re-entered by hand.

## Gate, as I observed it

```
./scripts/verify.sh                              → RESULT: PASS  (exit 0, 29.4s)
   stage 6 Unit tests  PASS  5.9s  365 test(s) executed
   stages 4 5 7 8 9 10 PENDING (owned by B-11)  ← stage 8 is Integration tests
dotnet test Aurora.sln -c Release --filter 'Category=Integration'
                                                 → Passed! 44, Failed: 0
git status --porcelain                           → empty (before and after my probes)
```

---

## What I attacked and what happened

Every probe below was run against PostgreSQL 17.11 — the pinned `postgres:17-alpine` — either
through the project's own fixture or against the **real `InitialCatalog` migration** applied with
`dotnet ef migrations script` to a scratch cluster built with the same three roles. All probes were
reverted; the tree is clean.

| # | What I tried | Result |
|---|---|---|
| 1 | Defeat the append-only guarantee with a **column-level** `GRANT UPDATE (detail, redacted)` on `catalog.operator_audit_event`, table-level still `SELECT, INSERT` | **Worked.** `has_table_privilege(...,'UPDATE')` returns **false**; the `UPDATE` succeeds and rewrites the operator audit row. Ran the real integration suite with this exact shape committed and the allowlist recorded correctly: **`The_app_role_holds_exactly_the_table_privileges_the_allowlist_records…` passed.** → **H-1** |
| 2 | Grant a privilege the "every privilege PostgreSQL can grant on a table" list omits | **Worked.** PG 17 added `MAINTAIN`. `GRANT MAINTAIN ON catalog.tenant TO aurora_app` gives `relacl = aurora_app=arwd**m**/aurora_migrator`; the seven-privilege cross-join reports exactly `{DELETE,INSERT,SELECT,UPDATE}` — zero differences. → **H-1** |
| 3 | Take a tenant over from inside the request-path role, using only the grants the migration issues | **Worked.** As `aurora_app`: deleted `globex.aurora.app` from `catalog.tenant_host` and re-bound it to Acme's tenant id; set Acme's `database_name` to `aurora_t_globex`; repointed `catalog.database_cluster.host` to `attacker.example.net`. No DDL, no superuser. → **H-2** |
| 4 | Land a new catalog table with privileges nobody decided, past the gate | **Worked.** Added `catalog.operator_audit_event` by raw SQL with the blanket grant copy-pasted from the line above. `./scripts/verify.sh` → **RESULT: PASS, 365 test(s) executed** — the same count as the clean tree. → **M-1** |
| 5 | Same change, run against the integration suite | **Caught, loudly and correctly**: `catalog.operator_audit_event exists but CatalogSchemaAllowlist.AppRolePrivileges records no decision for it; aurora_app holds [DELETE, INSERT, SELECT, UPDATE]`, plus the §9.3 guard. The mechanism works. It just is not in the gate. |
| 6 | Make the cluster wrong instead of the code: `ALTER ROLE aurora_app SUPERUSER CREATEDB CREATEROLE BYPASSRLS`, migration unchanged | **Worked and went unnoticed.** The migration applies happily; `aurora_app` then deletes `__EFMigrationsHistory`, drops `catalog.tenant_host` and reads `pg_authid`. Nothing in `src/` or `scripts/` asserts one thing about the role it grants to. → **M-2** |
| 7 | ADR-0028 §2 mechanism 2, exactly as the ADR writes it | **It is a no-op.** `ALTER DEFAULT PRIVILEGES … IN SCHEMA audit REVOKE UPDATE, DELETE ON TABLES FROM aurora_app` leaves **zero rows in `pg_default_acl`**, and a later ordinary `GRANT … UPDATE, DELETE` on a new `audit` table is completely unrestrained. → **H-3 (architect)** |
| 8 | Reach a database other than the catalog as `aurora_app`, on the fixture's cluster | **Refused, every one.** The `pg_database` enumeration is real; `template1` and `postgres` are both in the list and both `42501`. M-2's test now proves its name. |
| 9 | Obtain a table privilege via `PUBLIC` or role membership and hide it from the oracle | **No.** `has_table_privilege` reports the effective privilege; a `GRANT … TO PUBLIC` shows up as `aurora_app` holding it and becomes a difference. The reviewer's choice of oracle was right about *that* axis. |
| 10 | Find a secret in the repository, a credential in a log, or a connection string in an exception path | **Nothing.** Role passwords are minted per run and never written down; `AddCatalogDatabase` reads no configuration and logs nothing; no `EnableSensitiveDataLogging`/`EnableDetailedErrors` anywhere; `_output.WriteLine` emits counts and database names only; `database_cluster` stores `*_secret_ref` values with a `CHECK` that they are references. Clean. |
| 11 | Find a window where a table exists with privileges nobody intended (mid-migration, failed partial, `Down()`) | **No window during `Up()`.** EF Core wraps a migration in one transaction on Npgsql, so `CREATE TABLE` and its `GRANT` commit together. See L-1 for the day that stops being true. |
| 12 | Find the stage-2 per-tenant role (`aurora_app_<tenantKey>`) unowned | **It is owned.** `FOLLOWUP-001`, gated "before the first paying customer" in `docs/product/roadmap.md`. The stage-1 residual risk is recorded honestly in ADR-0007 §3.5 and is an accepted risk, not a finding. |

---

## High

### H-1 — the privilege oracle that replaced `ALTER DEFAULT PRIVILEGES` is bypassable by a one-line grant, and reports green while it is bypassed

`tests/integration/Aurora.Platform.Tenancy.IntegrationTests/CatalogPrivilegeTests.cs:55-63`
(`PrivilegesHeldSql`) · `tests/unit/Aurora.Platform.Tenancy.UnitTests/CatalogSchemaGuard.cs:267-273`
(`PostgresTablePrivileges`)

The whole M-1 fix rests on one comparison: cross every catalog relation with a hand-written list of
seven privileges, ask `has_table_privilege`, diff against `AppRolePrivileges`. Both halves of that
sentence are holes.

**(a) `has_table_privilege` cannot see a column-level ACL.** The append-only table ADR-0004 rule 5
exists for is `catalog.operator_audit_event`. I created it the way the allowlist's own doc comment
says it should be created — `["operator_audit_event"] = Set("SELECT", "INSERT")`, granted exactly
that — and added one more line, the sort of line an author writes *trying* to be careful:

```sql
GRANT SELECT, INSERT ON catalog.operator_audit_event TO aurora_app;
GRANT UPDATE (detail, redacted) ON catalog.operator_audit_event TO aurora_app;   -- invisible
```

`has_table_privilege('aurora_app', 'catalog.operator_audit_event', 'UPDATE')` → **`f`**. Zero
differences. `The_app_role_holds_exactly_the_table_privileges_the_allowlist_records_and_nothing_on_any_other_catalog_table`
**passes** (I ran the real suite: 43 passed, and the only failure was my probe colliding with L-2).
Then, as `aurora_app`:

```
UPDATE catalog.operator_audit_event SET detail='nothing to see here', redacted=true;   → UPDATE 1
DELETE FROM catalog.operator_audit_event;                                              → 42501
```

The operator audit trail — "every operator action, incl. support access" — is rewritable by the
request-path role, and the test whose entire job is to prevent that says the grants match the record.
This is M-1's outcome reproduced through the control installed to prevent M-1. (ASVS V1.2.2 / V4.1.3:
least privilege must be *verified*, not asserted.)

**(b) the list is already incomplete on the pinned version.** `PostgresTablePrivileges` is commented
"Every privilege PostgreSQL can grant on a table". PostgreSQL 17 — the version `CLAUDE.md` pins —
added `MAINTAIN`. After `GRANT MAINTAIN ON catalog.tenant TO aurora_app` the ACL reads
`aurora_app=arwd**m**/aurora_migrator` and the oracle reports `{DELETE,INSERT,SELECT,UPDATE}`:
a perfect match. That comment is `CLAUDE.md` self-check #1 — a claim with no mechanism behind it —
and the mechanism it stands in for is a security control.

**Fix (one change closes both, and closes the ones PG 18 will add).** Stop enumerating privileges and
compare the **ACL itself**. Read `pg_class.relacl` and `pg_attribute.attacl` for every relation in
`catalog`, project the entries for `aurora_app` *and for `PUBLIC`*, and diff that against the record.
Any ACL entry not in the record is a difference, whatever kind it is and whatever PostgreSQL adds
next. `aclexplode(relacl)` gives you the rows directly; add `attacl` and record column grants as
`UPDATE(detail)` so the allowlist can express the decision if one is ever genuinely wanted. Then
extend `The_privilege_test_fails_the_moment_a_grant_drifts_either_way` with a fourth shape — a
column-level grant — and watch it go red before you fix it. Until the ACL is the oracle, the doc
comment must not say "every privilege"; it says which seven, and that the eighth defeats it.

### H-2 — the recorded "decision" is all four privileges on all five tables, so the request-path role owns the routing table; I took a tenant over with it

`src/platform/Aurora.Platform.Tenancy/Migrations/20260911172124_InitialCatalog.cs:229-234` ·
`tests/unit/Aurora.Platform.Tenancy.UnitTests/CatalogSchemaGuard.cs:285-294` ·
`src/platform/Aurora.Platform.Tenancy/README.md:112-114`

M-1's fix built a mechanism for recording a per-table privilege decision and then recorded, for every
table, the same four privileges the deleted default granted. The *shape* changed; the *decision* was
not made. `aurora_app` is one role — `AddCatalogDatabase` takes a single connection string, so every
catalog write in every process is this role — and these are the tables that decide which database a
request reaches. Against the real migration, as `aurora_app`, with no DDL and no superuser:

```
DELETE FROM catalog.tenant_host WHERE host='globex.aurora.app';                         DELETE 1
INSERT INTO catalog.tenant_host VALUES ('globex.aurora.app', <acme-tenant-id>, false, now());
UPDATE catalog.tenant SET database_name='aurora_t_globex' WHERE key='acme';             UPDATE 1
UPDATE catalog.database_cluster SET host='attacker.example.net' WHERE id='eu-1';        UPDATE 1
```

Three separate crossings of the tenant→platform boundary:

1. **Tenant takeover by hostname.** ADR-0007 §3.2 resolves the tenant from the host. `DELETE` then
   `INSERT` on `catalog.tenant_host` rebinds another tenant's hostname to mine. The PK on `host`
   stops a straight overwrite; it does not survive delete-then-insert.
2. **Cross-tenant routing.** Repointing `catalog.tenant.database_name` sends Acme's requests at
   Globex's database. The only thing between that and cross-tenant reads is §4.3's identity check —
   which is **B-06.2 and does not exist yet**. Today there is nothing.
3. **Credential exfiltration for the whole cluster.** B-06.1's resolver builds a connection string
   from `database_cluster.host`/`port` and the password resolved from `*_secret_ref`. Repointing
   `host` makes the platform dial an attacker's PostgreSQL **carrying the real cluster credentials**
   for every tenant on that cluster.

What makes this a finding rather than a hypothetical: ADR-0004 rule 2's stated purpose is that "a bug
cannot drop a tenant's table" is "a property of the deployment rather than of our care." For the
routing tables it is entirely a property of our care — the entities happen to expose no delete path
(`Tenant` has `Reserve`/`Activate`/`RecordActivity`, `TenantHost` has only `Register`, there is no
`Remove` call in the context), which is application-level, exactly the thing the rule says not to rely
on. And the module README states the containment claim in terms the attack walks around:

> **Downwards** — `aurora_app` has no DDL … so it cannot alter the schema that decides where every
> tenant's data lives.

It cannot alter the *schema*. It can rewrite the *rows*, and the routing decision lives in rows.

Not critical because no endpoint reaches these writes yet; high because the grant ships now, the
allowlist blesses it as decided, and every module that later writes the catalog inherits it.

**Fix.** Make the decision the mechanism was built to record, with the writer named per grant:

- `catalog.database_cluster` → **`SELECT` only.** Nothing in ADR-0007 has the application registering
  or editing a cluster; it is operator/seed data. This single line removes the credential-redirection
  path.
- **Drop `DELETE` from all five.** §11.4 tombstones a tenant (`state='Deleted'` + `deleted_at`) and
  `DROP DATABASE` is `aurora_admin`; subscriptions close with `valid_to`. No code deletes anything
  today, and a forgotten grant is the loud `42501` this task already relies on. If offboarding later
  needs `DELETE` on `catalog.tenant_host`, that is the migration that grants it, with the writer named.
- Add a `Writer` note beside each entry in `AppRolePrivileges` — the task, saga step or module that
  needs it. A decision that cannot name its writer has not been made, and the reviewer of the next
  catalog table has nothing to compare against.
- Correct the README's "Downwards" bullet to say what the role *can* write, not only what it cannot
  create. B-06's and B-07's implementers read that paragraph.

If the team's answer is that the platform genuinely needs all four — because the saga, billing and the
package installer all run as `aurora_app` — then the finding is that one role serves both the request
path and operator-grade fleet writes, and the right fix is a fourth catalog role. Either answer is
fine. Recording "all four" without choosing is not.

### H-3 — ADR-0028 §2's second append-only mechanism does nothing at all, and its closing sentence claims a retrofit that does not exist (architect-owned, **not a B-05 blocker**)

`docs/decisions/ADR-0028-tenant-audit-store-and-write-path.md:38,41,76` — introduced on the
integration branch by `4fc526f`, not by this task. Raised here because ADR-0028 cites B-05 M-1 as its
evidence and a future reader will take it as settled.

ADR-0028 chose its option over the alternative specifically because of mechanism 2:

> `ALTER DEFAULT PRIVILEGES FOR ROLE aurora_migrator IN SCHEMA audit REVOKE UPDATE, DELETE ON TABLES
> FROM aurora_app` — so a table added to `audit` later inherits the policy

Executed on PG 17.11:

```
ALTER DEFAULT PRIVILEGES FOR ROLE aurora_migrator IN SCHEMA audit
  REVOKE UPDATE, DELETE ON TABLES FROM aurora_app;
SELECT * FROM pg_default_acl;                          → (0 rows)

SET ROLE aurora_migrator; CREATE TABLE audit.audit_event_next_year (...);
GRANT SELECT, INSERT, UPDATE, DELETE ON audit.audit_event_next_year TO aurora_app;
→ SELECT t · INSERT t · UPDATE t · DELETE t
```

PostgreSQL's built-in default grants `aurora_app` nothing on a new table, so there is nothing to
revoke and **no default-ACL row is created**. The statement succeeds and changes nothing, forever.
Worse, it cannot restrain a later `GRANT`.

That makes all three of the "three ways" point-in-time or per-table: #1 `REVOKE … ON ALL TABLES`
applies only to tables that exist when it runs, #2 is a no-op, #3 is a trigger somebody must create on
each new table. So the Consequences claim — "the next append-only table in a tenant database is
protected by where it is created, not by whether its author remembered ADR-0004 rule 5" — is false,
and it is the reason the `audit` schema was chosen over `platform`. **This is the B-05 M-1 failure
mode, pre-installed in the ADR that was written to avoid it.**

Second, §2's last sentence: "The same three-way enforcement is retrofitted to
`catalog.operator_audit_event` and `catalog.erasure_replay_log` (B-05 M-1's fix)." Neither table
exists — I checked the migrated schema — and B-05 did not do this. What B-05 did was delete the
default and record per-table decisions. A reader arriving in six months will believe two audit tables
are protected by a trigger and two revokes when they are protected by nothing, because they are not
there.

**Fix (architect).** (1) Replace mechanism 2 with something that fires: the only construct that makes
a *future* table append-only by where it is created is an **event trigger** on `ddl_command_end` that
revokes `UPDATE`/`DELETE` and attaches the guard trigger to any new table in `audit`; if that is too
much machinery, say plainly that each new `audit` table must do it in its own migration and give the
schema-level statements as belt-and-braces for tables that exist. (2) Delete or rewrite the retrofit
sentence to "when `catalog.operator_audit_event` and `catalog.erasure_replay_log` are created, their
migration records `SELECT, INSERT` in `CatalogSchemaAllowlist.AppRolePrivileges` and adds the guard
trigger — B-05 removed the default privileges that would otherwise have granted them `DELETE`."
(3) Whichever task implements ADR-0028 §2 must prove mechanism 2 by **creating a table after it runs**
and asserting the privilege is absent — otherwise it ships this no-op and the test passes.

---

## Medium

### M-1 — the control that replaced `ALTER DEFAULT PRIVILEGES` does not run in the gate; the half that does cannot detect an over-grant

`scripts/verify.sh:102-115` (stage 8 has no handler) ·
`tests/unit/Aurora.Platform.Tenancy.UnitTests/Catalog/CatalogPrivilegeAllowlistTests.cs`

`CLAUDE.md` calls `scripts/verify.sh` "the single quality gate". Stage 8 is `PENDING (owned by B-11)`,
so the privilege comparison against a real database never runs in it. The stage-6 half compares
`AppRolePrivileges.Keys` with `Columns.Keys ∪ InfrastructureColumns.Keys` — two dictionaries in the
same file, edited in the same commit. It catches a forgotten *record*. It cannot catch a wrong
*grant*, a table created by raw SQL (which an append-only table with a trigger must be), or a
`GRANT … ON ALL TABLES IN SCHEMA catalog` in a future migration, because none of those touch the EF
model.

Demonstrated: added `catalog.operator_audit_event` by raw SQL with
`GRANT SELECT, INSERT, UPDATE, DELETE`, ran the gate →

```
6   Unit tests   PASS   3.4s   365 test(s) executed
RESULT: PASS
```

365, the same count as the clean tree. M-1's exact defect, on M-1's exact table, through the project's
only gate. The integration suite catches it perfectly (probe 5 above) — it is simply not wired to
anything that must pass.

**Fix.** Two options, both cheap, and the first is worth doing even after B-11 lands stage 8:

1. **Assert the grants at stage 6, without Docker.** The migration is a C# file on disk. A unit test
   that parses `GRANT`/`REVOKE`/`ALTER DEFAULT PRIVILEGES` statements out of
   `Migrations/*.cs` and reconciles them with `AppRolePrivileges` runs in 5 ms, fails on an
   over-grant, and fails on the reappearance of `ALTER DEFAULT PRIVILEGES` by name. Make it report
   the number of statements it parsed (`CLAUDE.md` self-check #2), so it cannot pass having found none.
2. Flag for **B-11**: stage 8 must include this project's `Category=Integration` tests and, like
   stage 6, carry an executed-test floor.

Until one of those exists, note in `src/platform/Aurora.Platform.Tenancy/README.md` that the privilege
record is enforced by a test the gate does not run — today it reads as though `CatalogPrivilegeTests`
is a gate.

### M-2 — every privilege claim in this task is a property of the fixture's cluster; nothing makes a real cluster match, and the migration checks nothing about the role it grants to

`tests/integration/Aurora.Platform.Tenancy.IntegrationTests/CatalogDatabaseFixture.cs:73-94` ·
`src/platform/Aurora.Platform.Tenancy/Migrations/20260911172124_InitialCatalog.cs:216-234`

The rework fixed M-2 by having the fixture do what §8 step 3 says a provisioner must — `REVOKE ALL ON
DATABASE … FROM PUBLIC` over every row of `pg_database`. That is the right fixture. But there is **no
artifact anywhere in the repository that creates the three roles or hardens a real cluster** —
`scripts/` holds only dev/verify tooling, no infra, and no backlog row owns cluster bootstrap
(`FOLLOWUP-001` is stage 2, a different thing). So the suite is green partly because the fixture
fixed the environment, which is B-05's original fixture bug one level up, exactly as the brief
suspected.

Made concrete: with the migration byte-for-byte as shipped, I set
`ALTER ROLE aurora_app SUPERUSER CREATEDB CREATEROLE BYPASSRLS` and applied it. It succeeded without
a murmur, after which `aurora_app` deleted `catalog."__EFMigrationsHistory"`, dropped
`catalog.tenant_host`, and read `pg_authid`. Every sentence in both READMEs about least privilege was
false on that cluster and nothing in the product could tell.

**Fix — put the assertion where it runs on every real cluster.** The migration is the one artifact
that touches all of them, and it already fails loudly when a role is missing. Give it a `DO $$` block
before the grants that raises on anything it is about to trust:

- `aurora_app` exists and is **not** `rolsuper`, `rolcreatedb`, `rolcreaterole`, `rolbypassrls`,
  `rolreplication`;
- `aurora_migrator` owns `current_database()`;
- `NOT has_database_privilege('public', current_database(), 'CONNECT')` — I verified this oracle
  distinguishes the hardened catalog (`f`) from an unhardened database and `template1` (both `t`), so
  the migration can refuse to install a catalog into a cluster where §8 step 3 was skipped.

Add a backlog row for **cluster bootstrap** (roles, attributes, `REVOKE ALL … FROM PUBLIC` on
`postgres` and `template1`) so the fixture stops being the only place that knows what a cluster must
look like, and so B-07.1's `HardenDatabase` has a cluster-level counterpart to point at.

---

## Low

**L-1 — the no-window property is implicit and the first `CREATE INDEX CONCURRENTLY` removes it.**
Grants are safe today only because EF wraps the migration in one transaction. B-09's rule ("`CREATE
INDEX` without `CONCURRENTLY` rejected") will force `suppressTransaction: true` on some future
catalog migration, and that migration can commit a table before its grant. State the invariant in the
migration comment — *a `GRANT` is issued in the same transaction as the `CREATE TABLE` it belongs to*
— and hand it to B-09 as a rule it can check.

**L-2 — the drift test mints its synthetic table as `catalog.operator_audit_event`**
(`CatalogPrivilegeTests.cs:106`), a real ADR-0007 §9.2 table. The day it is created for real this test
fails with `42P07` instead of a privilege message — I hit exactly that during probe 5. Use
`Unique.Identifier("drift")`, as `A_table_created_without_a_grant_of_its_own_is_closed_to_the_app_role`
already does.

**L-3 — `Down()` leaves `aurora_app=U/aurora_migrator` on the `catalog` schema** (verified in
`pg_namespace.nspacl`). Harmless today and the author's judgement is right: a later table without its
own grant is still closed. But `Up()` and `Down()` should be symmetric about privileges as they are
about tables, or the asymmetry should be one line of comment rather than a review record.

**L-4 — the §4.3 attribution differs between the two places a reader will look.** The README says the
identity check is "which B-06 builds" (correct — `B-06.2`); the class remarks say "which B-07 must
test as *the* cross-tenant control". B-06.2's own backlog row already owes a deliberate mis-route
test and it lands first. Name B-06.2 in both.

**L-5 — the stage-1 caveat should say what it protects against.** §4.3's identity check catches a
*mis-routed* request: a bug, a bad restore, a hand-renamed database. It does not catch an attacker
holding `aurora_app`'s credentials, who opens their own connection and never runs the initializer —
and under stage 1 that one credential reaches every tenant database on the cluster, whose names are
readable from `pg_database` from inside the catalog. ADR-0007 §3.5 is honest about this ("stage 2
makes cross-tenant access impossible at the *database* level, not merely the application level") and
`FOLLOWUP-001` owns it, so this is an accepted risk, not a finding — but the module README is what
B-06/B-07 read, and it should carry the distinction in one sentence.

**L-6 — `MintPassword() => Guid.NewGuid().ToString("N")` is fine for a throwaway fixture and is the
nearest example B-07/FOLLOWUP-001 will copy when they mint real per-tenant role passwords.** Say so
in one line, or use `RandomNumberGenerator`.

---

## What I checked and found nothing wrong with

- **The fail-closed default is real.** A table created with no grant is `42501` for `SELECT`,
  `INSERT`, `UPDATE` and `DELETE`, tried as the role from its own connection. This is the single best
  thing in the rework and it is properly proven.
- **`PUBLIC` and role membership cannot hide a privilege from the oracle** — `has_table_privilege`
  reports the effective privilege, which `information_schema.role_table_grants` would not. The
  reviewer's oracle choice was right on that axis; H-1 is about the other two axes.
- **M-2's corrected statement is true.** `aurora_app` reaches the catalog and nothing else on the
  fixture's cluster, proved over `pg_database` with `template1` and the maintenance database
  explicitly required to be in the list, and the qualifier "under §3.5 stage 2" is now quoted
  wherever the claim appears.
- **No DDL for `aurora_app`**, including `CREATE EXTENSION` — which closes `dblink`/`postgres_fdw` as
  a cross-database route from inside the catalog.
- **No secrets in the repository, no credential or personal data in any log or test output**, no
  sensitive-data logging enabled, secret *references* in `database_cluster` rather than secrets.
- **No mid-`Up()` privilege window** (EF's per-migration transaction).
- **Stage-2 per-tenant roles are owned** (`FOLLOWUP-001`, gated before the first paying customer).

## Routing

- **Architect:** H-3 (ADR-0028 §2 — the default-privileges `REVOKE` is a no-op; the retrofit sentence
  describes protection that does not exist). Also H-2's open question: does the catalog need a fourth
  role separating request-path writes from operator-grade fleet writes, or is `aurora_app` with
  `SELECT`-only on `database_cluster` and no `DELETE` anywhere sufficient?
- **B-11:** stage 8 must run this project's integration tests with an executed-test floor (M-1).
- **B-06.2 / B-07.1:** the module README is now their source for what the app role bounds; L-4 and
  L-5 correct it before they read it. B-07.1's `HardenDatabase` should be paired with the
  cluster-bootstrap row M-2 asks for.
- **Backlog:** a cluster-bootstrap task (roles, role attributes, `REVOKE ALL … FROM PUBLIC` on the
  maintenance database and `template1`).

## Tier

FULL was correct and remains correct. The rework must come back to a security reviewer, not only to
the orchestrator: H-1's fix changes the oracle every future catalog table is judged by, and H-2's fix
changes what the request path may do to the routing table.
