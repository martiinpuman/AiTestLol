# Review — ARCH-CORRECTIONS (docs: ADR-0030, ADR-0031, ADR-0028 Amendment 1, ADR renumbering)

- **Reviewer:** senior-reviewer (not the author of any commit on this branch)
- **Date:** 2026-09-11
- **Branch:** `task/ARCH-CORRECTIONS` @ `7b38b0c`, 5 commits, docs only (10 files, +327/−27)
- **Tier:** **Full** — one item is the append-only enforcement of the audit log, one decides what enforces the architecture rules, one decides what a Country Package may reference
- **Verdict:** **CHANGES_REQUESTED** — two majors, both one-sentence fixes. No blockers. The substance of all three corrections is sound and, where it claims to have been executed, it was executed: I re-ran every PostgreSQL statement and every one behaved exactly as the amendment says.

## Gate

`scripts/verify.sh` on a detached worktree of `7b38b0c`: **PASS** (17.1s).

```
 0 Preflight PASS · 1 Restore PASS · 2 Format & style PASS · 3 Build PASS
 4 Dependency licence gate PENDING (B-11) · 5 Vulnerability gate PENDING (B-11)
 6 Unit tests PASS — 208 test(s) executed
 7–10 PENDING (B-11) · 11 Summary PASS
 Gate incomplete: stage(s) 4 5 7 8 9 10 are not implemented yet.  RESULT: PASS
```

The branch changes no `src/`, `tests/` or `scripts/` file, so the result is the base result. Note that stage 4 being `PENDING` is itself the corroboration of ADR-0030's stated limitation: the ArchUnitNET rejection entry is not enforced by anything today, exactly as `dependencies.md` §5.1 says.

---

## 1. What I reproduced, and what I could not

Everything ADR-0028 Amendment 1 asserts about PostgreSQL was re-executed from scratch against `postgres:17-alpine` — **PostgreSQL 17.11 on x86_64-pc-linux-musl**, the pinned image — with roles `aurora_migrator` (schema owner) and `aurora_app`, each probe connected as the role under test. **Ten of ten claims reproduced. None failed. Nothing was left untested.**

| # | Claim in the amendment | Result |
|---|---|---|
| 1 | `ALTER DEFAULT PRIVILEGES … REVOKE UPDATE, DELETE` leaves `pg_default_acl` empty | **Reproduced** — `rows_in_pg_default_acl = 0` |
| 2 | After that no-op, an ordinary `GRANT … UPDATE, DELETE` on a new `audit` table is unrestrained | **Reproduced** — `relacl = {…,aurora_app=arwd/aurora_migrator}`, then as `aurora_app`: `UPDATE 1`, `DELETE 1` |
| 3 | The positive form creates the row `defaclacl = {aurora_app=ar/aurora_migrator}` | **Reproduced** — exactly one row, `(aurora_migrator, audit, r)`, ACL string identical to the ADR's |
| 4 | A table created **afterwards** by `aurora_migrator` comes out `SELECT`+`INSERT` only | **Reproduced** — `{aurora_migrator=arwdDxtm/aurora_migrator,aurora_app=ar/aurora_migrator}`; as `aurora_app`: `INSERT 0 1`, then `UPDATE`/`DELETE`/`TRUNCATE` all `ERROR: 42501: permission denied` |
| 5 | **The old acceptance criterion would have passed against the no-op** | **Reproduced, and this is the load-bearing one.** A table created after the `REVOKE` has `relacl = <null>`; as `aurora_app` both `UPDATE` **and** `INSERT` fail. A probe asserting only "UPDATE is refused" is green against a mechanism that does nothing |
| 6 | A table created in `audit` by a role other than `aurora_migrator` gets no `aurora_app` entry at all (fail-closed) | **Reproduced** — `relacl = <null>`, `has_table_privilege(aurora_app, …, 'SELECT'/'INSERT') = f/f` |
| 7 | `BEFORE UPDATE OR DELETE … FOR EACH ROW` on a partitioned parent is **cloned** to a partition created afterwards | **Reproduced** — `pg_trigger` on `audit_event_2026_09` carries `audit_event_append_only` with `tgparentid <> 0` |
| 8 | It fires for `aurora_migrator` (the owner) through the parent **and** directly against the partition | **Reproduced** — `ERROR: 42501` on all four statements |
| 9 | **Truncate triggers are not cloned**: `TRUNCATE` on the parent refused, `TRUNCATE` on the partition succeeds | **Reproduced** — the truncate trigger exists only on the parent; `TRUNCATE audit.audit_event` → `42501`, rows still 2; `TRUNCATE audit.audit_event_2026_09` → `TRUNCATE TABLE`, rows 2 → **0** |
| 10 | `CREATE EVENT TRIGGER` requires superuser on 17.11 | **Reproduced** — `ERROR: 42501: permission denied to create event trigger "guard"` / `HINT: Must be superuser to create an event trigger.` |

