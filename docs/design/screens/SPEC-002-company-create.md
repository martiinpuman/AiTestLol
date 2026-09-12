# Screen spec — Company: create

Status: ready · Author: ui-designer · Date: 2026-09-12
Backlog: `DESIGN-001` · Unblocks: `B-15.3`
Product spec: `../../product/specs/SPEC-002-company-create-and-list.md` (BR-2, BR-3, AC-1, AC-2, AC-3, AC-10, and its **UX reference** section)
Architecture: `../../decisions/ADR-0010-authorization-roles-and-permissions.md`, `../../decisions/ADR-0013` (Problem Details, no `Idempotency-Key` here)
Prototype: `../prototypes/companies-list.html` — the create dialog is part of that file; open it and press `+ New company`
Components referenced: Buttons (§1), Text input (§2), Forms with inline validation (§9), Dialogs (§14), Toasts (§15), Empty states (§17), App shell (`app-shell.md`, reconnection banner)

**This document plus `SPEC-002-companies-list.md` (amended on the same date, see "What changed in the list spec") is the whole of `DESIGN-001`.** The list already had a spec and a prototype; rather than write a second, competing answer, this spec covers the create form and states the small number of corrections it forces on the list.

## Scope

**In:** the single create field and its validation; the two list columns; the list's empty, loading and error states; the permission-gated state; focus order; what a screen reader announces; what happens over a SignalR circuit.

**Out, per SPEC-002's own non-goals:** the full data grid (`components.md` §8 — `GRID-001`/`GRID-002`) and the tenant/company switcher's real switching behaviour (Milestone 2). Editing, deactivating and every Company attribute beyond `name` are Milestone 2. Nothing below should be read as a down payment on any of them.

## Purpose

Turn a permission a user holds into a record they can see, in the fewest possible keystrokes, and be unambiguous about whether it worked. This is the first write path in the product, so its failure modes — not its happy path — are what this spec is mostly about.

## User and task flow

A signed-in user in one tenant holding the Company-management permission (SPEC-002 BR-3).

1. From **Companies** (the list), they activate `+ New company`.
2. A dialog opens with one field, already focused.
3. They type a name and press `Enter` (or activate **Create company**).
4. The button enters its loading state for the round trip.
5. **Success:** the dialog closes, focus returns to `+ New company`, a toast names what was created, and the new row is in the list — no navigation, no page reload (SPEC-002 AC-10).
6. **Validation failure:** focus lands on the field with the reason attached to it; nothing was sent to the server.
7. **Refused or failed:** the dialog stays open with the typed name intact and a banner says why.
8. **Circuit dropped mid-submit:** see "The circuit" below. The user is told the outcome is unknown and sent to the list to check. Nothing is retried for them.

## Layout

```
  ┌─────────────────────────────────────────────┐  ← overlay scrim (`overlay`)
  │  ┌───────────────────────────────────────┐  │
  │  │ New company                           │  │  h2, radius-lg, elevation-4
  │  │                                       │  │  surface-raised
  │  │ ┌───────────────────────────────────┐ │  │
  │  │ │ (banner region — hidden by       │ │  │  role="alert", tabindex="-1"
  │  │ │  default; 403 / 500 / unknown)   │ │  │
  │  │ └───────────────────────────────────┘ │  │
  │  │                                       │  │
  │  │ Company name (required)               │  │  label, always visible
  │  │ ┌───────────────────────────────────┐ │  │
  │  │ │                                   │ │  │  Text input (§2), 32px
  │  │ └───────────────────────────────────┘ │  │
  │  │ Up to 200 characters.      14 left    │  │  hint · counter (≥160 only)
  │  │ Enter a company name.                 │  │  error-msg (hidden unless set)
  │  │                                       │  │
  │  │                  [ Cancel ] [ Create  │  │  Cancel = secondary,
  │  │                             company ] │  │  Create = primary, rightmost
  │  └───────────────────────────────────────┘  │
  └─────────────────────────────────────────────┘
```

Dialog width `440px`, capped at `90vw`. All spacing, radius, elevation and colour from `tokens.json`; no value in the prototype is a literal.

### Why a dialog and not a route

One field does not need a page. A dialog keeps the list mounted behind it, which is what lets the new row appear in place (AC-10) instead of via a navigation, and it inherits the return-focus and Esc contracts from `components.md` §14 rather than needing a bespoke back action, breadcrumb and unsaved-changes guard for a single input.

