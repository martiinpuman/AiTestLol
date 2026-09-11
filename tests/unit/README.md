# `tests/unit/` — fast tests, no database

Empty by design. Task B-01 scaffolds the test projects that exist without a module; the per-module
unit test projects are created with their modules by `scripts/new-module.sh` (task B-14).

One project per module, named `Aurora.Modules.<M>.UnitTests`. These run in `verify.sh` stage 6,
which **must pass with Docker stopped** — nothing here may touch a container, a database or the
clock. Use `FakeTimeProvider` for anything effective-dated.

See [`docs/architecture/testing-strategy.md`](../../docs/architecture/testing-strategy.md).
