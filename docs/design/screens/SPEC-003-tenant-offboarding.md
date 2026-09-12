# Screen spec — Tenant offboarding (operator console)

Status: ready · Author: ui-designer · Date: 2026-09-12
Backlog: `DESIGN-002` · Unblocks: `B-07`
Product spec: none — see `SPEC-003-tenant-list.md`'s front matter.
Architecture: `../../decisions/ADR-0007-multi-tenancy-database-per-tenant.md` §11.1 (backup), §11.4 (the offboarding state machine — quoted in full below), §11.5 (GDPR erasure vs. tenant deletion — this spec is the former, never the latter); `CLAUDE.md` ("offboarding must be able to export and then destroy exactly one tenant's data")
Prototype: `../prototypes/tenant-offboarding.html`
Components referenced: App shell → Operator console (`app-shell.md`), Workflow stepper (`components.md` §13), Dialogs incl. Danger zone panel (§14), Status badges (§12), Toasts (§15)

## Scope

**In:** the four-stage journey a tenant takes from `Active` to `Deleted` — `Suspended → Exporting → PendingDeletion → Deleted` — as one page per tenant, and the two most consequential controls in the whole operator console: starting an export, and the final, irreversible `DROP DATABASE`.

**Out:** anything about *how* the export or the deletion is produced server-side (the saga/job mechanics belong to whichever backlog row implements ADR-0007 §11.4); a bulk "offboard N tenants" flow (ruled out for the same reason `SPEC-003-tenant-list.md` has no bulk actions at all); GDPR data-subject erasure (§11.5) — a completely different operation this spec must not be confused with, see "What this is not," below.

## Purpose

`CLAUDE.md` requires this to work; ADR-0007 §11.4 requires `DROP DATABASE` never be automatic; this task's own brief requires the destructive step be "impossible to reach by accident and impossible to confuse with the export step." This spec is the answer to all three at once, on one page, because splitting "export" and "destroy" across two different screens would make it easier, not harder, to lose track of which one an operator is looking at.

## What this is not

**This is not GDPR data-subject erasure.** ADR-0007 §11.5 is explicit that erasing one individual's data inside a tenant that keeps operating is pseudonymisation, done from *inside* that tenant by its own users, on a completely different timeline (deferred to a statutory retention date) and mechanism (an erasure replay log, re-applied after restores) than anything here. This screen ends with the tenant's **entire database gone** — the correct answer only when the *customer's contract* ends, never the answer to "one person asked to be forgotten." If an operator arrived here believing it does the latter, that is exactly the kind of confusion `CLAUDE.md`'s two-different-operations warning exists to prevent, and it is called out here in the loudest place a reader of this file will see it.

## User

The same Aurora operator as the other two specs, having reached this page from `SPEC-003-tenant-detail.md`'s "Begin offboarding…" (from `Active`) or "Manage offboarding →" (from `Suspended`/`Exporting`/`PendingDeletion`) action.

## Task flow

1. From `Active`, "Begin offboarding…" navigates here and shows all four stages upcoming, with an explanation of the whole journey before anything is clickable.
2. The operator confirms **suspension** (restates consequences per Principle 2 — reversible, read-only, 30-day-ish first stage, no fixed deadline of its own) → tenant becomes `Suspended`.
3. From `Suspended`, the operator either **resumes** (returns to `Active`, exits this flow entirely — the easy, safe, one-click reversal) or **starts export** → tenant becomes `Exporting`.
4. Export runs; when both bundles complete, two signed, expiring download links appear and "Continue to pending deletion →" — previously disabled — becomes available.
5. Continuing sets `deletion_due_at` 30 days out and moves the tenant to `PendingDeletion`. From here, **cancel deletion** (safe, restores to `Active`, available any time before the final step) sits beside, but is never confusable with, **permanently delete** (available only once `deletion_due_at` has passed, gated behind the heaviest confirmation in this whole design system).
6. Once actually deleted, this page shows the deletion certificate and nothing else is actionable.

## Layout

The stepper (`components.md` §13) is shown in its **vertical, expanded** form on every breakpoint this spec targets, not just on tablet — every stage carries enough content (dates, download links, warnings) that a horizontal bar with a current-step-only detail panel would hide the very information a "deliberately uncomfortable" flow needs kept visible.