The consequence, stated rather than hidden: **the dialog has no URL.** Reloading the browser while it is open lands on `/companies` with the dialog closed and the typed name gone. That is acceptable for one field and one required value; it would not be for a Milestone 2 Company form with a dozen fields, and whoever writes that spec should revisit this decision rather than inherit it.

## The field

| | |
|---|---|
| Label | `companies.create.nameLabel` — "Company name", always visible above the control. Never a placeholder-as-label (`components.md` §2). |
| Required marker | The word `(required)` appended to the label in normal weight, not an asterisk — an asterisk needs a legend to mean anything and is announced as "star". |
| Control | Text input (`components.md` §2), `type="text"`, `autocomplete="off"`, `spellcheck="false"` (a company name is not English prose and red squiggles under a correct legal name are noise). |
| Hint | `companies.create.nameHint` — "Up to 200 characters." Present from the start; it is the rule, not an error. |
| Counter | `companies.create.counter` — shown **only** from 160 characters (`components.md` §2: "within 20% of the limit"). Tabular numerals so the digits do not jitter as it counts down. |
| No `maxlength` | Deliberate. A hard cap silently drops the tail of a pasted 250-character name and makes the over-length message unreachable — a validation rule that cannot fire is not a check. The field accepts the over-long value and says so. |

### Validation

Applied in this order, client-side first (in-circuit) and identically on the server (SPEC-002 AC-3). The client check is a courtesy; the server check is the rule.

| Rule | Condition | Message key | en |
|---|---|---|---|
| Required | Trimmed length is 0 (so `"   "` is empty, not three characters — BR-2) | `companies.create.errorRequired` | Enter a company name. |
| Length | Trimmed length > 200 | `companies.create.errorTooLong` | Use 200 characters or fewer — this name is {count}. |

**Timing** (`components.md` §9): on blur *if the field has been edited*, and on submit. An untouched empty field shows nothing — tabbing from the autofocused field to Cancel must not accuse the user of anything. Once a field is marked invalid it re-validates on every keystroke, so the error clears the moment it is actually fixed.

**No uniqueness check.** SPEC-002 gives Company name no uniqueness rule, so two companies may share a name and the form never says "that name is taken". This is load-bearing, not an omission: a duplicate-name message would tell the caller that a record they may not be allowed to see exists. If uniqueness is ever added, it must be scoped to what the caller can already read.

**Counting characters.** The 200 limit is counted in UTF-16 code units, matching .NET's `string.Length`, so the browser counter and the server validator agree for a name containing a non-BMP character. A test that submits a 200-code-unit name containing one astral character and expects acceptance is the check; without it the two counters can drift by exactly the amount nobody notices.

**One enforcement point.** Per ADR-0010 rule 5 and SPEC-002's API notes, the UI action and `POST /api/v1/companies` go through the same application-service command. The UI is not allowed to be the only thing enforcing any rule on this page.

## Permission, and why this screen is not an existence oracle

