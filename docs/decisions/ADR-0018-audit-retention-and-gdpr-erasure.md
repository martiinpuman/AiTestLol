# ADR-0018 — Audit logging, retention and GDPR erasure

- **Status:** Accepted (2026-09-11) — mechanics of §1 (schema, append-only enforcement, partitioning, writer signature, hash-chain canonical form) specified by ADR-0028; the decisions here are unchanged
- **Deciders:** architect
- **Related:** ADR-0004 rule 5, ADR-0007 §11, ADR-0008 §7 point 9, ADR-0016 (logs are not audit)

## Context

`CLAUDE.md`: *"Every financial change is audit-logged with who, when, what and tenant."* Two further obligations collide with it. Statutory bookkeeping law requires records to be kept — commonly seven years, jurisdiction-specific and therefore a Country Package concern. Data-protection law requires personal data to be erasable on request. An invoice naming a person is simultaneously a statutory record that must be kept and personal data that must be erasable. Resolving that tension is most of this ADR.

**Audit is not logging.** Logs are operational, sampled, retained 30 days and contain no personal data (ADR-0016). Audit is a business record, complete, retained for years, and legally meaningful. They are different stores with different rules, and a system that uses one for the other fails both.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **EF Core `SaveChangesInterceptor` writing to an append-only `audit` schema, plus explicit `IAuditWriter` for non-entity actions** *(chosen)* | Captures before/after automatically for annotated entities; one implementation; explicit calls cover the actions that have no entity (sign-in, export, package install, filing) | Misses anything written by raw SQL or `ExecuteUpdate` — mitigated by making those paths explicit and reviewed |
| PostgreSQL triggers per table | Catches every writer, including raw SQL | Audit logic in SQL duplicated across every module and every tenant database; no access to the application's notion of actor or correlation; migrations become heavier |
| Temporal/history table per entity | Complete history, queryable as of a date | Doubles the schema; answers "what did it look like?" but not "who changed it and why"; expensive across thousands of tenant databases |
| Event sourcing the whole domain | Perfect audit by construction | A very large architectural bet for an SMB ERP, and accountants need relational queries over current state. ADR-0003 already rejected the document/event store direction |

## Decision

### 1. The audit store

`audit.audit_event` in the **tenant** database:

```
id, sequence (bigserial), occurred_at timestamptz, tenant_id, company_id,
actor_user_id, actor_display, actor_type (User|System|Operator|ApiClient),
action, entity_type, entity_id, correlation_id, source (Ui|Api|Job|Operator),
before jsonb, after jsonb, prev_hash bytea, hash bytea
```

- **Append-only, enforced at two levels**: `UPDATE` and `DELETE` revoked from `aurora_app`, plus a trigger that raises on either (ADR-0004 rule 5).
- **Tamper evidence**: `hash = SHA256(prev_hash || canonical(row))`, chaining per tenant. A daily job writes the chain head to `catalog.operator_audit_event`. This is cheap, needs no external service, and turns "the audit log was edited" from undetectable into detectable. For an ERP that is worth the few lines it costs.
- Written **in the same transaction** as the change it records. An audit entry that can be lost independently of its change is not an audit entry.

### 2. What is audited

- Every create, update and delete of an entity marked `[Auditable]`, with before/after.
- **Every financial change, without exception**: postings, reversals, document state transitions, price and credit-limit changes, period open/close, tax-code changes.
- Actions with no entity: sign-in and sign-out, failed sign-in, permission and role changes, data export, package install/upgrade/deactivate, report filing, operator support access, erasure requests and executions.
- **Not** audited: reads. Read auditing at ERP volume is a cost with little value; the exceptions — exporting data, viewing a payroll-like sensitive report — are audited explicitly as *actions*.

### 3. The ledger is its own audit trail

Posted journal entries are immutable and corrections are reversals (`CLAUDE.md`), so the ledger already records what happened. The audit log therefore records the **decisions around** postings — who approved, who attempted and was refused, what was changed before posting — rather than duplicating journal lines. Duplicating them would double the largest table in the system for no added truth.

### 4. Personal data inside audit payloads