I also executed the H-1 premise that criterion 2 rests on, because a criterion resting on an unverified premise is the defect this branch exists to remove: after `GRANT UPDATE (detail) ON audit.later TO aurora_app`, `has_table_privilege(…,'UPDATE')` returns **`f`** while the `UPDATE` returns **`UPDATE 1`**, and `pg_class.relacl` does **not** show the grant — only `pg_attribute.attacl` does (`{aurora_app=w/aurora_migrator}`). Criterion 2's insistence on `aclexplode(relacl)` **plus** `attacl` is therefore necessary, not belt-and-braces. `MAINTAIN` is visible as the `m` in `arwdDxtm`, confirming the second half of H-1.

**On the criterion as written, not merely the prose.** `solution-layout.md` §6.4 item 1, criterion 3(b) (line 281) does assert the positive half — "the `INSERT` **succeeds** and the `UPDATE`/`DELETE` return `42501`" — with (a) pinning the `pg_default_acl` row. Against the no-op, (a) finds zero rows and (b)'s `INSERT` fails. **The replacement criterion can fail where the original could not.** That is the claim the branch made about itself and it holds.

**ADR-0031 §2's assembly claim, verified by building it.** I built `src/Aurora.Countries.Contracts` from `task/B-12` in a scratch worktree and read the `AssemblyRef` table of the produced `Aurora.Countries.Contracts.dll` with `System.Reflection.Metadata`:

```
System.Runtime · System.Text.RegularExpressions · System.Collections ·
Aurora.SharedKernel · System.Text.Json · System.Runtime.Numerics · System.Memory · System.Linq
```

`Aurora.SharedKernel` is named; **`Aurora.Documents.Canonical` is not** (it is an empty project — `csproj` + `packages.lock.json` and no source). A derived rule reading the compiled reference table computes a two-element set and fails against a correct three-element allowlist. The ADR's "read the `.csproj`, not the compiled assembly" is correct and the reason it gives is the real reason.

**Every other citation resolves.** `../reviews/B-04.md` row F1 records the planted `decimal`-in/`decimal`-out method with 5 violations at 4 sites; L1 fired on both the `.csproj` and the lock file for an unused `Microsoft.EntityFrameworkCore`; the 19-of-109 population-shrink demonstration is there. `testing-strategy.md` §5.4's new F1 text matches `FloatingPointRule`/`SolutionLayout` on the B-04 rework branch (population = every `.csproj` under `src/`; signatures, locals, IL opcodes, called members). `Aurora.Architecture.Tests.csproj` declares no `System.Reflection.Metadata` `PackageReference`. `PackageAssemblyReferenceRule.AllowedAuroraAssemblies`, `PackageSignature.ContentToSign`, the two `PackageSignatureVerifierTests` names, `PackageMetadataReaderTests.The_assemblies_a_package_references_are_read_and_checked`, `CoreContractGateTests`, `[ExtensionPoint]` and `CatalogSchemaAllowlist.AppRolePrivileges` all exist. `catalog.operator_audit_event` and `catalog.erasure_replay_log` genuinely do **not** exist anywhere in `src/` on `task/B-05` (only a comment in `CatalogSchemaGuard.cs` anticipating them), so the withdrawal of the retrofit sentence is accurate; B-05's migration does carry the "no `ALTER DEFAULT PRIVILEGES`, on purpose" comment the amendment credits it with.

---

## 2. Findings

### Majors

**M-1 — `ADR-0028-tenant-audit-store-and-write-path.md:143`: the amendment routes the project-manager to a section this branch renumbered away, and another in-flight branch is about to occupy it.**

> `- ../architecture/solution-layout.md` **§6.2** `re-specifies B-16.1's acceptance criteria …`