```
┌───────────────────────────────────────────────────────────────────────────┐
│ A  Aurora ERP · Operator Console                          J. Alvarez  ⏻   │
├───────────────────────────────────────────────────────────────────────────┤
│  ← Meridian Distribution                                                  │
│  Offboarding Meridian Distribution                                       │
│  ───────────────────────────────────────────────────────────────────────  │
│  ✓ Suspended            2026-08-15 09:02 · J. Alvarez                    │
│  ✓ Exported             2026-08-15 09:40 · both bundles ready            │
│    ⤓ Portability dump (.pgdump, expires 2026-08-22)                      │
│    ⤓ Open-format bundle (CSV/JSON + interchange, expires 2026-08-22)     │
│  ● Pending deletion  ← current stage                                     │
│    Deletes automatically-eligible from 2026-09-14 (30-day grace)         │
│    Today: 2026-09-12 · 2 days remaining                                  │
│    ┌─────────────────────────────┐   ┌─────────────────────────────────┐│
│    │ Cancel deletion             │   │  DANGER ZONE                    ││
│    │ Restores this tenant to     │   │  Permanently delete             ││
│    │ Active immediately.         │   │  Not available until 2026-09-14.││
│    │        [ Cancel deletion ]  │   │  [ Permanently delete… ] (later)││
│    └─────────────────────────────┘   └─────────────────────────────────┘│
│  ○ Deleted               not yet reached                                 │
└───────────────────────────────────────────────────────────────────────────┘
```

## Stage-by-stage design

### Stage 0 — not yet started (tenant is `Active`)

