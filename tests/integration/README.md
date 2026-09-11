# `tests/integration/` — tests against a real PostgreSQL database

Each project is created by the task that owns the thing it tests.

| Project | Tests | Landed with |
|---|---|---|
| `Aurora.Platform.Tenancy.IntegrationTests` | The catalog database: migrated schema, constraints, round trips and `aurora_app`'s privileges | B-05 |

Still to come: `Aurora.Countries.NewZealand.Tests` with the first Country Package, and one
`Aurora.Modules.<M>.IntegrationTests` per module from `scripts/new-module.sh` (task B-14).

Every test here is tagged `[Trait("Category", "Integration")]` so `verify.sh` can run stage 6
without Docker and stage 8 with it. Tests share **one** PostgreSQL container per xUnit collection —
a container costs about nine seconds to start and is the single biggest lever on the gate's
runtime budget.

Every module integration-test assembly must contain a subclass of `TenantIsolationContract<T>`
(`Aurora.TestKit`, task B-10); an architecture fitness test asserts it, because a module whose
isolation is merely assumed is a module whose isolation is untested.

The tenancy project is the one exception, and it is not a loophole: the catalog is the single
database shared by every tenant, so there is no second tenant database to keep it out of. What it
asserts instead is the blast radius of the role the request path holds — see that module's
[README](../../src/platform/Aurora.Platform.Tenancy/README.md). Once B-07 provisions tenant
databases, the contract applies to them in the ordinary way.

See [`docs/architecture/testing-strategy.md`](../../docs/architecture/testing-strategy.md) and
[`ADR-0007`](../../docs/decisions/ADR-0007-multi-tenancy-database-per-tenant.md) §12.2.