Two permissions are involved. SPEC-002 BR-3 names the create permission (`organization.company.manage`, exact constant still the implementing developer's to finalise). `SPEC-002-companies-list.md` proposes a lower-privilege `organization.company.view` for the list query, since ADR-0010 rule 5 requires a permission on queries too. **Both names remain proposals for B-15.2's developer to confirm or rename**; nothing in this spec depends on the spelling.

Three states follow, and the third is the one with a rule attached.

1. **Holds `.view` and `.manage`.** Everything above.
2. **Holds `.view`, not `.manage`.** The list renders in full. `+ New company` is present, focusable, and carries `aria-disabled="true"` with `aria-describedby` pointing at a reachable reason (`components.md` §1's correction — never the native `disabled` attribute, which would remove it from the tab order and put the reason out of reach). The reason **names the permission**, and that is safe precisely because this user can already see the surface it gates: it tells them nothing about data.
3. **Holds neither — or the resource is not served for this tenant at all.** One state, `companies.unavailableHeading` + `companies.unavailableBody`, and the two causes are **deliberately indistinguishable**:

> **Rule.** The rendering of `/companies` for a caller without the list permission must be identical to its rendering for a caller in a tenant where the resource does not exist: same heading, same body, no table, no row count, no footer, no create action, and **no permission name**. Whatever HTTP status the server chose, the UI collapses it to this one state.

A permission name in this copy would break the sameness — a tenant that never had the resource has no permission to name — and the difference is exactly what turns a screen into an oracle for "does this thing exist here?". This is why the previous copy ("Ask a tenant administrator for the `organization.company.view` permission") is replaced. Note this rule concerns the **UI**; whether the API should likewise collapse `403` and `404` on this route is a question for the architect (see "To route").

**The footer count is part of this.** `Showing {shown} of {total}` must report the caller's *scoped* query (BR-4), never the tenant-wide row count. A total larger than the rows shown would state the existence of companies the caller is not allowed to see, which is the same leak in a smaller font.

## States

| State | What the user sees |
|---|---|
| **List — default** | Two columns, Name and Created. Nothing else; no sort affordance, no checkbox column, no toolbar (see `SPEC-002-companies-list.md`, "Why not the Data grid"). |
| **List — loading** | Skeleton rows in the table body; the column headers stay put so nothing shifts when data arrives. The `<caption>` reads `companies.loadingLabel` so a screen-reader user hears what is loading, not an untitled table. |
| **List — empty** | Framed as an anomaly, not an invitation: a provisioned tenant always has one default company (SPEC-001 BR-3), so zero rows means a guarantee was violated. No "create your first company" call to action here — see the list spec for the full reasoning. |
| **List — error** | Heading, one line, and a Retry button. Visually distinct from empty (`components.md` §17). |
| **List — unavailable** | The single state above. |
| **Dialog — default** | Field empty and focused, no error, no counter, submit enabled. |
| **Dialog — validating** | `danger` border on the field, message below it, `aria-invalid="true"`. |
| **Dialog — submitting** | Button label replaced by a fixed-width spinner (button keeps its width), `aria-disabled="true"` on both buttons, the input `readonly` (not `disabled` — the value stays visible and focus is not thrown to `<body>`), `Esc` inert. Minimum visible 150ms so it cannot flash. |
| **Dialog — refused (403)** | Banner `companies.create.errorForbidden`, typed name kept, controls restored. The list behind it drops to state 2 — the permission really is gone, so the action must stop being offered. |
| **Dialog — failed (500)** | Banner `companies.create.errorServer`, typed name kept, controls restored. |
| **Dialog — outcome unknown** | Banner `companies.create.errorUnknownOutcome`. See below. |
| **Long values** | A 200-character name wraps in the Name cell rather than truncating — no ellipsis, so no hover-only tooltip that a keyboard user cannot reach. In the input it scrolls internally, as native text inputs do. |

## The circuit (Blazor Server, `InteractiveServer`)

Every one of these is a SignalR round trip; none of them is optimistic.

- **In flight.** The submitting state above. Nothing in the list changes until the server answers. The button is the progress indicator; there is no shell-level banner for an ordinary slow save (`app-shell.md`: a slow round trip is not a dropped circuit and must not look like one).
- **Circuit drops mid-submit.** The shell's reconnection banner appears and freezes editable controls, the dialog included; the typed name stays on screen. "Frozen" here means `readonly` plus `aria-disabled`, **not** the native `disabled` attribute — see finding 8. The command has left the browser and no answer came back, so **its outcome is genuinely unknown**.
- **Circuit returns.** `app-shell.md` requires any action pending at the drop to be resolved one way or the other, never left in limbo. SPEC-002 deliberately requires no `Idempotency-Key` on this command, so the UI has no safe way to retry: a second attempt could create a second company. It therefore resolves the action as *unknown* and says so — `companies.create.errorUnknownOutcome`, which tells the user to close the dialog and check the list before trying again. **The UI must not re-send the command automatically.**
- **Circuit permanently lost.** The shell escalates to the `danger` banner with "Reload page"; the dialog's contents are gone after the reload. Stated plainly because it is true, not because it is good.

SPEC-002's stated reason for omitting `Idempotency-Key` is that the button's loading state handles double submission. That is correct for a double-click and not correct for a circuit drop, which is the case above. Routed to the project-manager rather than changed here.

## Keyboard and focus order

Tab order on the list page, in DOM order:

1. **Skip to main content** (visually hidden until focused) → `#main`
2. Toggle navigation · 3. Tenant/company switcher · 4. Global search · 5. Commands (⌘K) · 6. Notifications · 7. User menu
8. Nav rail items (skipped by 1)
9. `+ New company`
10. Retry (error state only) / Prev–Next (many-rows state only)

The table itself has no focusable descendants: this screen has no row-level action, so there is no cell cursor to specify. That is a property of *this* list, not a relaxation of `components.md` §8's keyboard model for the grid.

Inside the dialog:

| Action | Key |
|---|---|
| Open the dialog | `Enter`/`Space` on `+ New company` |
| Initial focus | `#company-name`, automatically, on open |
| Move between Company name → Cancel → Create company | `Tab`; `Shift+Tab` reverses. Focus **cycles inside the dialog** and never reaches the page behind the scrim. |
| Submit from the field | `Enter` |
| Cancel | `Esc`, or `Enter`/`Space` on Cancel — **`Esc` is inert between the submit click and the server's answer** (`components.md` §14: a dialog can be dismissed while collecting input, never while a commit is in flight) |
| Return | On close by any route — Cancel, `Esc`, or a successful create — focus is on `+ New company` |

No new keyboard shortcut is introduced. `Ctrl/Cmd+K` still opens the command palette from the list.

## Accessibility

WCAG 2.2 Level AA. Each row names the success criterion it serves so a reviewer can check it instead of trusting it.

| Element | Accessible name (source) | Role | Keyboard | Focus-visible | SC |
|---|---|---|---|---|---|
| `+ New company` | Its own text, `companies.newButton` | `button`, plus `aria-haspopup="dialog"` | `Tab`, `Enter`/`Space` | 2px `border-focus` ring, 2px offset | 4.1.2, 2.1.1, 2.4.7, 2.4.6 |
| `+ New company`, no `.manage` | Same, plus the reason via `aria-describedby` | `button` with `aria-disabled="true"` — **stays in the tab order** | `Tab` reaches it; activation no-ops | Same ring | 4.1.2, 2.1.1, 3.3.2 |
| Create dialog | `aria-labelledby` → the `<h2>` (`companies.create.dialogTitle`) | `dialog`, `aria-modal="true"` | Focus trapped; `Esc` closes when idle | — | 4.1.2, 2.4.3, 2.1.2 |
| Company name | `<label for>` → `#company-name`; exactly one label, no placeholder-as-label | `textbox`, `required`; `aria-invalid="true"` when invalid | `Tab`; `Enter` submits | Ring + `border-focus` border | 1.3.1, 3.3.2, 2.5.3, 4.1.2 |
| Hint / counter / error | Referenced by the field's `aria-describedby`; when invalid the **error id comes first**, so "what is wrong" is read before "what the rule is" | — | Not focusable | — | 3.3.1, 3.3.3, 1.3.1 |
| Dialog banner (403/500/unknown) | Its own text | `alert`, `tabindex="-1"`, focused on arrival | Not in the tab order when hidden | Ring when focused | 4.1.3, 3.3.1, 2.4.3 |
| Create company | Its own text; while submitting, `aria-label` = `companies.create.submitting` (the visible label is a spinner marked `aria-hidden`) | `button`, `aria-disabled` while in flight | `Enter`/`Space` | Ring | 4.1.2, 4.1.3, 2.4.7 |
| Cancel | Its own text | `button` | `Enter`/`Space`, or `Esc` | Ring | 4.1.2, 2.1.1 |
| Success toast | Its own text; also written to a polite live region | `status` region | Not focus-stealing | — | 4.1.3 |
| Companies table | Visually hidden `<caption>` (`companies.tableCaption`, naming the tenant) | `table` with `<th scope="col">` | Not focusable | — | 1.3.1 |
| Unavailable state | Heading + paragraph, read as ordinary page content — **not** `role="alert"`: it is this user's steady state, not an interruption | — | Reachable by heading navigation | — | 1.3.1, 2.4.6 |
| Skip to main content | `shell.skipToContent` | `link`, first focusable element | `Tab` then `Enter` | Becomes visible on focus | 2.4.1, 2.4.7 |

**What a screen reader announces**

- *On a failed submit:* focus moves to the name field, which is announced as `"Company name, edit, invalid entry, Enter a company name. Up to 200 characters."` — label, then the invalid state from `aria-invalid`, then the description in `aria-describedby` order. The error element is deliberately **not** a live region: it updates on every keystroke during live re-validation, and a live region there would read the message again on each one.
- *On a server refusal or failure:* the banner's `role="alert"` announces it, and focus moves to it, so the user's next `Tab` starts from the message rather than from wherever they were.
- *On a successful create:* the dialog closes, focus returns to `+ New company` (announced as `"New company, button"`), and the polite live region reads `"Nordwind Trading AB created."` The new row is **not** separately announced — one event, one announcement.

**Colour is never the only signal.** The invalid field has a `danger` border *and* a message *and* `aria-invalid`; the banner differs from a success toast in wording, not only in colour (1.4.1).

**Motion.** `prefers-reduced-motion: reduce` flattens the skeleton shimmer to a static dimmed block and halves the spinner's speed rather than stopping it, so a pending state stays perceivable (2.3.1, and `tokens.md`'s motion rule).

**Target size.** Every interactive element on this screen clears SC 2.5.8's 24 × 24 CSS px minimum on its own, without relying on the spacing exception: the primary and secondary buttons and the name input are 32px tall, the top-bar icon buttons 32 × 32, and the smallest control present — the paging buttons in the many-rows state — 26px. (`components.md` §8's 15px grid checkbox and the 23px filter-chip remove button do **not** clear it, but neither appears on this screen; they belong to `GRID-001`/`GRID-002`.)