- `before`/`after` reference a data subject by **surrogate id**, never by inlining name, email, address or phone.
- Free-text fields that may contain personal data are stored, because a dispute may depend on them, and are covered by the erasure mechanism in §6.
- `actor_display` is a denormalized name kept for readability and is **pseudonymised on erasure** (ADR-0007 §11.5); `actor_user_id` is a surrogate and survives, so the chain of responsibility stays intact.

### 5. Retention

- Retention rules are **declared by the Country Package** (`IRetentionPolicy`, ADR-0008 §7 point 9) per document type, into `platform.retention_rule`.
- **Default where no package declares one: 7 years** from the end of the fiscal year in which the document was posted. A documented, conservative default is better than an undefined one, and a package overrides it.
- Audit events are retained at least as long as the longest document retention in that tenant.
- **Legal hold** is a tenant-level or document-level flag with a reason and an owner that blocks *both* deletion and erasure while it is set. Without it, a pending dispute and a routine erasure job will eventually collide.
- Retention expiry does not delete automatically. It **permits** deletion; a tenant-visible job proposes and the tenant confirms. Silent destruction of business records is not a feature.

### 6. GDPR erasure

The two operations ADR-0007 §11.5 distinguishes, restated with the mechanics:

**Tenant deletion** — the customer ends the contract. The database is destroyed (ADR-0007 §11.4). Complete by construction, which is database-per-tenant's strongest argument.

**Data-subject erasure** — an individual within a tenant. **Pseudonymisation, not deletion:**

1. Personal identifiers live in designated places only: `party.person` and contact rows, `audit.audit_event.actor_display`, `catalog.identity_user`. Every such property is annotated `[PersonalData]`.
2. Erasure replaces those values with a stable tombstone (`"Erased subject 7f3a…"`), keeping surrogate keys so documents, ledger references and the audit hash chain stay intact and balanced.
3. **Erasure is deferred, not refused, while a statutory retention period covers a document referencing the subject.** The request is recorded with a `due_at`, executed automatically when retention lapses, and the subject is told the date. Refusing outright is legally wrong; deleting outright is also legally wrong. Deferral with a date is the defensible answer.
4. A fitness test asserts every entity with a `[PersonalData]` property implements `IPseudonymisable` or is on a reviewed exception list (rule S5). This is how we avoid discovering a forgotten column during a regulator's deadline.
5. **Backups cannot be edited.** An erasure is recorded in `catalog.erasure_replay_log`, retained longer than the 35-day backup retention, and re-applied automatically after any restore.
6. The erasure itself is audited — subject reference, tenant, requester, timestamp, fields affected — **without recording the erased values**.
7. Erasure publishes `SubjectErased`, which Country Packages and downstream modules consume to pseudonymise their own side tables (ADR-0008 §4.1 R3).

## Consequences

- Positive: one audit mechanism, written transactionally, tamper-evident, with retention supplied by the jurisdiction rather than guessed by us.
- Positive: the statutory-record versus erasure conflict has a written, defensible resolution instead of being discovered under a 30-day legal deadline.
- Positive: `[PersonalData]` gives us a machine-readable inventory of personal data — which is itself a GDPR Article 30 obligation, obtained as a side effect.
- Negative: audit volume. An ERP writing thousands of documents a day generates a large `audit_event` table per tenant. Mitigation: monthly partitioning by `occurred_at`, `jsonb` payloads compressed by PostgreSQL's default TOAST, and a fitness test keeping payloads bounded.
- Negative: the interceptor misses raw-SQL writes. Mitigation: raw SQL that mutates business data requires an explicit audit call and is called out in review; `ExecuteUpdate`/`ExecuteDelete` on `[Auditable]` entities is banned by a fitness rule.
- Negative: the hash chain must be written in sequence per tenant, a small serialization point on a table that is already append-only per tenant. Measured, not assumed; if it bites, the chain becomes per-day rather than per-event.
- Negative: pseudonymisation is irreversible and a mistaken erasure cannot be undone except from a backup. The operation requires an explicit confirmation and the `platform.subject.erase` permission.

## Revisit when

A jurisdiction's Country Package declares retention rules incompatible with the default (expected and handled), a regulator requires read auditing for a specific document class, or audit volume forces the table out of the tenant database into a separate archive store.
