# ADR-0001 — Record architecture decisions

- **Status:** Accepted (2026-09-10)
- **Deciders:** architect
- **Supersedes:** —
- **Superseded by:** —

## Context

This is an autonomous team. Nobody remembers earlier sessions; the repository is the only memory. Decisions that live in a chat transcript are lost, and decisions that live only in code are discovered by archaeology. An ERP is expected to be maintained for a decade, which makes "why is it like this?" the single most expensive question we can fail to answer.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| No formal record; decisions live in code and comments | Zero overhead | Rationale is unrecoverable; rejected options are re-litigated every iteration |
| A single living `architecture.md` | One place to look | Edits erase history; no way to see what we used to believe or why we changed |
| **Numbered, immutable ADRs (Nygard format)** | History is append-only; a superseded decision keeps its reasoning; each decision is independently linkable and reviewable | Some ceremony; the set grows large |

## Decision

Every architecturally significant decision is recorded as `docs/decisions/ADR-####-kebab-title.md` with the sections: **Status, Context, Options considered, Decision, Consequences, Revisit when**.

Rules:
1. Numbers are allocated sequentially and never reused.
2. An **accepted ADR's Decision section is never edited**. To change a decision, write a new ADR that supersedes it, and update only the old one's `Status` and `Superseded by` lines.
3. ADR files are never deleted (a hard limit in `CLAUDE.md`).
4. "Options considered" must contain at least two credible options with honest pros and cons. An ADR with one option is not a decision, it is a statement.
5. Any ADR that names a third-party dependency must state the version, the SPDX licence and the date the licence was verified, and the dependency must also appear in `docs/architecture/dependencies.md`.
6. A decision made *for* us (locked by the product owner) is still recorded as an accepted ADR, with the options that were available and an honest Consequences section including our disagreement, if any.

"Architecturally significant" means: it is hard to reverse, it constrains other teams' work, it introduces a dependency, or it costs money.

## Consequences

- Positive: rationale survives personnel and session turnover; reviewers can reject a change by citing an ADR; rejected options are recorded so they are not re-proposed.
- Positive: the licensing discipline the project requires has a natural home.
- Negative: 25 ADRs exist at bootstrap, which is a lot of reading for a newcomer. Mitigated by `docs/architecture/overview.md` §7, which routes by question rather than by number.
- Negative: the immutability rule means a typo-level correction to a Decision section requires a superseding ADR. Accepted; clarifications may be added under a clearly-marked `## Clarifications (non-normative)` heading instead.

## Revisit when

The ADR set exceeds roughly 60 documents, or a newcomer cannot find the relevant decision within two minutes. At that point add an index with tags rather than changing the format.
