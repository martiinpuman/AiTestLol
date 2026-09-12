# ADR-0003 — Persistence: Entity Framework Core

- **Status:** Accepted (2026-09-10) — **locked by the product owner**
- **Deciders:** product owner (choice), architect (consequences)

## Context

The system needs: rich aggregate mapping (value objects such as `Money` and `Quantity` owned by entities), transactional consistency for postings, schema migrations that must run across thousands of tenant databases, and enough raw-SQL capability for reporting and set-based maintenance. Verified in this environment: `Microsoft.EntityFrameworkCore` 10.0.12 (MIT) and `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (PostgreSQL licence).

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **EF Core 10** *(chosen)* | First-party, MIT; migrations are a real, versioned artefact — essential for the per-tenant migration orchestration in ADR-0007; owned types map value objects cleanly; change tracking gives us a natural unit of work; compiled models remove per-context model-build cost; `ExecuteUpdate`/`ExecuteDelete`/`SqlQuery<T>` cover set-based and raw SQL without a second library | Change tracking hides cost (N+1, unbounded materialization); LINQ can silently translate badly; the model must be tenant-invariant or the model cache explodes |
| Dapper + hand-written SQL and migrations | Explicit, fast, no translation surprises | No migration story — we would build one; value-object mapping is manual everywhere; the hand-written unit of work becomes our own worse ORM |
| Marten (document store on PostgreSQL) | Excellent Postgres integration, event sourcing available | Document storage is a poor fit for a general ledger that must be queried relationally by accountants and auditors; larger conceptual novelty than an ERP warrants |
| NHibernate | Mature, flexible | Small and shrinking ecosystem; weaker migration tooling; hiring risk over a decade |

## Decision

Use **EF Core 10** with the Npgsql provider as the only ORM. Rules:

1. **One `DbContext` per module**, each owning a dedicated PostgreSQL **schema** inside the tenant database (`sales`, `inventory`, `ledger`, …) with its own migrations-history table in that schema. Modules never map another module's tables.
2. **The EF model must be tenant-invariant.** The mapped model may vary by *schema version* and by *installed package* (packages bring their own `DbContext`), never by tenant. This is what allows one compiled `IModel` to be shared across every tenant on a given version; see `../architecture/scalability.md` §6.
3. **No `AddDbContext` for tenant contexts.** Tenant contexts are constructed only through the tenant-scoped factory in ADR-0007. Only `CatalogDbContext` is registered conventionally.
4. `NoTracking` is the default query behaviour; tracking is opted into per unit of work that intends to write.
5. Raw SQL is allowed and expected for reporting projections and maintenance, through EF Core's own `FromSql`/`SqlQuery<T>`/`ExecuteUpdate` APIs. **We do not add Dapper**; EF Core covers the need and a second data-access story doubles the review surface.
6. Migrations are **expand/contract**, always. See ADR-0007 §7 for the enforcement rule.

## Consequences

- **The architect agrees with this choice.** The migration story alone justifies it: without versioned, ordered, idempotent migration artefacts, per-tenant rollout across thousands of databases would be a bespoke system we would have to build and test ourselves.
- Change tracking makes it easy to write a query that materialises an entire year of general ledger into memory. Mitigations: `NoTracking` default; a fitness test that flags `ToListAsync` on an `IQueryable` without a preceding `Take`; report queries over 10 000 rows are jobs, not requests (`scalability.md` §5).
- Owned types for `Money` mean the currency travels with the amount in the schema (`amount numeric(19,4)` + `currency char(3)`), so a currency-less amount cannot exist in the database. This is a real correctness win and it is cheap.
- Compiled models (`dotnet ef dbcontext optimize`) are required before the first production release; without them, per-context model building at ~50–200 ms each becomes visible when many tenants are cold.
- Npgsql is licensed under the **PostgreSQL licence** — a permissive, BSD-style licence. It satisfies the project rule; it is recorded explicitly in `dependencies.md` because "PostgreSQL" is not one of the three names spelled out in `CLAUDE.md` and a future reviewer will otherwise flag it every time.

## Revisit when

A module's access pattern is genuinely document-shaped (an audit archive, an e-invoice payload store) — such a module may use `jsonb` columns through EF Core rather than a second ORM. Revisit the ORM choice only if EF Core's migration model stops supporting multi-database orchestration.
