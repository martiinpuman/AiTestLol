# `src/platform/` — tier 1 platform modules

Each platform capability is a `Contracts` project plus an implementation project. B-01 scaffolded
tier 0, the hosts and the test projects only; a platform module appears here when the task that owns
it creates it. The full list, and what each one owns and must not do, is in
[`docs/architecture/modules.md`](../../docs/architecture/modules.md) §4; the folder layout is in
[`docs/architecture/solution-layout.md`](../../docs/architecture/solution-layout.md) §1.

| Module | State | Owner |
|---|---|---|
| [`Aurora.Platform.Tenancy`](Aurora.Platform.Tenancy/README.md) + `.Contracts` | Catalog database and the tenant registry (B-05). Connection resolution, scopes and the tenant `DbContext` factory land with B-06 | B-05 … B-08 |
| Identity, Access, Audit, Messaging, Jobs, Localization, Configuration | Not created yet | their own tasks |

The `Platform.` namespace segment is load-bearing: every architecture fitness test is expressed as
a rule over it. Do not flatten it.