**Focus not obscured (2.4.11).** The shell's connection banner occupies its own grid row rather than overlaying the content area, so it cannot cover a focused control at the bottom of the page. The prototype's fixed "Prototype controls" panel *can* — it is review scaffolding and does not ship.

## Resource keys

English is the base locale. sv-SE is shown to prove the layer is real; a Country Package adds further locales without touching core code.

**32 `companies.*` keys** govern this screen, plus `shell.skipToContent` — 33 rows below. Of those: **15 new** (`companies.tableCaption` and the 14 `companies.create.*`), **2 replaced** (`unavailable*` for `noAccess*`), **1 changed in text** (`newButtonDisabledReason`), **1 removed** (`newButtonToast`). Four further `shell.*` keys were added for the connection banner (finding 7). The 14 `companies.demo*`/`companies.preview*` keys in `prototypes/i18n.js` are prototype scaffolding and do not ship.

| Key | en | sv-SE | Where |
|---|---|---|---|
| `companies.pageTitle` | Companies — {tenant} · Aurora ERP | Företag — {tenant} · Aurora ERP | `<title>` |
| `companies.breadcrumb` | Master data · Organization | Grunddata · Organisation | Page header |
| `companies.heading` | Companies | Företag | `<h1>` |
| `companies.tableCaption` | Companies in {tenant} | Företag i {tenant} | Hidden `<caption>` **(new)** |
| `companies.colName` | Name | Namn | Column header |
| `companies.colCreated` | Created | Skapat | Column header |
| `companies.footerCountFew` | Showing {shown} of {total} companies | Visar {shown} av {total} företag | List footer |
| `companies.footerCountMany` | Showing {rangeStart}–{rangeEnd} of {total} companies | Visar {rangeStart}–{rangeEnd} av {total} företag | List footer, paged |
| `companies.newButton` | + New company | + Nytt företag | Page header action |
| `companies.newButtonDisabledReason` | Requires the {permission} permission | Kräver behörigheten {permission} | `aria-describedby` on a disabled action **(changed: the constant is now interpolated, never translated)** |
| `companies.loadingLabel` | Loading companies… | Läser in företag… | Loading caption |
| `companies.emptyHeading` | No companies yet | Inga företag än | Empty state |
| `companies.emptyBody` | This shouldn't happen on a provisioned tenant — every tenant starts with one default company. Contact support if you see this. | Det här ska inte hända för en etablerad klient — varje klient startar med ett standardföretag. Kontakta supporten om du ser det här. | Empty state |
| `companies.errorHeading` | We couldn't load your companies | Vi kunde inte läsa in dina företag | Error state |
| `companies.errorBody` | Try again, or contact support if this keeps happening. | Försök igen, eller kontakta supporten om problemet kvarstår. | Error state |
| `companies.retry` | Retry | Försök igen | Error state |
| `companies.unavailableHeading` | Companies isn't available here | Företag är inte tillgängligt här | Unavailable state **(replaces `companies.noAccessHeading`)** |
| `companies.unavailableBody` | If you think this should be available to you, ask a tenant administrator. | Om du tror att det här borde vara tillgängligt för dig, kontakta en administratör. | Unavailable state **(replaces `companies.noAccessBody`)** |
| `companies.create.dialogTitle` | New company | Nytt företag | Dialog `<h2>` **(new)** |
| `companies.create.nameLabel` | Company name | Företagsnamn | Field label **(new)** |
| `companies.create.requiredSuffix` | (required) | (obligatoriskt) | Field label **(new)** |
| `companies.create.nameHint` | Up to 200 characters. | Högst 200 tecken. | Field hint **(new)** |
| `companies.create.counter` | {remaining} characters left | {remaining} tecken kvar | Counter, ≥160 chars **(new)** |
| `companies.create.submit` | Create company | Skapa företag | Primary button **(new)** |
| `companies.create.submitting` | Creating… | Skapar… | Button `aria-label` while in flight **(new)** |
| `companies.create.cancel` | Cancel | Avbryt | Secondary button **(new)** |
| `companies.create.errorRequired` | Enter a company name. | Ange ett företagsnamn. | Field error **(new)** |
| `companies.create.errorTooLong` | Use 200 characters or fewer — this name is {count}. | Använd högst 200 tecken — det här namnet har {count}. | Field error **(new)** |
| `companies.create.errorForbidden` | You don't have permission to create companies. | Du har inte behörighet att skapa företag. | Dialog banner **(new)** |
| `companies.create.errorServer` | We couldn't create the company. Try again. | Vi kunde inte skapa företaget. Försök igen. | Dialog banner **(new)** |
| `companies.create.errorUnknownOutcome` | The connection dropped before we could confirm. Close this and check the list before creating {name} again. | Anslutningen bröts innan vi hann bekräfta. Stäng det här och kontrollera listan innan du skapar {name} igen. | Dialog banner **(new)** |
| `companies.create.successToast` | {name} created. | {name} har skapats. | Toast + live region **(new)** |
| `shell.skipToContent` | Skip to main content | Hoppa till huvudinnehållet | First focusable element **(new, shell-owned)** |

