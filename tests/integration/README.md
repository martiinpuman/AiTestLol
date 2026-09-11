# `tests/integration/` — tests against a real PostgreSQL database

Empty by design. Task B-01 scaffolds the test projects that exist without a module or a platform
assembly; the rest are created by the tasks that own them —
`Aurora.Platform.Tenancy.IntegrationTests` with B-06, `Aurora.Countries.NewZealand.Tests` with the
first Country Package, and one `Aurora.Modules.<M>.IntegrationTests` per module from
`scripts/new-module.sh` (task B-14).

Every test here is tagged `[Trait("Category", "Integration")]` so `verify.sh` can run stage 6
without Docker and stage 8 with it. Tests share **one** PostgreSQL container per xUnit collection —
a container costs about nine seconds to start and is the single biggest lever on the gate's
runtime budget.

Every module integration-test assembly must contain a subclass of `TenantIsolationContract<T>`
(`Aurora.TestKit`, task B-10); an architecture fitness test asserts it, because a module whose
isolation is merely assumed is a module whose isolation is untested.

See [`docs/architecture/testing-strategy.md`](../../docs/architecture/testing-strategy.md) and
[`ADR-0007`](../../docs/decisions/ADR-0007-multi-tenancy-database-per-tenant.md) §12.2.
