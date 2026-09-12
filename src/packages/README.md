# `src/packages/` — Country Packages

Empty by design. Task B-01 scaffolds tier 0, the hosts and the test projects only.

A Country Package is an installable, versioned unit of jurisdiction-specific behaviour that a
tenant enables. It may reference `Aurora.Countries.Contracts` and `Aurora.Documents.Canonical`
**and nothing else** ([`docs/architecture/modules.md`](../../docs/architecture/modules.md) §6),
which is what keeps the core country-agnostic — the core must never contain an
`if (country == "NZ")`.

`Aurora.Countries.NewZealand/` is the first reference package, chosen for the quality of its public
rules rather than its market size; it exists to prove the extension points are real. See
[`ADR-0008`](../../docs/decisions/ADR-0008-country-package-contract.md) for the contract and
`docs/research/` for why New Zealand.