`companies.newButtonToast` is **removed** — it existed only to say the create form was specified elsewhere. It is specified here.

## Locale, formats and pseudo-locale expansion

| Value | Locale-formatted? |
|---|---|
| `Created` column | **Yes** — through the shared date formatter, never a literal pattern. en `09/05/2026`; sv-SE `2026-09-05`. |
| `{shown}`, `{total}`, `{rangeStart}`, `{rangeEnd}` in the footer | **Yes** — through the number formatter, so grouping separators differ (en `1,204`; sv-SE `1 204`). Passing the raw integer into the string would produce identical output in both locales and demonstrate nothing; the prototype does this correctly and comments why. |
| `{count}` in the over-length error | **Yes** — same formatter. |
| `{remaining}` in the counter | **Yes** — same formatter. |
| Company name | **No.** User data, rendered verbatim, never transformed, never truncated, and never interpolated as markup. |
| `{permission}` (`organization.company.manage`) | **No.** An identifier, interpolated into a translated sentence so no translator is ever handed a constant to translate. |
| `{tenant}` | **No.** A tenant's own display name. |

**Pseudo-locale.** Assume any en string can roughly double in length (German and Finnish routinely do; a pseudo-locale that doubles every string is the cheap way to test it). What the layout does:

- The page header is a wrapping flex row: a long heading and a long button label move to two rows instead of colliding.
- Button labels are never truncated and never ellipsised. The dialog's action row wraps to a second row if `Cancel` + `Create company` no longer fit in 440px.
- The `Created` column has a fixed 220px width and the label wraps within it; the Name column takes the rest and wraps.
- The field hint and the counter sit on one row with the counter right-aligned; if the doubled hint no longer fits, they wrap and the counter drops below.
- Nothing on this screen uses `text-overflow: ellipsis`, so there is no case where an expanded string silently loses characters.

