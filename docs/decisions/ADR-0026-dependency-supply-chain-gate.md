# ADR-0026 — The dependency supply-chain gate: two tiers and a generated closure allowlist

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0002 (locked restore makes the gate enforceable), ADR-0020 (test tooling, standing rule 1), `../architecture/dependencies.md` §1 and §7, `../architecture/solution-layout.md` §5.2 stage 4, `../reviews/B-01.md` finding **S-1**
- **Supersedes:** nothing. This ADR records, and corrects, a rule that until now lived only as one sentence in `dependencies.md` §1.

## Context

`CLAUDE.md` sets a hard limit: a new third-party dependency needs a permissive licence (MIT, Apache-2.0, BSD) and an entry in `docs/architecture/dependencies.md`. `verify.sh` stage 4 is the mechanism that turns that from a request into a rule. It exists to catch three things, in this order of importance:

1. **A licence turning commercial under a package we already ship.** Not hypothetical: AutoMapper, MediatR, MassTransit and FluentAssertions all moved to commercial terms in 2025–2026, and `dependencies.md` §5 already rejects nine packages, six of them on licence grounds. For a product with a ten-year horizon, this is the expensive failure.
2. **An unapproved package entering the graph** — including a package we already rejected arriving through a back door.
3. **The graph silently growing**, which is how typosquats, abandoned packages and future relicensing arrive.

The rule as originally written said stage 4 *"lists all packages **including transitive** and fails if any is absent from this file"*. The B-01 peer review measured the actual graph against it. On B-01's nine `packages.lock.json` files:

| Measured 2026-09-11 on B-01 (`16dd60f`) | Count |
|---|---|
| Distinct packages in the graph | **66** |
| Distinct package ids referenced directly by a project (13 `Direct` entries) | **7** |
| Packages present only transitively | **59** |
| Of those, absent from `dependencies.md` | **57** |

Every one of the 57 arrives legitimately through an approved direct package — Testcontainers, bUnit, Shouldly, the test SDK — and all are permissively licensed. They are simply not *listed*, which is exactly what the rule tests. So the gate would be **red on its first run against a correct codebase**, and the only way to make it green would be for the architect to hand-write 57 rows describing decisions nobody made.

That matters more than the inconvenience. A gate that fails for a reason nobody believes in gets weakened, filtered or skipped — and then it is no longer there for case 1, which is the one that costs money. The gate must be red only when something is genuinely wrong.

