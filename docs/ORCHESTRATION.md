# Orchestration playbook

Rules for the orchestrator and for reviewers. **Developers do not need this file** — everything they
must know is in `CLAUDE.md` and `docs/DEVELOPER_BRIEF.md`.

It lives here rather than in `CLAUDE.md` because `CLAUDE.md` is loaded into *every* agent on *every*
run, including researchers and designers who will never dispatch a task or set a review tier. Keeping
it out of there is worth doing every time an agent starts.

## Task size is a hard limit, checked before dispatch

`.claude/agents/project-manager.md` has always said a task must fit one session, **aiming under about
400 changed lines**. It was not enforced, and the cost was immediate: B-12 shipped **9 004 lines across
69 files** and B-04 **4 540**, so neither could report anything for the better part of an hour. A task
that large cannot give feedback, cannot be reviewed carefully, and makes a rejection round
catastrophically expensive.

**The orchestrator checks size before dispatching, not after.** Read the acceptance-criteria row and
count what it actually demands. If it implies more than roughly 400 lines, or more than about six
acceptance criteria, or more than one subsystem — **send it to the project-manager to split first.**
Dispatching an oversized task is an orchestrator error, not a developer one.

Rules of thumb that catch it early: a row listing ten of anything is ten tasks or one task with a
scope cap; a row whose verbs include both "define" and "host"/"load"/"execute" is two tasks; a row
naming a contract *and* its implementation is two tasks.

### Every brief carries a scope cap

State the bounded deliverable, then this, explicitly:

> Deliver exactly these. **If a further improvement, hardening or abstraction suggests itself, do not
> build it — name it in your summary as a follow-up.** This brief is deliberately narrow.

Briefs that read "also consider…" invite breadth and get it. Ask for what the task needs and nothing
more; the reviewer and the next task will catch what is genuinely missing.

### Hand back at the first complete slice

Tell every developer:

> When you have one complete, working, tested vertical slice — not the whole task — commit it and
> report. Do not carry on to the next piece without handing back. A slice in review while you build
> the next is worth more than a finished task nobody has seen.

This converts one silent 50-minute run into two 25-minute runs with feedback in between, at no cost to
quality. The gate still applies to each slice.

## Starting a dependent task before its predecessor merges

A dependent task used to wait for its predecessor to be reviewed **and** merged, which put a whole
review's latency on the critical path. It may now start earlier, under conditions:

**Allowed** when the dependent task is **Standard** or **Light** tier, and the predecessor's branch is
**gate-green** — `./scripts/verify.sh` passes on it — even though its review is still open. Branch from
the predecessor's `task/<ID>`, not from the integration branch, and say in the first commit message
which branch you are stacked on.

**Never** when either task is **Full** tier. Tenancy, money, the ledger, tax, auth and migrations wait
for a merged predecessor. The whole point of a Full review is that the design may still change, and
rebuilding on a design that moved is more expensive than the wait.

**Whoever starts early owns the rebase.** When the predecessor merges, rebase onto the integration
branch and re-run the gate before handing over. If the predecessor's review forces a change that
invalidates your work, that is the cost of starting early — say so plainly rather than patching around
it.

The orchestrator decides and names this in the brief. Absent an explicit instruction, wait for the
merge.

## Review depth and length

**How deep a review goes is also set by the tier.** Reviews are the single largest cost in this
project's cycle time — on the two largest tasks so far, roughly half the total agent time went on the
rejection round — so depth must be spent where it changes outcomes.

| Tier | What the reviewer does |
|---|---|
| **Full** | Everything. Read the implementation against the ADR line by line, verify claims by running them, prove the tests bite by mutation, and attack the two or three properties the design actually rests on. This is what found the allocation blocker. |
| **Standard** | Run the gate. Verify the **top three risks** you identify in the change, by executing them rather than reasoning about them. Read the rest for correctness without exhaustive proof. Say explicitly which three you chose and why. |
| **Light** | Run the gate. Confirm the acceptance criteria are met. Check the change does not weaken an existing guarantee. Stop there. |

At Standard and Light, a reviewer who finds something that smells like a Full-tier risk should
escalate rather than quietly doing a Full review — say so in the verdict and let the orchestrator
decide. Depth that nobody asked for is depth nobody budgeted for.

