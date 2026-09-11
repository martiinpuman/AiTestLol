# `src/platform/` — tier 1 platform modules

Empty by design. Task B-01 scaffolds tier 0, the hosts and the test projects only; the platform
modules are created by the tasks that own them.

Each platform capability is a `Contracts` project plus an implementation project, for example
`Aurora.Platform.Tenancy.Contracts/` and `Aurora.Platform.Tenancy/`. The full list, and what each
one owns and must not do, is in [`docs/architecture/modules.md`](../../docs/architecture/modules.md)
§4; the folder layout is in
[`docs/architecture/solution-layout.md`](../../docs/architecture/solution-layout.md) §1.

The `Platform.` namespace segment is load-bearing: every architecture fitness test is expressed as
a rule over it. Do not flatten it.