All four stages render `upcoming` (`components.md` §13). A plain-language paragraph states the whole journey before the first click exists: *"Offboarding suspends this tenant (read-only, reversible), then produces an export, then a 30-day reversible pending-deletion window, then permanent deletion. Nothing here is irreversible until the final step."* The one action, `tenants.offboarding.beginAction` ("Suspend {tenant} to begin"), opens an ordinary confirmation dialog (**not** a typed-name gate — suspension is reversible, so this follows `components.md` §14's normal "confirming/submitting" pattern, not its "for the most irreversible actions" escalation) restating the target tenant by name, per Principle 2.

### Stage 1 — `Suspended`

Two actions, deliberately unequal in visual weight because they are unequal in consequence:

- **"Resume tenant"** (`secondary` button) — one confirmation, returns to `Active`, exits this page back to `SPEC-003-tenant-detail.md`. The safe path stays cheap.
- **"Start export →"** (`primary` button) — one confirmation naming what happens next ("This begins producing an export. The tenant stays read-only.") → `Exporting`.

Read-only info shown: `suspended_at`, and who suspended it (from the audit trail) — restating Principle 2's "who, when, what" even for a reversible step.

### Stage 2 — `Exporting`

No user action while either bundle is in progress — a progress row per bundle (`tenants.offboarding.dumpBundle` / `openFormatBundle`), each `pending → running → ready` (or `failed`, with a retry affordance scoped to *that bundle only*, per `components.md` §8's own "error mid-scroll, retry inline" precedent for a partial failure rather than restarting the whole export). Per ADR-0007 §11.4, two bundles are produced: a `pg_dump -Fc` (portability back to Aurora) and an open-format bundle (CSV/JSON per aggregate plus the tenant's Country Package's accounting-interchange format, ADR-0008 extension point 6). When both are `ready`:

- Two signed, expiring download links appear, each showing its own expiry date plainly (`tenants.offboarding.expiresOn`) — an expired link re-renders as `tenants.offboarding.expired` with a "Regenerate" action, never a silently-dead link.
- **"Continue to pending deletion →"** becomes available. Before both bundles are ready it is present but `aria-disabled`, reason `tenants.offboarding.continueDisabledReason` ("Available once both exports complete") — this is one of **two** places in this spec (the other is "Permanently delete…" in Stage 3 below) where a control is disabled-with-reason rather than absent, and both are the same, named exception to the disabled-vs-absent rule: a **same-state condition that resolves on its own with no further click** (a background job finishing here; a calendar date arriving in Stage 3), where *why can't I do this yet* is exactly the question the operator needs answered without guessing, and hiding the control would hide that answer. This is not a counterexample to the cross-state rule `SPEC-003-tenant-detail.md` states as absolute — that rule is about an action belonging to a *different* `TenantState` never appearing on this one; both controls here belong to the *current* state and stay disabled only until a condition inside that same state resolves.

**No reverse path is offered from `Exporting`.** ADR-0007 §11.4's diagram is one-directional from here on; this spec does not invent a "cancel and resume" action mid-export, because nothing in the ADR describes what an interrupted export leaves behind. Flagged in "To route" rather than guessed at.

### Stage 3 — `PendingDeletion` — the deliberately uncomfortable step

This is the stage the task's brief is centrally about, so its design gets stated as a set of explicit rules rather than left implicit in the mock-up above:

1. **The countdown is the header of this stage, not a field inside it.** `deletion_due_at` and "today," both locale-formatted, plus a locale-formatted day count (`tenants.offboarding.daysRemaining`) — the single most important fact on the page while this stage is current.
2. **Two panels, side by side, never one below the other in a single list of buttons.** Left: a plain `panel` (not `danger`-bordered) holding "Cancel deletion" alone. Right: the Danger zone panel (`components.md` §14's new convention) holding "Permanently delete…" alone. Spatial separation is load-bearing here, not decorative — the brief's "impossible to confuse with the export step" generalizes to "impossible to confuse with the *other* button in this same stage," and two visually distinct containers do that more reliably than font weight or color alone (which a colorblind operator, or a quick glance, can miss).
3. **"Cancel deletion" is always available** the instant this stage is reached, with exactly one confirmation ("This restores {tenant} to Active immediately.") — cheap, because reversal should always be cheap.
4. **"Permanently delete…" is present but genuinely inert until `deletion_due_at` has passed** — the second of this spec's two same-state, resolves-on-its-own exceptions to the disabled-vs-absent rule (see Stage 2's "Continue" above, which names both together). Before that date it renders `aria-disabled` with the reason `tenants.offboarding.notYetEligible` ("Not available until {date}"), so the operator knows what's coming and when rather than discovering a hidden control later; there is no way to accelerate the grace period from this UI, matching ADR-0007 §11.4's stated 30-day window having no documented override.
5. **Once eligible, activating it opens the heaviest confirmation dialog in this entire design system** — see below.

### Stage 4 — `Deleted`

Terminal. Shows the deletion certificate (`tenants.offboarding.certificateHeading`): who triggered final deletion, when, the backup expiry date (ADR-0007 §11.4: backups expire within 35 days independent of the tenant row), and a restatement that this event is recorded in `catalog.operator_audit_event`. No control on the page is interactive except navigation away.

## The permanent-delete confirmation

Deliberately harder than `SPEC-003-tenant-detail.md`'s `ProvisioningFailed` destroy dialog, because this tenant, unlike a `ProvisioningFailed` one, may hold real business data someone built. One system-performed identity check, run twice, plus three operator-satisfied gates:

**The identity check (system-performed, not an operator gate).** Exactly like `SPEC-003-tenant-detail.md`'s destroy dialog, opening this dialog re-checks `platform.tenant_identity` against this tenant id and shows the result before the three gates below are reachable — `identityConfirmed` or `identityMismatch`, the whole dialog inert on a mismatch. **The same check runs a second time, at act time**, immediately before the destroy command is sent, when "Permanently delete" is clicked — not reused from the first, on-open result. See "Detection, not prevention" immediately below for why this is two checks and not one, and what it does and does not guarantee.

Then, the three gates a human satisfies, in any order, all required before the submit button's `aria-disabled` clears:

1. **Export re-shown, not re-trusted from memory.** The dialog restates the export completion timestamp and both bundles' download links from Stage 2 inline, so the operator is looking at proof, not recalling it. If either bundle's signed link has since expired, the dialog blocks entirely with `tenants.offboarding.exportExpiredBlock` ("The export has expired. Regenerate it before deleting.") and no path forward except closing the dialog — a deliberate structural requirement that the export must be *currently retrievable*, not merely "was produced once," at the moment of permanent deletion.
2. **A checkbox, not pre-checked**: `tenants.offboarding.verifiedExportCheckbox` ("I have verified the export at {timestamp} is complete and retrievable.") — an explicit act distinct from the typed confirmation in gate 3, so a fast, careless "paste the key and go" pattern (which an operator could build muscle memory for after gate 3 alone) cannot skip this attestation.
3. **Type the tenant key to confirm**, exactly the same mechanism as `SPEC-003-tenant-detail.md`'s destroy dialog (Principle 4 — one way to confirm an irreversible action, not two different typed-confirmation UIs for two different destroy buttons in the same product).

The submit button (`tenants.offboarding.confirmDelete`, "Permanently delete") stays `aria-disabled` until all three gates are satisfied and the first identity check has confirmed; satisfying the gates in any order is allowed, but all three must hold at submit time. Clicking submit then runs the *second* identity check before anything destructive happens: if it disagrees with the first (the tenant's state or identity stamp changed while the dialog was open), the dialog aborts, shows `identityMismatch` again, and destroys nothing — the three gates the operator already satisfied do not carry over to a retry, and re-opening the dialog runs both checks fresh. If the second check confirms, the dialog closes, the page moves to Stage 4, and a toast + certificate + audit entry all record both check results and the outcome. **`Esc` and any backdrop click are inert once submission is in flight** (`components.md` §14's "never while a commit is actually in flight" rule), and — because this is the one action in the whole design system where "the circuit dropped and we don't know if it worked" is least acceptable to leave ambiguous — a dropped circuit here resolves exactly like `SPEC-002-company-create.md`'s "outcome unknown" pattern: the dialog does not retry, and tells the operator to reload the tenant's page and check its state before touching anything else.

### Detection, not prevention

Stated plainly rather than implied, because a UI spec that lets a reader assume this guard is atomic would be worse than one that says nothing: **the identity check and the destroy command cannot be one operation, no matter how the dialog is built, because PostgreSQL will not let a session rename or drop the database it is currently connected to.** Reading `platform.tenant_identity` requires a connection *to* the tenant's own database (by then renamed to `deleted_<key>_<date>` per Stage 3); issuing `DROP DATABASE` requires a *different* connection, to the cluster's maintenance database, addressing the target only by name. Between closing the first connection and opening the second, nothing at the database level re-verifies that the name still refers to what the check just read — the check and the act are sequential, on two connections, never one transaction.

What actually limits the blast radius of that irreducible gap is not the check being atomic — it isn't — it is two other things this design already does for unrelated reasons: the target was already isolated thirty days earlier at Stage 3 (renamed, `CONNECT` revoked from everyone but `aurora_admin`, per ADR-0007 §11.4), so nothing else has a path to concurrently rename or repoint it in the gap between the two connections; and every check result, on both the first and the act-time run, is written to the deletion certificate and `catalog.operator_audit_event` regardless of outcome, so a wrong drop — should the irreducible gap ever actually matter — is detected and auditable after the fact even though it cannot be structurally prevented within the operation itself. **This is why the act-time check is required (the AC below) even though it cannot close the gap completely: a check performed only once, at render time, adds nothing once the operator has been looking at the dialog for a while; a check repeated immediately before the act at least narrows the window to the two-connection handoff itself, which is the smallest it can get.**

This residual belongs in an architecture decision, not only here — this section is the design's honest account of it pending that. See "To route."

## States

| State | What the operator sees |
|---|---|
| **Loading** | Skeleton stepper (all stages present, no state yet resolved) rather than a blank page — the shape of the four stages is known regardless of which one is current. |
| **Error loading the tenant** | Inline error, retry; no stepper renders until the tenant's actual state is known (rendering a guessed stepper state would be worse than a delay). |
| **Missing `platform.tenant.manage`** | The whole stepper renders read-only (every stage's action becomes disabled-with-reason, same convention as `SPEC-003-tenant-detail.md`) — an operator can still *see* where a tenant is in offboarding without being trusted to move it. |
| **Missing `platform.tenant.destroy` specifically** | Every action up through "Continue to pending deletion" and "Cancel deletion" works normally; only "Permanently delete…" is disabled-with-reason naming `platform.tenant.destroy` — the same split as the detail spec's two permissions, exercised at its sharpest point. |
| **Concurrent change** | Another operator (or an automated process — none exists per this milestone's scope, but the UI does not assume it never will) can advance/cancel the same tenant from another session; on any action attempt returning 409, the page reloads its state and shows a banner, the same pattern as the detail spec. |
| **Export bundle failed** | That bundle's row shows `failed` with a retry affordance scoped to it; the other bundle, if already `ready`, is unaffected; "Continue" stays disabled until both are `ready`. |

## Locale and formatting

| Value | Locale-formatted? |
|---|---|
| `deletion_due_at`, "today," every timestamp in the stepper and the certificate | **Yes** — shared date/date-time formatter. |
| `{days remaining}` | **Yes** — number formatter, same discipline as every count elsewhere in this design system. |
| Bundle expiry dates | **Yes** — same date formatter. |
| Tenant key (typed-confirmation gate) | **No** — an identifier, case-sensitive, never transformed. |

## Keyboard

| Action | Key |
|---|---|
| Move between stepper stages (informational, not a tab set — only the current stage has interactive controls) | Standard document flow; the stepper is not a `tablist`, because `components.md` §13 explicitly limits click-navigation to completed/current steps, and this flow has no "jump ahead" concept at all |
| Begin/resume/start-export/cancel-deletion confirmations | `Tab` to the action, `Enter`/`Space` opens its dialog; standard dialog focus trap applies |
| Permanently-delete dialog | `Tab` order: export links (informational, focusable for a screen-reader user to actually reach the download) → checkbox → typed-confirmation field → Cancel → Permanently delete. `Esc` inert once submitting. |
| Download an export bundle | `Tab` to the link, `Enter` — a real `<a href>` to the signed URL, not a JS-only action, so it also works for a screen reader's "open in new tab" |

## Accessibility notes

- The stepper's `done`/`current`/`upcoming` states (`components.md` §13) carry text labels, not just the checkmark/dot glyph — "Suspended," "Exported," "Pending deletion," "Deleted" are always present as real text, so the state is legible without color or shape.
- The two Stage-3 panels are structurally separate `<section>`s with their own headings ("Cancel deletion" / `tenants.dangerZoneHeading`), so a screen-reader user navigating by heading encounters them as two distinct destinations, matching the sighted "never in the same button row" rule.
- The permanent-delete dialog's three gates are each announced as they become satisfied (`aria-live="polite"` on a small status line: "2 of 3 confirmed"), so an operator using a screen reader gets the same "how much further" feedback a sighted operator gets from watching the submit button's disabled state.
- The deletion certificate (Stage 4) is ordinary page content, not an alert — it's the page's steady-state content once reached, not an interruption.

**Named against WCAG 2.2 AA, per criterion, rather than asserted:**

| SC | How this screen meets it |
|---|---|
| 1.3.1 Info and Relationships | The vertical stepper's four stages are real headed content in document order, not an image or a canvas rendering of a progress bar; "Cancel deletion" and "Permanently delete…" are two separate `<section>`s. |
| 1.4.1 Use of Color | The stepper never relies on the connecting line's color alone — every stage carries a text label; the Danger zone panel's red border is reinforced by its own "Danger zone" heading text, not color alone. |
| 2.1.1 Keyboard | Both export download links are real `<a href>` elements, reachable and activatable by keyboard, per "Keyboard" above — never a click-only affordance. |
| 2.4.6 Headings and Labels | Every stage and both Stage-3 panels have a real, specific heading ("Suspended," "Cancel deletion," "Danger zone") rather than a generic one. |
| 3.3.4 Error Prevention (this SC is written for legal/financial commitments, and this screen's final step is exactly that class of commitment) | The permanent-delete action requires review (the re-shown export), confirmation (the checkbox), and correction opportunity (the typed key, rejecting a near-miss) before it submits — the three-gate design directly implements this criterion's own three suggested mechanisms, not by coincidence. |
| 4.1.2 Name, Role, Value | `aria-disabled` (never native `disabled`) on "Permanently delete…" before eligibility and before all three gates are met, so its reason stays reachable — same rule as the other two specs in this set. |
| 4.1.3 Status Messages | The three-gate status line and the identity/eligibility state both update through a polite live region, not a visual-only change. |

## Resource keys

New prefix `tenants.offboarding.*`.

| Key | en | sv-SE | Where |
|---|---|---|---|
| `tenants.offboarding.pageTitle` | Offboarding {tenant} · Aurora ERP | Avveckling av {tenant} · Aurora ERP | `<title>` |
| `tenants.offboarding.heading` | Offboarding {tenant} | Avveckling av {tenant} | `<h1>` |
| `tenants.offboarding.intro` | Offboarding suspends this tenant (read-only, reversible), then produces an export, then a 30-day reversible pending-deletion window, then permanent deletion. Nothing here is irreversible until the final step. | Avveckling stänger av klienten (skrivskyddad, återställningsbar), skapar sedan en export, därefter ett 30-dagars återställningsbart väntefönster, och slutligen permanent radering. Inget här är oåterkalleligt förrän det sista steget. | Stage 0 |
| `tenants.offboarding.beginAction` | Suspend {tenant} to begin | Stäng av {tenant} för att börja | Stage 0 action |
| `tenants.offboarding.stageSuspended` | Suspended | Avstängd | Stepper stage 1 label |
| `tenants.offboarding.stageExported` | Exported | Exporterad | Stepper stage 2 label |
| `tenants.offboarding.stagePendingDeletion` | Pending deletion | Väntar på radering | Stepper stage 3 label |
| `tenants.offboarding.stageDeleted` | Deleted | Raderad | Stepper stage 4 label |
| `tenants.offboarding.resume` | Resume tenant | Återuppta klient | Stage 1 action |
| `tenants.offboarding.startExport` | Start export → | Starta export → | Stage 1 action |
| `tenants.offboarding.dumpBundle` | Portability dump (.pgdump) | Portabilitetsdump (.pgdump) | Stage 2 |
| `tenants.offboarding.openFormatBundle` | Open-format bundle (CSV/JSON + interchange) | Öppet format-paket (CSV/JSON + utbytesformat) | Stage 2 |
| `tenants.offboarding.expiresOn` | Expires {date} | Går ut {date} | Stage 2 |
| `tenants.offboarding.expired` | Expired | Har gått ut | Stage 2 |
| `tenants.offboarding.regenerate` | Regenerate | Skapa på nytt | Stage 2 |
| `tenants.offboarding.continueAction` | Continue to pending deletion → | Fortsätt till väntande radering → | Stage 2 action |
| `tenants.offboarding.continueDisabledReason` | Available once both exports complete | Tillgänglig när båda exporterna är klara | Stage 2 disabled reason |
| `tenants.offboarding.daysRemaining` | {days} days remaining | {days} dagar kvar | Stage 3 header |
| `tenants.offboarding.cancelDeletion` | Cancel deletion | Avbryt radering | Stage 3 safe action |
| `tenants.offboarding.cancelDeletionBody` | Restores this tenant to Active immediately. | Återställer klienten till Aktiv omedelbart. | Stage 3 |
| `tenants.offboarding.permanentDeleteAction` | Permanently delete… | Radera permanent… | Stage 3 danger action |
| `tenants.offboarding.notYetEligible` | Not available until {date} | Inte tillgänglig förrän {date} | Stage 3 disabled reason |
| `tenants.offboarding.verifiedExportCheckbox` | I have verified the export at {timestamp} is complete and retrievable. | Jag har verifierat att exporten från {timestamp} är fullständig och hämtningsbar. | Delete dialog |
| `tenants.offboarding.exportExpiredBlock` | The export has expired. Regenerate it before deleting. | Exporten har gått ut. Skapa den på nytt innan radering. | Delete dialog |
| `tenants.offboarding.typeToConfirm` | Type {key} to confirm | Skriv {key} för att bekräfta | Delete dialog |
| `tenants.offboarding.confirmDelete` | Permanently delete | Radera permanent | Delete dialog submit |
| `tenants.offboarding.gatesStatus` | {satisfied} of 3 confirmed | {satisfied} av 3 bekräftade | Delete dialog live status |
| `tenants.offboarding.successToast` | {tenant} permanently deleted. | {tenant} permanent raderad. | Toast |
| `tenants.offboarding.certificateHeading` | Deletion certificate | Raderingsintyg | Stage 4 |
| `tenants.offboarding.certificateBody` | Deleted {date} by {actor}. Backups expire {backupExpiry}. Recorded in the platform audit trail. | Raderad {date} av {actor}. Säkerhetskopior går ut {backupExpiry}. Registrerat i plattformens granskningslogg. | Stage 4 |
| `tenants.offboarding.disabledReasonManage` | Requires the {permission} permission | Kräver behörigheten {permission} | Missing-permission state |
| `tenants.offboarding.concurrentChangeBanner` | This tenant's state changed. Reloading… | Klientens status har ändrats. Läser in på nytt… | Concurrent-change state |
| `tenants.offboarding.resumeBody` | Restores {tenant} to Active immediately. | Återställer {tenant} till Aktiv omedelbart. | Stage 1 "Resume tenant" confirmation body — added this round; see Design-system findings |
| `tenants.offboarding.startExportBody` | This begins producing an export. The tenant stays read-only. | Det här startar en export. Klienten förblir skrivskyddad. | Stage 1 "Start export" confirmation body — added this round |
| `tenants.offboarding.bothBundlesReady` | Both bundles ready | Båda paketen klara | Stepper's "done" summary for Stage 2 — added this round |

## Acceptance criteria

**24 machine-checkable, 4 human.**

**Stage gating**

1. **[M]** "Continue to pending deletion" is `aria-disabled` whenever either export bundle is not `ready`, and enabled only when both are.
2. **[M]** "Permanently delete…" is `aria-disabled` whenever the current date is before `deletion_due_at`, in every tested timezone offset, and only becomes enabled at or after it.
3. **[M]** "Cancel deletion" is present and enabled throughout the entire `PendingDeletion` stage, both before and after `deletion_due_at` passes, until the tenant is actually `Deleted`.
4. **[M]** No control exists anywhere on this page that transitions a tenant directly from `Suspended` or `Exporting` to `Deleted`, skipping `PendingDeletion` — every path passes through it.
5. **[M]** The Stage-0 confirmation for beginning offboarding does **not** require typing the tenant's name/key (it is a normal confirmation, per components.md §14's non-escalated case) — distinguishing it from the two typed-confirmation gates elsewhere on this page.
6. **[M]** "Continue to pending deletion" and "Permanently delete…" are the *only* two controls on this entire page that are ever `aria-disabled` rather than absent for a state-related reason, and both reasons name a same-state, self-resolving condition (a job finishing; a date arriving) — never a different `TenantState`'s action rendered disabled. *This is the criterion that makes the disabled-vs-absent rule's stated exception decidable rather than asserted.*

**The permanent-delete dialog**

7. **[M]** Opening the dialog performs a first identity-check request before any of the three gates below are reachable; the gates' container is absent (not merely disabled) until that first check resolves, and a mismatch on it leaves the gates permanently unreachable for that dialog session.
8. **[M]** Clicking "Permanently delete" triggers a **second**, distinct identity-check request, separate from the first, before the destroy command is sent — the guard runs at act time, not only at render time. *Fails if only one identity-check request is ever observed across opening the dialog and submitting it — this is the same criterion `SPEC-003-tenant-detail.md` states for its own destroy dialog, restated here because this dialog needed it and did not have it.*
9. **[M]** If the second check disagrees with the first (simulated by changing the tenant's identity stamp between dialog-open and submit), no destroy command is sent, the three already-satisfied gates are discarded (a reopened dialog re-earns all three), and the dialog shows `identityMismatch`.
10. **[M]** The submit button is `aria-disabled` unless all three gates (export retrievable, checkbox checked, typed key exact match) are simultaneously satisfied **and** the first identity check has confirmed.
11. **[M]** If either export bundle's signed link has expired at dialog-open time, the dialog renders `exportExpiredBlock` and no combination of the other two gates enables submission.
12. **[M]** The checkbox defaults unchecked on every dialog open, including a reopen after a cancelled attempt — it is never remembered as pre-checked.
13. **[M]** Typed-confirmation matching is case-sensitive and rejects a trimmed/partial match.
14. **[M]** A dropped circuit between submit and response renders the "outcome unknown" pattern (no automatic resend; the operator is told to reload and check state) rather than either a false success or a false failure.
15. **[M]** On success, the page transitions to Stage 4 and both a toast and the deletion certificate reflect the same actor and timestamp, and both identity-check results (first and second) are present in the audit entry.

**Visual and structural separation**

16. **[M]** "Cancel deletion" and "Permanently delete…" render in two separate DOM sections with distinct accessible names/headings — never as two buttons in one shared container or button row.
17. **[M]** The Danger zone panel (containing "Permanently delete…") is styled with the `danger` border treatment defined in `components.md` §14; the "Cancel deletion" panel is not.
18. **[M]** The deletion-due countdown renders as the stage's own header content, not nested inside a generic field list.

**Permission**

19. **[M]** Lacking `platform.tenant.manage` disables every stage-advancing action (begin, resume, start export, continue, cancel deletion) with a reachable reason, while the stepper's read-only content (dates, download links, certificate) remains fully visible.
20. **[M]** Lacking `platform.tenant.destroy` alone disables only "Permanently delete…"; every other action on the page (including "Cancel deletion") functions normally.

**Localisation and access**

21. **[M]** Every user-facing string on this page — visible text and assistive-only text alike — resolves through the resource layer under a pseudo-locale marker test, except the tenant key. *Widened from "every visible string" for the same reason `SPEC-003-tenant-list.md` widened its equivalent criterion: a visible-only population cannot find a hard-coded `aria-label`, and did not, elsewhere in this design system's first draft (see that spec's Design-system findings). This page's own hard-coded strings found and fixed this round — `resumeBody`, `startExportBody`, `bothBundlesReady` — were all visible text, so the population gap did not cause them, but the criterion is worded to the full population regardless, on the same reasoning.*
22. **[M]** `{days remaining}`, both bundle expiry dates and the deletion-due date all render through the shared locale formatters — verified by asserting digit grouping/date order differs between two tested locales while the underlying value does not.
23. **[M]** axe-core reports zero violations on Stage 1, Stage 2 (both bundles ready), Stage 3 (before and after eligibility), the open permanent-delete dialog, and Stage 4.
24. **[M]** Every control on every stage is reachable and operable by keyboard alone in visual order, including both export download links.

**Human pass**

25. **[H]** Screen-reader pass: the three-gate status line in the permanent-delete dialog is heard updating live as each gate is satisfied; the two Stage-3 panels are announced as clearly separate destinations, not one merged region.
26. **[H]** An operator unfamiliar with this spec, asked "which of these two buttons is safe to click without reading further," correctly picks "Cancel deletion" every time, on both a sighted pass and a colorblind-simulation pass.
27. **[H]** Dark and light themes both read correctly, including the Danger zone panel's border against `surface-raised` (the same pairing `SPEC-002-company-create.md` finding 2 already flagged as the system's tightest contrast margin — re-verified this round; see "Design-system findings" / the `surface-raised` audit below).
28. **[H]** With every en string doubled, the permanent-delete dialog's three gates and its action row remain fully readable without truncation on a 440px-wide dialog (per `SPEC-002-company-create.md`'s own pseudo-locale layout budget).

## Design-system findings

**1. The identity-stamp re-check was missing from this dialog entirely.** The prior round shipped this page's permanent-delete dialog with no re-check at all, while `SPEC-003-tenant-detail.md`'s `ProvisioningFailed` destroy dialog already had one. Fixed: a first check gates the three gates' visibility (matching the detail dialog), and a **second, independent** check runs again at submit, immediately before the destroy command — see "The permanent-delete confirmation" and "Detection, not prevention" above, and the matching fix now also applied to the detail spec's own dialog for consistency (it had a check, but only ever ran once, at open).

**2. The disabled-vs-absent rule had two undeclared exceptions in this file, while claiming to have one and while the detail spec stated no exceptions at all.** "Continue to pending deletion" (Stage 2) and "Permanently delete…" (Stage 3) are both disabled-with-reason for a same-state, self-resolving condition, not a permission gap — the only category `SPEC-003-tenant-detail.md`'s original wording allowed. Both specs now state the same three-way rule (cross-state: absent, always; permission gap: disabled; same-state resolves-on-its-own: disabled), and this file's own text at both sites now says so instead of one of them claiming to be singular.

**3. Three hard-coded English strings, found on the same re-scan that found `SPEC-003-tenant-detail.md`'s 24.** `resumeBody`, `startExportBody` (both confirmation-dialog bodies, `openConfirm`) and `bothBundlesReady` (the stepper's Stage-2 "done" summary) bypassed the resource layer entirely. All three now have keys (table above).

**4. `tokens.json`'s contrast audit had zero `surface-raised` pairs — raised once before, in `SPEC-002-company-create.md` finding 2, and still open.** This screen's Danger zone panel and its permanent-delete dialog are exactly the surface this gap was always going to matter on first. Measured this round (`tokens.build.py`, re-run, real output): `accent-solid`/`danger-solid` fill against bare `surface-raised` fail at 2.65:1 / 2.69:1 in dark theme (below the 3.0:1 SC 1.4.11 needs); the same two fills against `surface-sunken` pass at 3.46:1 / 3.52:1. Fixed at the component level, not just measured: `app.css`'s `.dialog .actions` now seats every dialog's button row on `surface-sunken`, full-bleed to the dialog's edges — this applies to every dialog in every prototype, not only this screen's, since it is one CSS rule shared by all of them. Full numbers, both themes, in `tokens.md`'s audit tables. **Also found while fixing this:** `tokens.build.py` wrote its audit output to a path inside an unrelated temp directory instead of `docs/design/tokens.json`, so re-running it — as `tokens.json`'s own header instructs after any edit — never actually updated the checked-in file; and `tokens.css`'s header comment named a "`gen_css` step" that does not exist anywhere in this repo. Both corrected; see `tokens.md` and `tokens.css` for the detail.

## To route (outside `docs/design/`)

- **architect:** is `Exporting` reversible back to `Suspended`/`Active`, or does an interrupted export leave the tenant stuck? ADR-0007 §11.4 doesn't say. This spec deliberately does not offer a reverse action from `Exporting` rather than guess.
- **architect:** confirm the final `DROP DATABASE` at Stage 3→4 is genuinely operator-triggered (this spec's whole design assumes it is, per the task brief's reading of "never automatic") and not a scheduled job that fires the moment `deletion_due_at` passes — if it's the latter, this entire stage's design needs to change from "a button becomes available" to "a countdown to an automatic event with a last-chance cancel," which is a materially different (and arguably less safe) design.
- **project-manager:** confirm the 30-day grace period is in fact fixed/non-configurable per tenant or plan — this spec assumes a single constant, matching ADR-0007 §11.4's literal "30-day grace" wording.
- **architect:** the "Detection, not prevention" residual above (the identity check and the destroy command cannot be one atomic operation because PostgreSQL will not rename/drop a database it is connected to) belongs in an architecture decision, not only in a design spec's prose. This document states the residual honestly pending that; it does not resolve it.
- **orchestrator / a developer:** `prototypes/tokens.css` is hand-synchronized from `tokens.json`, not generated by any real tool — its header previously claimed a "`gen_css` step" that doesn't exist. The two files' values still agree today (checked this round, both by eye and by confirming every value this task added to `tokens.json` was already present in `tokens.css` unchanged), but nothing prevents them drifting the next time either is edited alone. Worth a small tooling task: a script that emits `:root { --color-...: ...; }` directly from `tokens.json`, so agreement is enforced rather than hoped for.

## What we borrowed and why

The asymmetric-friction pairing — a one-click safe reversal beside a maximally-gated irreversible action, spatially separated rather than merely color-coded — is the same shape GitHub uses for "Cancel" versus "Delete this repository" (type-the-name-to-confirm, disabled until exact match) and AWS uses for "Stop instance" versus its account-closure flow (multi-step, re-shown consequences, time-delayed). The export-must-be-currently-retrievable gate on the delete dialog borrows from how reputable backup/export tools (e.g., a cloud provider's "export your data before we delete your account" flow) refuse to let a stale, possibly-expired export stand in for a real, checkable one at the moment it matters most.
