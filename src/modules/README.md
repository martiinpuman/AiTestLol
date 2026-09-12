# `src/modules/` — business modules

Empty by design. Task B-01 scaffolds tier 0, the hosts and the test projects only; business
modules must not be started before the walking skeleton (B-15) is green.

A module is four projects — `Aurora.Modules.<M>.Contracts`, `.Domain`, `.Application`,
`.Infrastructure` — and `scripts/new-module.sh` (task B-14) generates them together with the
module's `DbContext`, its DI extension, its unit and integration test projects and its
`TenantIsolationContract` subclass. Create a module with that script, not by hand.

What each of the four projects may reference is fixed by
[`docs/architecture/solution-layout.md`](../../docs/architecture/solution-layout.md) §2, and which
modules may reference which by [`docs/architecture/modules.md`](../../docs/architecture/modules.md)
§6. Both are enforced by architecture fitness tests.

The `Modules.` namespace segment is load-bearing: every architecture fitness test is expressed as a
rule over it. Do not flatten it.