The section is now **§6.4**. Every other reference was updated by `7b38b0c`; this one was missed because the renumber commit did not touch `ADR-0028`. This is not a cosmetic dangling link: `task/ARCH-IDENTITY` introduces a real **§6.2 "The identity and authorization rows"**, so after that merge the pointer resolves to a section that exists and is about something else entirely. The sentence it sits in is the *only* instruction telling the project-manager to replace `BACKLOG.md`'s B-16.1 row — a row which today (verified, `BACKLOG.md:44`) still quotes `ALTER DEFAULT PRIVILEGES … REVOKE UPDATE, DELETE` and the criterion that passes against it. If that instruction is not followed, the no-op ships into the audit store. Given a Full-tier audit-trail change whose entire delivery mechanism is one cross-reference, a silently-wrong pointer is worse than a broken one.

*Fix:* `§6.2` → `§6.4` on line 143. One word.

**M-2 — `solution-layout.md:285` and `:289`: the tail-truncation follow-up and the catalog append-only tables are each defined as depending on a row that does not exist and that nothing asks anyone to write.**

The judgement call itself is **right**: recording tail truncation as a limitation rather than blocking B-16 is correct. The chain-head job cannot be built before `catalog.operator_audit_event` exists, B-16's value does not depend on it, and an append-only log that detects every tamper except tail truncation by a DDL-capable role is strictly better than today's nothing. ADR-0018 §1 really does specify the daily chain-head job, so the ADR is citing a real plan, not inventing a rescue.

What is wrong is the **execution of the follow-up instruction**, and the same defect appears twice:

- line 289: "Carry it as a follow-up (the project-manager assigns the id), **depending on the row that creates `catalog.operator_audit_event`**";
- line 285 (criterion 7): "**When the row that creates them is written**, it carries criteria 2 and 4 …".

No such row exists in `BACKLOG.md`, no §6.4 item creates one, and both sentences are phrased as though the other will produce it. A project-manager transcribing §6.4 faithfully ends up with one follow-up blocked forever on a predecessor nobody owns. This is the planning-level form of the defect the branch exists to remove: an instruction whose mechanism you cannot point at. Three of the four §6.4 items are executable rows; this one is not.

*Fix:* add a fifth §6.4 item — the row that creates `catalog.operator_audit_event` and `catalog.erasure_replay_log`, carrying criteria 2 and 4 and the `CatalogSchemaAllowlist.AppRolePrivileges` entry with the writer named (criterion 7 already specifies its content, so this is a re-home, not new design) — and make the tail-truncation follow-up depend on **that item** by name. Both then have owners, and §6.4 stays a document the PM can transcribe end to end.

### Minors (follow-ups, not merge blockers)

**m-1 — `ADR-0005-frontend-blazor-server.md:27`: the one remaining unmarked invitation to reintroduce the withdrawn dependency.** Rule 1 still reads "Enforced by an **ArchUnitNET** test". ADR-0030 redirects it in its *Supersedes* line and argues in Consequences that ADR-0005's text is history — but ADR-0020's equivalent sentence was *not* left as history: its row was struck through and the ADR gained an amendment header. The convention is applied to one ADR and not the other, and the unmarked one is the one a reader reaches by searching for "ArchUnit". With `check-dependencies.sh` stage 4 not existing, documentation is the *only* thing standing between a developer and re-adding the package. *Fix:* one line in ADR-0005's header — "Amended 2026-09-11: rule 1's enforcement mechanism is superseded by ADR-0030" — leaving the product-owner-locked decision text untouched.