## Acceptance criteria

Each is written so a developer can watch it fail. **[M]** = machine-checkable (bUnit / Playwright / axe-core in CI). **[H]** = needs a human pass. **20 machine-checkable, 4 human.** Nothing below says "the form is accessible" or "the screen is localized"; each names an observable that can be absent.

**Dialog mechanics**

1. **[M]** Activating `+ New company` renders an element with `role="dialog"`, `aria-modal="true"`, and `aria-labelledby` resolving to `companies.create.dialogTitle`; focus is on the name field on the first render after open. *Fails if the dialog opens without moving focus.*
2. **[M]** With the dialog open, `Tab` from the last control returns focus to the name field and `Shift+Tab` from the name field reaches the submit button; focus never lands on an element outside the dialog. *Fails if the trap is removed.*
3. **[M]** `Esc` closes the dialog while idle and does **not** close it between the submit activation and the server's answer.
4. **[M]** Closing by any route — Cancel, `Esc`, successful create — leaves focus on `+ New company`. *Fails if focus drops to `<body>`.*

**Validation**

5. **[M]** Submitting an empty name sends no command, sets `aria-invalid="true"`, renders `companies.create.errorRequired`, lists the error element's id **first** in the field's `aria-describedby`, and leaves focus on the field.
6. **[M]** Submitting `"   "` produces exactly the result in (5) — the required error, not a three-character name.
7. **[M]** Entering 201 characters leaves 201 characters in the field (nothing truncated) and, on submit, renders `companies.create.errorTooLong` with `{count}` = 201 formatted per the active locale.
8. **[M]** A 200-code-unit name containing at least one non-BMP character is accepted by both the client counter and the server validator.
9. **[M]** The counter is absent at 159 characters and present at 160. *Fails if it is always shown or never shown.*
10. **[M]** After an invalid submit, editing the field to a valid value clears the error and removes `aria-invalid` without a second submit.
11. **[M]** Blurring the name field before any keystroke shows no error.

