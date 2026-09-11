# Developer brief — read this first, then only what it sends you to

`docs/` is ~91,000 words. You do not need most of it. This page exists so you can stop reading it
after about three minutes and know exactly which sections to open for the task you were given.

**This is a routing document, not a summary.** Where it states a rule, the rule is binding. Where it
describes a design, open the named section — do not implement from this page's description of it.

## The five things that are true on every task

1. **Integration branch is `claude/multi-tenant-saas-erp-pv2nap`, never `main`.** Branch to
   `task/<ID>`, commit there, do not merge, do not push. The orchestrator integrates.
2. **Commit as soon as your work first compiles**, then in small steps. Usage limits have killed
   agents on this project five times; committed work always survived, uncommitted work nearly did not.
3. **`source scripts/dev-env.sh`** before any `dotnet` command. The SDK is not on `PATH` by default.
4. **The gate is `./scripts/verify.sh`.** It must pass. Stage 6 enforces a minimum executed-test
   count, so a suite that silently stops running fails the build.
5. **Read `CLAUDE.md`'s "Self-check before you submit"** and apply it before you hand over. Every one
   of the first three tasks was rejected for one of those three shapes. It is the cheapest thing you
   can do to avoid a rework round.

## Which document answers which question

| Your task involves | Open | Not the whole file — these sections |
|---|---|---|
| Anything at all | `CLAUDE.md` | Self-check, Review tiers, Definition of Done |
| Where a project or file goes; what `verify.sh` does | `docs/architecture/solution-layout.md` | §1 layout, §4 build files, §5 gate stages, §6 your task's row |
| Which module may reference which | `docs/architecture/modules.md` | §3 the dependency table |
| How to test something | `docs/architecture/testing-strategy.md` | §1 principles, §5 fitness rules, the shared-container rule |
| Tenants, databases, connections, migrations | `docs/decisions/ADR-0007` | §3 roles, §4 the structural guarantee, §5 pooling, §7 migrations, §8 provisioning, §9 catalog schema, §12 isolation tests |
| Country Packages, extension points | `docs/decisions/ADR-0008` | §4 package schemas, §5.1 install steps, §6 effective dating, §9 the load/signature seam |
| Money, rounding, allocation | `docs/decisions/ADR-0021` | §6 and the Consequences |
| Tax, rates, registrations | `docs/decisions/ADR-0023` | and ADR-0021's Consequences, which apply to it |
| A user-visible screen | `docs/design/components.md`, `docs/design/app-shell.md` | the component you are building, and its states |
| What a feature is *for* | `docs/product/specs/` | the SPEC named in your brief |

## What NOT to read

`docs/` is 64 files and ~100 000 words. Reading more than your task needs costs time and tokens and
makes you no better at the task. In particular:

- **Do not read `docs/reviews/`.** It is ~16 000 words of past reviews. Everything durable in it has
  been distilled into `CLAUDE.md`'s self-check list. The one exception: if a review *is* your brief —
  you are reworking a task and the brief names `docs/reviews/<ID>.md` — read that one file.
- **Do not read whole ADRs.** The table above names the sections. ADR-0007 alone is 6 900 words and
  you almost certainly need three of its sections.
- **Do not read other modules' documentation** to understand yours. If your task needs a contract
  another module owns, the brief says so; if it does not and you think it should, ask rather than
  reading your way to an answer.
- **Do not read `docs/research/` or `docs/design/`** unless your task builds a screen or a Country
  Package. They answer *what to build and why*, not *how*.
- **Do not read `docs/ORCHESTRATION.md`.** It is the orchestrator's and reviewers' rules for
  dispatching and reviewing; nothing in it changes what you build.

A good rule: if you cannot say which acceptance criterion a document is helping you satisfy, stop
reading it.

**Your task's acceptance-criteria row in `solution-layout.md` §6 is a floor, not the contract.** The
ADR it implements is the contract. A row narrower than its ADR has already let one blocker through to
review — if you notice the gap, say so; that is an architecture finding, not a nit.

## The five traps this codebase has already fallen into

Each cost a review round. They are described properly in `CLAUDE.md`; this is the index.

1. A comment claiming behaviour that no code produces.
2. A check that reports success having measured nothing.
3. A test whose generator cannot reach the region where the law breaks.
4. `decimal` rounding silently past 28–29 significant digits in money arithmetic.
5. A fault injected from outside on a timer, which is flaky on a different machine.

## What a good handover looks like

The orchestrator wants a short summary, not logs: branch name, what you built against each acceptance
criterion, **the verbatim `verify.sh` result**, every judgement call you made on an ambiguous point and
why, anything in the ADRs you found wrong or unclear (report it — do not edit `docs/architecture/` or
`docs/decisions/`, they belong to the architect), and follow-ups you recommend.

If you were asked to demonstrate that something fails, **the demonstration is a deliverable.** Report
what you broke, what the output said, and that you reverted it.