**m-2 — `docs/reviews/B-12.md` is untracked and exists only inside a locked agent worktree.** ADR-0031 cites `../reviews/B-12.md` six times (Related line, §1's m2, §2, §3's M2, §4's m7) and `solution-layout.md` §6.4 item 4.6 cites it again. `git ls-tree` finds it on **no branch**; the file (and `security-B-12.md`) sits untracked in `.claude/worktrees/agent-a6883ccecd5c360c2`. Every evidentiary citation in the Country Package ADR currently resolves to nothing in the repository that is supposed to be the team's only memory. *Fix (orchestrator, not the architect):* commit both review files before or with this merge.

**m-3 — `solution-layout.md:281`, criterion 3(b): the creating role is unnamed.** The default ACL applies **only** to tables created by `aurora_migrator` — I verified that a table created in `audit` by any other role gets `relacl = null` and grants `aurora_app` nothing. "A table created in `audit` after the migration" does not say who creates it. A fixture that creates the probe table as the test container's superuser fails the `INSERT` half, and the tempting repair is to add an explicit `GRANT` to the probe table — which silently converts criterion 3(b) back into a check that no longer tests the default at all. *Fix:* "a table created in `audit` **by `aurora_migrator`** after the migration".

**m-4 — `solution-layout.md:3`: the status line still says `accepted v3` while the file now carries "Changes in v4" and "Changes in v5".** Pre-existing from v4; this branch adds the second stale increment, and the merge with `task/ARCH-IDENTITY` (which also calls its change v5) is the moment to settle it. *Fix:* bump to `v5` as part of the conflict resolution.

### Nits

- **n-1 — `dependencies.md:163`** (a line this branch edited): "`check-dependencies.sh` fails if any of these ids appears anywhere in the graph" is present tense for a script that §5.1, twenty-eight lines below, correctly says does not exist. The document is like this throughout and predates the branch, but the line was touched; "(at `verify.sh` stage 4 — see §5.1, not implemented yet)" would keep the claim and its mechanism together.
- **n-2 — `ADR-0031` §2:** the derived allowlist is one level deep — `{contract} ∪ {contract's *direct* `Aurora.*` ProjectReferences}`. If `Aurora.SharedKernel` or `Aurora.Documents.Canonical` ever takes an `Aurora.*` project reference of its own and the contract exposes its types, a conforming package will emit a reference the derived set does not contain and be refused. One sentence saying the derivation is deliberately direct-only, and what to do when that stops being true, would cost nothing now and save a confusing refusal later.
- **n-3 — `ADR-0028` Amendment 1, "What was wrong with it":** it names mechanism 2 as the no-op but says nothing about the original mechanism 1 (`REVOKE UPDATE, DELETE ON ALL TABLES IN SCHEMA audit FROM aurora_app`), which also removes nothing when `aurora_app` was never granted those privileges. The replacement §2 is clean — it asserts the resulting ACL and explicitly stops asserting which statement ran — so no false claim survives; only the post-mortem is one bullet short.

---

## 3. Verdict on each of the four items

**1. ADR-0030 — accept the decision.** The supersession is scoped exactly as claimed: the *Architecture tests* row, the Consequences bullet and the *Revisit when* clause of ADR-0020, each struck in place with an amendment header saying everything else stands; I checked the diff line by line and no other row was touched. The IL-metadata-plus-project-file split is the right mechanism and, unusually, the ADR can point at eleven planted violations that were watched going red. The "Withdrawn after adoption" section is real: the package is out of the approved table, a pointer replaces it where a reader scanning that table will hit it, the ids are in the machine-readable block, and the honest "what enforces this, and what does not yet" paragraph sits directly under the withdrawal table in bold — not buried. §5.1's own admission that stage 4 does not exist is corroborated by the gate output above. The `testing-strategy.md` §5.4 F1 correction matches the implemented rule: population is every project under `src/`, and the rule reads locals, `ldc.r8`/`conv.r8`, and called members whose signature mentions a floating-point type. The ADR's refusal to assert "ArchUnitNET could not express F1" is the right call and is the standard the rest of this review applied to it. Only m-1 is outstanding.

**2. ADR-0028 Amendment 1 — accept the mechanism; the ten behaviours are real.** See §1. The positive default grant does what the revoke never did, the row trigger is cloned and stops the owner, the truncate trigger is not cloned and the job must attach it, event triggers are out of reach without superuser, and the old criterion genuinely could not fail. The withdrawn retrofit sentence is gone and what replaces it (criterion 7) describes only work nobody has done — correctly labelled as not done. On the two admitted limitations: the DDL-capable role is correctly reduced to detection, and the two extra tamper cases added to B-16.2 are the right cases. On tail truncation, recording it is the right call — but see **M-2**, because the recording currently terminates in a row nobody owns.

**3. ADR-0031 — accept all five answers, and answer 3 emphatically.** **The bounded-range decision is correct; the B-12 rework should continue on it without waiting for this review.** An unbounded `coreContractRange` makes extension point 10 decorative and turns §5.2's fleet compatibility report into a green-light generator, and "compatible with a contract version that did not exist when the package was built" is not a claim anybody could have verified. Making it a **manifest validity** rule rather than a gate outcome puts the invariant where the value is constructed, which is the right place; keeping the duplicate check in `CoreContractGate` is justified because the gate also runs over catalogue manifests and a target version the process is not running; and turning `CoreContractGateTests`' currently-passing `("1.0.0","1.4.0")` row into a refusal is exactly the evidence that the rule landed. The cost — a package re-release per MAJOR bump — is real, acknowledged and paid for by §5.2's deprecation window. The derived allowlist (answer 2) is a genuine improvement over a hand-maintained constant and fails in both directions; the `.csproj`-not-assembly reasoning is verified above. §3.2's contract test **is** recorded as open, in §1 under "Demonstration that does not exist yet, recorded as open" and again in the Consequences negative bullet naming all three missing demonstrations — not presented as done. Answer 4's registry-plus-exception-list, with `ITaxCategoryMapping.DefaultCodeFor` explicitly parked rather than quietly blessed, is the right shape, and requiring a fixture interface written to break it is the right instinct. Answer 5's "built whole, not in halves" argument for `Aurora.Countries.TestKit` is correct, as is keeping it out of `Aurora.TestKit`.

**4. The renumbering — correct except for M-1.** Every `ADR-####-name.md` link in `docs/` resolves to a file that exists (checked exhaustively). No reference to **ADR-0008's own §6.2** was altered — all seven survivors (ADR-0007:314, ADR-0017:40, ADR-0020:27, ADR-0021:19 and :43, ADR-0008:248, `modules.md`:116, `testing-strategy.md`:121) are byte-identical to base. The only remaining `0029` mention in the tree is `HUMAN_INBOX.md:85`, which refers to the identity ADR and is correct — it is `task/ARCH-IDENTITY`'s number, not this branch's. The one miss is `ADR-0028:143`.

**The two conflict hunks: combining is the right resolution, with one ordering constraint.** Both hunks are pure additions at the same append point and the two branches' content is disjoint — nothing needs to be chosen between, only sequenced.

- *Hunk 1 ("Changes in v5"):* merge into one paragraph naming §6.2/§6.3 (identity rows, ADR-0029) and §6.4 (corrections; ADR-0028 Amendment 1, ADR-0030, ADR-0031). Both branches independently called their change v5, which is accurate — same day, one version. Bump the status line to **v5** while you are there (m-4).
- *Hunk 2:* keep both sides in this order — `B-15.1 gains B-16.2 …` → **this branch's B-16.1 override blockquote** → the identity branch's "Still open after v4, closed in §6.2/§6.3 below" sentence → §6.2 → §6.3 → §6.4. The blockquote says "the B-16.1 row **above**"; if it is placed after §6.2/§6.3 the row it points at is no longer above it, and the override loses its referent. That is the only way this resolution can go wrong.

---

## 4. To route

- **Project-manager:** §6.4 items 1–4 are ready to transcribe once M-1 and M-2 land — B-16.1's replacement criteria (`BACKLOG.md:44` still carries the no-op statement), the Light-tier ArchUnitNET pin removal (sequenced after B-04 merges), the Full-tier `Aurora.Countries.TestKit` row, and the six B-12 rework items (two of which enlarge that branch and should reach the size check).
- **Orchestrator:** commit `docs/reviews/B-12.md` and `docs/reviews/security-B-12.md` (m-2); they exist only as untracked files in a locked worktree while ADR-0031 cites them six times.
- **B-12 rework:** proceed on the bounded-range answer. This review endorses it without reservation.

## 5. Patterns worth remembering in this codebase

1. **A renumbering is finished when the files that *reference* the renamed thing are re-grepped, not only the files the branch already touched.** The one miss here was in an ADR the renumber commit had no other reason to open. Grep for the *old section number* as well as the old ADR id.
2. **A cross-reference that another in-flight branch will make valid-but-wrong is more dangerous than a broken one.** Two branches editing the same numbered-section space need the numbers reserved before either writes, not reconciled after.
3. **Planning documents inherit the "name the mechanism" rule.** "Carry it as a follow-up depending on the row that creates X" is the planning-level version of a claim with no mechanism behind it when no row creates X. Every follow-up must terminate in something that exists or in a row this document creates.
4. **PostgreSQL privilege statements must be executed, never read.** `ALTER DEFAULT PRIVILEGES … REVOKE` on a privilege the role never had writes no `pg_default_acl` row; `has_table_privilege` is blind to column-level grants and short by `MAINTAIN` on 17; row triggers clone to new partitions and truncate triggers do not. All four were confirmed here in about ninety seconds against the pinned image — cheaper than any argument about them.
5. **When correcting an acceptance criterion, state which half of it can fail.** The pattern that worked here: assert the *exact* privilege set so the criterion fails both when the mechanism is missing and when someone widened it.