### Length is part of the job

A finding is worth what it changes, not what it weighs.

| Tier | Target |
|---|---|
| **Full** | As long as the findings require. Evidence for a blocker is never cut. |
| **Standard** | ~800 words. Verdict, the top three risks and how you executed them, findings. |
| **Light** | ~300 words. Verdict, gate result, findings. |

Two habits regardless of tier: state a finding once, in the place it belongs, and put the evidence
that proves it next to it. A "Patterns noted" section earns its place only when the pattern is new —
if it restates one already in this file's self-check list, cite it in a clause instead.


## Keep the non-building roles busy

**2026-09-11.** The product owner noticed there had been almost no visible UI progress and
asked why more was not running in parallel. The answer was an orchestrator mistake, not a
sequencing constraint.

Every row from B-01 to B-15 is platform hardening, so no UI *code* ships until B-15 — that
is the hardening-first ordering the human confirmed and it stands. But the `ui-designer`,
`architect`, `project-manager` and `researcher` produce documents, not builds. **They do not
compete for the four CPUs that developers and reviewers saturate**, and almost none of their
work is blocked by the platform chain. Leaving them idle while three developers rebuild the
same solution buys nothing and hides progress the human can actually see.

The rule: **a developer or reviewer slot is contended; a docs-only slot is not.** Before
ending any orchestration round, check whether the designer and the architect have something
to do. They usually do — every review this iteration routed at least one question to the
architect, and the design system has run three screens behind the backlog since it was
written.

Concurrency, restated: **up to six agents, of which at most four may build.** Reviews and
developer tasks count against the four. Designers, architects, project-managers and
researchers do not.

## A finding raised twice and left is itself the finding

**2026-09-11, from B-04's second review.** The first review rejected B-04 for a `.csproj`
comment describing a fixture directory that did not exist (M-1), and separately listed as
**minor n-6** the residue of the same idea: a filter excluding any path segment named
`Fixtures/`. The author fixed M-1 and left n-6, exactly as the tiering allows.

n-6 was the half with teeth. A production `.csproj` under **any** directory named
`Fixtures/` vanished from the scanned population entirely — invisible not just to the
layering rules but to all six tenancy rules, the floating-point rule and the clock rule,
while the suite stayed green at 42/42. The rule set that is currently the only automated
proof of tenant isolation had a hole you could drive a module through, and it was written
down in the first review as a nice-to-have.

The rule this buys: **when the same idea appears in a review as both a major and a minor,
the minor is not independent.** Fixing the stated instance and deferring the mechanism
leaves the mechanism. Before accepting a rework, check every deferred minor that shares a
mechanism with an accepted major, and promote it rather than carrying it.

More generally, a minor's tier is a claim about blast radius, and that claim is worth
re-testing when the code around it changes. A minor deferred twice should be either fixed
or written into the backlog with an id — never carried a third time in a review file where
it reads as acknowledged and handled.

## Security-review an ADR before dispatching the rows that depend on it

**2026-09-11.** ADR-0029 specified sign-in, the `tid` claim and permission evaluation, and nine
bootstrap rows were written from it. Before dispatching any of them it went to a security
reviewer as a **design** review — no code existed. The reviewer returned **two blockers and
nine high findings**, and named six of the nine rows as not dispatchable as written.

Both blockers were cross-tenant, and neither was a mechanism the architect got wrong. They were
things **no document owned**: a cache key specified in a different ADR with no tenant in it,
which let the one legitimate mint site issue a claim for someone else's tenant; and find-or-create
on a platform-wide user record, which made invitation redemption an account takeover of an
existing customer in every tenant they belonged to.

Had those nine rows been dispatched first, the blockers would have been found — if at all — by
nine separate reviewers looking at nine separate implementations of a design that was already
wrong, after nine implementations had been written. The whole review cost one agent and under
fifteen minutes.

The rule: **when an ADR gates more than about three rows, and touches tenancy, money, auth or
anything a Country Package can influence, review the ADR before dispatching any of them.** Brief
it as a design review and say so — the reviewer's job is to attack the decisions, not to check
the prose. Ask specifically what it attacked and could *not* break, because that list is what the
architect must not redesign while fixing the rest.