**Commit and its failures**

12. **[M]** While the command is in flight the submit button carries `aria-disabled="true"`, still exposes a non-empty accessible name, does not change width, the input is `readonly` and keeps its value, and Cancel does not act.
13. **[M]** On success the dialog closes, a row carrying the **trimmed** name is appended without a page navigation, and the footer count is recomputed from the rendered row count through the locale number formatter. *Fails if the count is a literal.*
14. **[M]** On success a message containing the created name is written to an `aria-live="polite"` region. *Fails if only a visual toast is rendered.*
15. **[M]** On `403` or `500` the dialog stays open, keeps the typed name, renders the matching banner with `role="alert"`, and moves focus to it.
16. **[M]** If the circuit drops between submit and answer, no command is re-sent automatically, and when the circuit returns the dialog shows `companies.create.errorUnknownOutcome` — not a success and not a plain failure.

**Permission and leakage**

17. **[M]** For a caller lacking the list permission and for a caller in a tenant where the resource is not served, `/companies` renders the same heading key, the same body key, no table, no footer count, and no create action. *Fails if either case renders anything the other does not.*
18. **[M]** The footer `{total}` equals the row count of the caller's scoped query. *Fails if a company-scoped assignment yields a tenant-wide total.*

**Localisation and audit**

19. **[M]** Rendered under a pseudo-locale whose every value carries a marker prefix, every visible string on this screen carries the prefix. *Fails for any string that bypassed the resource layer — this is what makes "no hard-coded strings" observable rather than asserted.*
20. **[M]** axe-core reports zero violations on the list default state and on the open dialog.

**Human pass**

21. **[H]** Screen-reader pass (NVDA or VoiceOver): opening the dialog announces its title; a failed submit announces label → invalid → message; a successful create announces the toast once.
22. **[H]** With every en string doubled, no label on this screen is clipped and the dialog's action row wraps rather than overflowing.
23. **[H]** At 400% zoom on a 1280px-wide window the dialog and the list are operable without horizontal scrolling. **This is expected to fail today** — see finding 1 below. It is recorded as a criterion so the failure is visible rather than absent.
24. **[H]** Dark and light themes both read correctly at `compact` and `comfortable` density.

## Design-system findings

Written out rather than designed around, per the project rule that a check is only as good as the last link it follows.

**1. SC 1.4.10 Reflow is not met at the shell level.** `app-shell.md` declares `<834px` unsupported for v1 and `prototypes/app.css` has no breakpoint below 900px. SC 1.4.10 is not about phones: it is triggered by a low-vision user zooming a 1280px desktop window to 400%, which yields a 320 CSS px viewport. Nothing on *this* screen causes it; the shell does. Needs its own task.

**2. Dark-theme primary button inside a dialog fails SC 1.4.11 (measured).** `tokens.md`'s audit covers `accent-solid` against `surface` (3.04:1, already the tightest pair in the system) and against `canvas` — but **no pair in the audit uses `surface-raised`**, which is the background of every dialog, popover and menu. Measured with the same formula `tokens.build.py` uses:

| Pair | Ratio | Required |
|---|---|---|
| dark `accent-solid` `#3565CE` on `surface-raised` `#262B33` | **2.65:1** | 3.0:1 — **fails** |
| dark `border-strong` `#6B7480` on `surface-raised` | 3.01:1 | 3.0:1 — passes by 0.01 |
| dark `text-subtle` `#6B7280` on `surface-raised` | 2.94:1 | (exempt uses only) |

This screen's `Create company` button is the first primary button specified inside a dialog, but sign-in's re-authentication dialog already ships one, so the gap is live today. Two measured candidate fixes: lighten dark `accent-solid` to about `#4571D2` (3.08:1 on raised, 3.54:1 on surface, white label 4.62:1 — a tight window in every direction), **or** seat dialog action rows on `surface-sunken`, which clears 3.46:1 with today's fill and needs no palette change. The second is preferred, but it changes `components.md` §14's dialog anatomy and requires re-running `tokens.build.py` over a new set of `surface-raised` pairs — too much for this row. **Do not read this spec as asserting AA in dark theme until that lands.**