Stage 4 is implemented in task **B-11**, which has not started. Correcting the specification now costs a document edit; correcting it after the script exists costs a rewrite plus the habit of ignoring the gate.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **A. Keep the rule as written** — one hand-maintained list covering the full closure | One list, nothing implicit; every package in the product has a human-written row | 59 of 66 rows describe no decision. Every patch bump of Testcontainers or bUnit churns the list. Red on day one. The file is architect-owned, so the architect becomes a bottleneck on every dependency bump. Worst of all it spends scarce human review attention on rows that carry no information |
| **B. Narrow the name check to direct references; licence-check the whole closure** (the reviewer's second suggestion) | Minimal change, no new file, puts the name check exactly where the decisions are; never falsely red | **A new transitive package enters silently.** It would be licence-checked on arrival, but nothing makes a human notice that the graph grew. Loses requirement 3 entirely, and requirement 3 is how requirement 1 usually starts — a package we never evaluated becomes load-bearing, and relicenses two years later |
| **C. Two tiers: curated direct list in `dependencies.md`, plus an exact, generated, licence-verified closure allowlist in `dependency-closure.md`** *(chosen)* | Keeps human attention on the ~40 packages we actually chose, while the closure file still makes **every** addition, removal or licence change of a transitive package show up as a reviewable diff in the pull request that caused it. Generation is one offline command, so the gate is never falsely red for longer than it takes to run it. Licences are read from the resolved package, not from what the document remembers, so a relicensing is caught even when the version pin does not move | A second file to keep in sync, and a diff on every dependency bump. A machine-generated file living in an architect-owned folder needs a written ownership carve-out (see Consequences) |
| **D. Delegate to an SBOM / licence scanner** (CycloneDX .NET, Apache-2.0; `nuget-license`, MIT) | Standard formats, SPDX output, maintained by others | Puts a third-party tool *inside* the gate — the gate must be the most boring, least-dependent thing in the repository, and it would then need its own licence and maintenance watch. Several such tools want network access, which `solution-layout.md` §5.1 forbids beyond restore. And none of them removes the need for a reviewed allowlist: they produce a report, not a decision record |
| **E. Licence check only; no name lists at all** | Simplest; never falsely red | An unapproved but permissively licensed package sails in, so requirement 2 is lost. The §5 rejection list stops being enforced — and four of those nine were rejected for reasons other than licence text (maintainer behaviour, abandonment), which no licence scanner can see |

## Decision

Adopt **option C**. `verify.sh` stage 4 (`scripts/check-dependencies.sh`) governs the package graph in two tiers:

- **Direct tier** — any package with a `PackageReference` in a project (`"type": "Direct"` in `packages.lock.json`). Governed by the curated tables in `dependencies.md` §2 and §3: a deliberate choice, with a purpose, an owning ADR and a verification date.
- **Transitive tier** — everything else (`"type": "Transitive"` or `"CentralTransitive"`). Governed by `docs/architecture/dependency-closure.md`: a **generated, exact** allowlist of `(package id, resolved version, SPDX licence, arrives via)`.

The **licence** check applies to both tiers and is made against the **resolved package's own `.nuspec`**, never against the version string recorded in a document.

Stage 4 fails when any of the following is true:

1. A **direct** package is absent from `dependencies.md` §2/§3.
2. A **transitive** package is absent from `dependency-closure.md`.
3. `dependency-closure.md` contains a row for a package that is **no longer in the graph** (the allowlist is exact, not a superset — otherwise it silently pre-approves).
4. A package's **resolved licence differs from the recorded licence**, in either file.
5. A resolved licence is **not on the accepted SPDX list** in `dependencies.md` §1.
6. A package id on the **rejection list** in `dependencies.md` §5 appears anywhere in the graph, at any version, direct or transitive.

Additionally, `dependency-closure.md` being absent or unparseable is a stage 4 failure, and the gate **never writes to either file** — regeneration is a separate, explicit command (`scripts/check-dependencies.sh --update-closure`), because a gate must not silently fix what it is measuring (`solution-layout.md` §5.1).

The full specification — file format, the two licence classes, the offline read mechanism and the traps — is `dependencies.md` §7. That is where implementers look; this ADR records why the shape is what it is.

## Consequences

- **Positive.** The gate is green on a correct codebase and red on a real problem, which is the only condition under which anyone will keep it. Requirement 3 survives option B's simplification: a new transitive package still stops the build until a human has looked at it.
- **Positive.** Licence *changes* are caught independently of version pins, because the check reads the resolved `.nuspec`. This is the FluentAssertions-8 scenario, and it is the one that costs money.
- **Positive.** The §5 rejection list becomes executable rather than advisory. B-01's reviewer verified by hand that none of the nine rejected packages was present; from B-11 that check is free and permanent.
- **Positive.** The gate stays offline and dependency-free: it reads committed lock files and the already-restored NuGet package cache. It adds seconds, not minutes, to `verify.sh`.
- **Negative — accepted.** Every dependency bump that changes the fan-out produces a diff in `dependency-closure.md`. That diff *is* the review artifact; the cost is one command and a glance at the added rows.
- **Negative — needs a governance carve-out.** `dependency-closure.md` sits in the architect-owned `docs/architecture/` but is regenerated by senior developers as part of any task that changes a dependency. `CLAUDE.md`'s ownership table does not anticipate a machine-generated file in a docs folder. The carve-out is written in `dependencies.md` §7.4 — developers may regenerate it with the documented command and may never hand-edit it — and a question is raised in `docs/HUMAN_INBOX.md` to have `CLAUDE.md` amended. Until the human answers, the carve-out stands as an architect's decision.
- **Negative.** Packages whose `.nuspec` carries a licence *file* or only a legacy `licenseUrl` instead of an SPDX expression cannot be resolved by the script and need a recorded human verification that must be repeated when the version changes. One such package is already in the graph (`xunit.abstractions` 2.0.3). This is a small, bounded manual surface — in the observed cache, 207 of 208 packages carried an SPDX expression.
- **Neutral.** `dependencies.md` §2/§3 version columns become documentation of what was verified when, not a gate condition; a version drifting from the document is reported by stage 4 and swept at each milestone health check. The licence property they exist to protect is enforced against the resolved package regardless.

## Revisit when

- Closure churn becomes a real nuisance — as a trigger, more than one regeneration per week sustained, or a single bump moving more than ~30 rows. The answer then is not to weaken the gate but to reduce the fan-out (fewer, better-chosen direct packages).
- A licence scanner appears that is permissively licensed, offline-capable, maintained and produces a reviewable, diffable artifact — option D becomes cheaper than our own script.
- NuGet ships first-class licence policy (a `packageSourceMapping`-style declarative licence allowlist honoured by restore). Then the gate moves into restore and this script shrinks to the rejection list.
- Any milestone health check finds the gate has been skipped, filtered or weakened in `verify.sh` — that is evidence the design failed, not that the team did.
