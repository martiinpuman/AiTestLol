# `tests/unit/` — fast tests, no database

One project per module or platform module, created with the thing it tests: a module's by
`scripts/new-module.sh` (task B-14), a platform module's by the task that owns it.

| Project | Tests | Landed with |
|---|---|---|
| `Aurora.SharedKernel.UnitTests` | `Money`, `Quantity`, `Percentage`, `DateRange`, typed ids, `Result`; `CompanyScope` and `ICompanyScoped` | B-03; B-03.1 |
| `Aurora.Platform.Tenancy.UnitTests` | The registry's value objects and invariants, the catalog EF model, the ADR-0007 §9.3 guard over that model | B-05 |

Modules' projects are named `Aurora.Modules.<M>.UnitTests`. These run in `verify.sh` stage 6,
which **must pass with Docker stopped** — nothing here may touch a container, a database or the
clock. Use `FakeTimeProvider` for anything effective-dated. Building an EF model needs a provider's
type mappings but no connection, which is why the catalog model is checked here and not only in the
integration suite.

Stage 6 fails below a floor of executed tests (`solution-layout.md` §5.3). Re-round that floor in
`scripts/verify.sh` in the same commit that adds or removes a project from this folder.

See [`docs/architecture/testing-strategy.md`](../../docs/architecture/testing-strategy.md).