**3. `text-subtle` was carrying real content.** `tokens.md` reserves it for placeholder text and disabled-control labels — the two cases WCAG 1.4.3 exempts — but `app.css` routed `.field .hint` through it (3.50:1 light, 3.38:1 dark), and `companies-list.html` routed its scale note through it. Both are now `text-muted` (6.30:1 / 7.47:1). The same misuse remains in `.nav-section-label`, `.popover .group-label`, `.cmdk .group-label`, `.recent-item .co` and `.cmdk .result .co` — all shell chrome specified elsewhere, all real content, all needing one sweep with a re-audit rather than five ad hoc edits.

**4. No dialog trapped or restored focus.** `components.md` §14 requires both; `app.js`'s `openModal`/`closeModal` did neither, so focus tabbed out of every open dialog into the page behind the scrim and landed on `<body>` on close (SC 2.4.3, 4.1.2). Fixed in `app.js` for every prototype, including sign-in's re-auth dialog and the company-switch confirmation.

**5. No toast reached assistive technology.** `components.md` §15 requires "a screen-reader-only live region announces the message regardless of the visual toast" and no prototype had one (SC 4.1.3). Fixed in `app.js`: polite for success/info, assertive for errors.

**6. No shell screen had a skip link** (SC 2.4.1, Level A) — only `sign-in.html`, which has no nav rail. Its `.skip-link` style is promoted to `app.css` and used here; `app-shell.md` should state it as a shell guarantee.

**7. The connection banner's three strings were hard-coded English** in `app.js`, which `CLAUDE.md` forbids outright. Keyed as `shell.reconnecting`, `shell.connectionLost`, `shell.reloadPage`, `shell.reconnected`.

**8. The reconnection freeze threw focus to `<body>`.** `app-shell.md` says editable controls "become disabled" on a dropped circuit, and `app.js` implemented that with the native `disabled` attribute. A circuit drops precisely when someone is mid-keystroke in a field, and disabling the element that currently has focus moves focus to `<body>` (SC 2.4.3) — the same contradiction `components.md` §1 already resolved for buttons. The freeze now uses `readonly` plus `aria-disabled`, which keeps focus, the caret and the value. `app-shell.md`'s wording should be corrected to match; it currently specifies the mechanism that breaks.

## What changed in the list spec

`SPEC-002-companies-list.md` is amended, not replaced. Four changes, all recorded in its own amendment section:

1. `+ New company` opens this dialog instead of a toast saying the form is specified elsewhere.
2. The "no permission" state becomes the neutral "unavailable" state above, and stops naming a permission.
3. The list table gains a hidden `<caption>` and `scope="col"` headers.
4. The footer count is computed and formatted, not carried as a literal.

## To route (outside `docs/design/`)

- **project-manager:** SPEC-002's justification for omitting `Idempotency-Key` ("handled at the UI layer by the button's loading/disabled state") holds for a double-click and not for a circuit drop mid-submit, where the UI cannot know the outcome. This spec's answer is to tell the user and not retry; if that is not acceptable, the decision to skip idempotency needs revisiting.
- **architect:** should `GET /api/v1/companies` return the same status for "no permission" and "resource not served for this tenant"? The UI collapses them either way; whether the API should is an ADR-0010/ADR-0013 question.
- **project-manager / architect:** `organization.company.view` is still only a proposal (from the list spec) alongside SPEC-002's `organization.company.manage`. B-15.2 should confirm or rename both.
- **backlog:** findings 1, 2 and 3 above each want their own row.

## What we borrowed and why

Quick-create as a single-field dialog over the list it adds to — rather than a route — is how Xero, QuickBooks Online and Linear handle adding one low-cardinality record: the surrounding context stays on screen, so the user sees the thing they made appear where they expected it. The "we could not confirm whether this was created — go and look" resolution after a dropped connection is the pattern Stripe's dashboard and GitHub use for a non-idempotent write whose response was lost: honest uncertainty beats an optimistic success message (which risks a silent duplicate on the user's retry) and beats a plain failure (which is a lie if it actually committed). The single neutral "not available here" state shared between *forbidden* and *does not exist* is the standard anti-enumeration response, the same reasoning `SPEC-001-sign-in.md` applies to credentials, moved one layer up from authentication to authorization.
