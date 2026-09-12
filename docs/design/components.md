# Components — Aurora ERP

Every component below is specified once and used everywhere the same problem occurs (Principle 4: one way to do each thing). States are written out explicitly because "it looks fine in the happy path" is not a spec — screens must be built to their loading, error, empty, disabled and read-only states from the start, not patched in later.

Conventions used throughout this file:
- All colors are token references (`var(--color-*)` from `tokens.json`/`tokens.css`); no raw hex appears in component code.
- "Round trip" means a Blazor Server SignalR message to the server and back. Every state machine below is written assuming that round trip is real, sometimes slow, and sometimes fails outright (circuit drop) — see `app-shell.md` for the reconnection banner that wraps all of this.
- "Per locale / Country Package" means the exact display format is resolved at render time from the tenant's active locale and installed Country Packages, never hard-coded (Principle 6).
- Every focusable element gets the shared focus style: a 2px solid `border-focus` ring, offset 2px from standalone controls (buttons, inputs, cards) or drawn as an inset ring with no offset inside dense contexts (grid cells, menu items) where an offset ring would overlap a neighbor. Focus is always visible; `outline: none` without a replacement is never acceptable.

---

## 1. Buttons

**Variants**: `primary` (accent-solid fill, one per view/section — the default action), `secondary` (outlined, `border-strong` + `text`), `tertiary`/ghost (text-only, for low-emphasis actions inside dense toolbars), `danger` (danger-solid fill, for destructive/irreversible actions), `icon button` (square, icon-only, always carries an accessible name via `aria-label` and a tooltip on hover/focus).

| State | Primary | Secondary | Danger | Notes |
|---|---|---|---|---|
| Default | `accent-solid` fill, `text-on-accent` label | `surface` fill, `border-strong` outline, `text` label | `danger-solid` fill, `text-on-accent` label | |
| Hover | `accent-solid-hover` | `surface-hover` fill | `danger-solid-hover` | Pointer only; no hover state is load-bearing (touch/keyboard users never see it, so hover never carries information that isn't also in another state). |
| Focus | + 2px `border-focus` ring, 2px offset | same | same | Ring shows on keyboard focus and on mouse focus alike — no `:focus-visible`-only suppression, because a mouse user tabbing back into a form is still a keyboard user at that moment. |
| Active/pressed | `accent-solid-active` | `surface-selected` fill | `danger-solid-active` | |
| Disabled | 40% opacity, `text-disabled` label, `cursor: not-allowed` | same pattern | same | **Correction (found while building `SPEC-002-companies-list.md`'s prototype):** a disabled button that must carry a reason reachable on focus cannot also be unfocusable — those two requirements contradict each other for a native `disabled` attribute, which removes the element from the tab order entirely. The correct mechanism is `aria-disabled="true"` (the element stays focusable and in the tab order) plus `aria-describedby` pointing at the reason text, with the click handler itself checking `aria-disabled` and no-op'ing rather than relying on the browser to block the click — never a native `disabled` attribute on any button whose disabled state has a reason a keyboard/screen-reader user needs to discover. A disabled control with no explanation, or one whose explanation only a mouse-hover user can ever reach, is a dead end, not a state. |
| Loading | Label replaced by a fixed-width spinner (button keeps its width so the layout doesn't jump), button disabled for re-clicks | same | same | Enters on click, before the round trip resolves. Minimum visible duration 150ms even if the server answers instantly, so it never flashes. |
| Success flash | Brief (600ms) checkmark swap on the button itself for the specific case of "Save"/"Post" completing, then reverts to default | — | — | Optional; used only for the single most common commit actions where a toast would be redundant with the button itself. |

**Content rule**: destructive and financial-posting buttons restate the target inline (`Post to Nordwind Trading AB`, `Delete 12 selected rows`) per Principle 2 — never a bare verb when the blast radius is more than "undo".

**Keyboard**: `Enter`/`Space` activates. `Tab` order follows visual reading order; primary action is not guaranteed to be first in tab order — it's wherever it visually sits.

---

## 2. Text input

**States**: default, hover (border darkens to `border-strong` if not already), focus (ring + `border-focus` outline), filled, disabled (`surface-sunken` fill, `text-disabled` value, not focusable), read-only (`surface` fill unchanged but no border-strong — a visibly non-editable but still-legible value, distinct from disabled: read-only means "correct but not editable here," disabled means "not applicable/not permitted right now"), error (`danger` border + inline message below, see Forms), loading (rare — used for a field whose value is being fetched/derived server-side; shows a skeleton bar in place of the value), empty (shows placeholder in `text-subtle`).

Label sits above the field, always visible (no placeholder-as-label — placeholder text disappears on input, which fails usability for anyone who pauses mid-task, and fails accessibility guidance outright). Character/byte limits, when they exist, show a counter once the user is within 20% of the limit, not before.

---

## 3. Number input

Right-aligned, tabular numerals (`font-feature-numeric`), formatted with the active locale's thousands/decimal separators **only while not focused** — on focus, the field shows the raw editable value (e.g., `12000.5`) so the user isn't fighting locale punctuation while typing; on blur it reformats per locale (e.g., `12,000.50` or `12 000,50` depending on the tenant's locale). Increment/decrement is available via `↑`/`↓` arrow keys (not spinner buttons, which cost screen width in a dense grid and are rarely used by mouse in practice). Same state set as text input, plus: **out-of-range** is a variant of error state (e.g., a quantity below a minimum order quantity) with the specific constraint stated in the inline message, not just "invalid value."

---

## 4. Date input

Renders and parses per the active locale's date format (Principle 6) — the component never assumes `MM/DD/YYYY`. Typing accepts several common separators and reformats to the locale's canonical display on blur; a calendar icon-button opens a picker popover as an alternative, never the only path (typed entry is faster for a daily user). Supports a `date range` variant with a single popover containing two calendars for reporting/filter contexts. Same core states as text input, plus: **invalid-format** error shows the expected format for the active locale as part of the message (e.g., "Use DD/MM/YYYY"). Country Packages may add validity-window constraints (e.g., a posting date outside an open fiscal period) — those render as the same error state with package-supplied message text, not a bespoke UI.

---

## 5. Money input

A number input with two required, locale-driven properties layered on: a **currency** (shown as a fixed, non-editable prefix/suffix per locale convention — prefix for `$1,234.56`-style locales, suffix for `1 234,56 €`-style locales) and **decimal precision from the currency's minor unit** (2 for USD/EUR, 0 for JPY, 3 for currencies like BHD) — the input never hard-codes "2 decimal places." When a document holds a foreign-currency line, the field shows both the entered amount and, read-only beside it, the converted amount in the document's base currency plus the rate used, so a user is never guessing which currency a number is actually in. States match Number input; **read-only** is the default state for any already-posted financial amount (Principle 2 — posted values are corrected via reversal, not edited in place).

---

## 6. Select

A closed-by-default dropdown for a small, fully-known, local option set (e.g., a status filter, a unit of measure from a short fixed list). Opens a `surface-raised` popover (`elevation-2`) anchored to the control; options are keyboard-navigable (`↑`/`↓`, `Enter` to choose, typeahead-to-jump, `Esc` to close without changing). States: default, hover/focus per-option inside the open popover, disabled (whole control), error (invalid/required-but-empty), empty (no options available — shows a short explanatory line inside the popover rather than an empty box, e.g. "No warehouses configured for this company"), read-only (renders as plain text with no affordance, for contexts like a posted document).

---

## 7. Async lookup (combobox)

For any option set that is large, tenant-specific, or server-backed — customers, items/SKUs, GL accounts, vendors. This is **not** a Select with more options; it queries the server as the user types (debounced ~250ms) rather than loading a full list client-side, because a tenant can have tens of thousands of customers or SKUs and shipping them all to the browser up front is exactly the anti-pattern this product must avoid.

| State | Behavior |
|---|---|
| Default (closed) | Shows the currently selected item's display label (e.g., item name + code), or placeholder if empty. |
| Typing | Opens the results popover; shows a lightweight in-popover spinner once a query is in flight (debounced, so brief pauses don't spinner-flash). |
| Results | Up to a fixed page (e.g., 20) of matches, each showing enough disambiguating detail to choose correctly (e.g., customer name **and** city/customer number, since names collide) — never just a bare name. |
| No results | Explicit "No matches for 'x'" row, plus, where the user has permission, a "Create new…" affordance inline rather than forcing a context switch to another screen. |
| Selected | Closed state re-renders with the chosen item; a small clear (×) affordance is available unless the field is required. |
| Error | Query failed (server/circuit issue) — shows a retry affordance inline in the popover, distinct from "no results" so a user never mistakes a failed lookup for "this customer doesn't exist." |
| Disabled / read-only | Same conventions as Select. |

**Keyboard**: identical navigation model to Select once the popover is open, so a user's muscle memory transfers between the two.

---

## 8. Data grid

The single grid component used for every tabular list in the product — customer lists, sales/purchase order lists, journal lines, stock ledgers, everything. It must handle both a 40-row customer list and a 150,000-row transaction ledger with the same code path and the same set of guarantees, because a component that only performs at small scale will quietly become a production incident at a real tenant's data volume.

### Non-negotiables (per `CLAUDE.md`/brief)
- **Server-side paging and virtualization always**, never "fetch everything and filter/sort client-side." A page fetches a bounded row count (default 50, configurable) plus a total-count estimate; the grid never assumes it has the whole dataset in memory.
- **Row virtualization** renders only the DOM rows in or near the viewport, recycled on scroll — required to stay responsive at 100k+ rows over a SignalR connection where the browser side must not accumulate the entire dataset.
- Sorting, filtering, and grouping happen **server-side**; the grid sends the request and shows a loading state on the affected columns/rows, it does not sort a local array.

### Anatomy
Toolbar (search box, filter chips, column settings, saved-view selector, bulk-action bar when rows are selected) → column header row (sort indicator, filter affordance per column, resize handle, drag handle for reorder) → virtualized row viewport → footer (page/row-count summary, page-size control).

### Features and their states

| Feature | Behavior | States |
|---|---|---|
| Sorting | Click a column header to sort asc/desc/none (cycling); shift-click adds a secondary sort key. Server re-queries on every change. | Default (no sort arrow) · active-asc/desc (arrow shown) · loading (header shows a subtle progress bar across the grid while the new page loads; existing rows stay visible, dimmed, until the new page arrives — never a blank flash). |
| Filtering | Per-column filter (type-aware: text contains/equals, number/date range, enum multi-select) plus a global search box that filters across the columns marked searchable for that grid. Active filters render as removable chips in the toolbar. | Default (no chips) · active (chips shown, count badge on the filter icon) · loading (as above) · no-results (see Empty states) · error (see below). |
| Column settings | Show/hide columns, reorder (drag or a settings-panel checklist), resize (drag handle, min-width enforced so a column never collapses to unreadable), pin a column left/right (e.g., keep "Customer" visible while scrolling wide ledgers horizontally). Persisted per user per grid. | Panel open/closed; a "Reset to default" action always available so a user can recover from a bad configuration. |
| Saved views | Save the current sort+filter+column configuration under a name, shared "for everyone in this company" or private to the user; a default view can be pinned. Switching views is instant on already-fetched metadata, then re-queries data. | List (own + shared views) · create/rename/delete (each behind an inline confirm for delete) · "unsaved changes" indicator when the live grid state diverges from the loaded view. |
| Bulk actions | Row checkboxes (header checkbox = select page / select all matching filter, explicitly distinguished — "Select all 3,412 matching rows" is a separate, deliberate action from "select the 50 on this page," because those are very different blast radii) drive a contextual action bar (e.g., "Approve," "Export," "Delete") replacing the toolbar while any row is selected. | None-selected (toolbar default) · some-selected (bulk bar + count) · action-in-progress (per Principle 3: progress shown per-item for long-running bulk operations, e.g. "42 / 3,412 processed," not a single opaque spinner for an operation that could take minutes) · action-error (partial failure shown as "3,409 succeeded, 3 failed — view errors," never a silent partial success). |
| Keyboard navigation | Arrow keys move a cell-level focus cursor (not just row focus) so a user can review a wide ledger without a mouse; `Enter` opens the row/record; `Space` toggles the row checkbox; `Ctrl/Cmd+A` triggers "select all on page" (with the matching-filter option offered, not silently assumed); column headers are reachable and operable (sort/filter) by keyboard, not mouse-only. | — |
| Virtualization / scale | Row height is fixed per density setting (32px compact / 44px comfortable) so virtualization math is cheap and predictable; horizontal virtualization applies too for grids with many columns (e.g., a 40-column stock-ledger export view). | Loading-more (a slim in-row skeleton for rows just beyond the fetched page, replaced as they scroll into view and the next page streams in) · at-end (no more rows, footer states total count) · error mid-scroll (a retry affordance in place of the failed page's rows, not a whole-grid failure). |

### Grid-wide states
- **Empty (no data at all)**: see Empty states component — a specific, actionable message ("No sales orders yet — Create your first order"), not a bare "No data."
- **Empty (filtered to nothing)**: distinct message ("No rows match your filters") with a one-click "Clear filters" action, so a user never confuses "there is no data" with "your filter is too narrow."
- **Loading (initial)**: skeleton rows matching the current density, not a spinner overlay that hides the toolbar/columns the user already knows.
- **Error**: a retry affordance inline where the data would be; the toolbar and column headers stay usable (e.g., a user can still adjust filters and retry) rather than the whole component going blank.
- **Read-only grid**: used for posted/historical data views — no checkboxes, no bulk-action bar, sort/filter/column features remain fully available (read-only affects mutation, never affects the user's ability to look at their own data efficiently).

### What we deliberately did not do
No client-side "load 10,000 rows into a JS array and filter in the browser" mode, ever, including for "small" tenants — a grid built to page and virtualize from day one is the same grid that survives a tenant growing from 10,000 to 500,000 ledger lines without a rewrite. **Borrowed from**: AG Grid's and Airtable's server-side row model, and Excel's own tables-over-a-million-rows behavior (Excel doesn't render a million cells either — it virtualizes the viewport) as the proof this pattern scales to spreadsheet-grade data volumes.

---

## 9. Forms with inline validation

Fields validate on blur (not on every keystroke, which is noisy and interrupts typing) and on submit attempt (which re-validates everything and focuses the first invalid field). A field already marked invalid re-validates live as the user corrects it, so the error clears the moment it's actually fixed rather than waiting for another blur.

| State | Presentation |
|---|---|
| Default | Label + control, no decoration. |
| Valid (after having been invalid) | Border returns to normal; no persistent "✓ valid" checkmark noise on every field — silence is the reward for correctness in a dense form. |
| Invalid | `danger` border on the control, an inline message directly below it in `danger` text starting with what's wrong and, where possible, how to fix it ("Tax ID must be 10 digits" not "Invalid value"), and the field is included in an on-submit summary at the top of the form for forms with more than ~6 fields, linking down to each error. |
| Required, empty, untouched | No error shown yet (errors don't precede interaction) — only surfaces on blur/submit. |
| Server-side validation error | Same visual treatment as client-side invalid; arrives after the round trip on submit, so the submit button shows its loading state until the response (pass or field-level errors) comes back — never a form that appears to hang with no feedback. |
| Cross-field validation | Attached to whichever field is most actionable (e.g., "ship date must be after order date" attaches to Ship Date, not both fields), plus mentioned in the submit-time summary. |
| Non-field-specific error (e.g. sign-in credentials) | Presented as a single banner above the field group, in the same `danger`/`warning` visual language as a field error, `role="alert"`, with focus moved to the banner on arrival. The implicated fields (e.g. email and password) get the `danger` border but **no individual message text** — this is the deliberate exception to "attach the error to the most actionable field": for anything where confirming *which* input was wrong would let an attacker enumerate accounts (wrong email vs. wrong password) or probe a lockout policy, the response must be identical regardless of which part was actually incorrect. See `docs/design/screens/SPEC-001-sign-in.md` for the worked example. |
| Disabled section | An entire section can be disabled with a one-line reason at the section header (e.g., "Billing address matches shipping address" toggle disabling the billing fields) rather than disabling fields one by one with no explanation. |

**Country Package extension point**: every form reserves a documented slot (a named `ExtraFields` region per form, positioned logically — e.g., "tax identifiers" sits directly under the base address block) where an installed Country Package can add fields (a VAT number, a company registration number format) without the base form's layout or validation logic changing. A form spec that doesn't declare this slot for a form that plausibly needs jurisdiction-specific fields is incomplete.

---

## 10. Master–detail layout

A list (grid or simpler list) on one side, the selected record's detail on the other. Desktop (≥1200px): both panes visible side by side, list ~35-40% width, resizable by a drag handle, remembered per user. Tablet (834-1199px): one pane at a time — selecting a list row navigates to a full-width detail with a back action, rather than compressing both panes into an unreadable split (per `app-shell.md` responsive rules).

| State | Behavior |
|---|---|
| No selection | Detail pane shows an empty state ("Select a customer to see its details") rather than a blank pane — a blank pane reads as broken, not as "nothing chosen yet." |
| Selected, loading | List row shows a selected style immediately (optimistic — Principle 3) while the detail pane shows a skeleton matching the eventual layout, not a spinner that discards layout information. |
| Selected, loaded | Full detail. |
| Selected, then list refetches (e.g., a filter change) and the selected row disappears from results | Detail pane stays showing the previously selected record (it still exists — a filter just no longer matches it) with a small note that it's outside the current filter, rather than yanking the pane closed on the user mid-review. |
| Unsaved changes in detail, user selects a different list row | Confirm-or-discard interstitial before navigating away — never silently discard an in-progress edit. |
| Error loading detail | Inline error with retry, scoped to the detail pane; the list stays usable. |

---

## 11. Document line editor

Used for every line-based document: sales orders, purchase orders, invoices, goods receipts, journal entries. This is the highest-frequency, highest-stakes surface in the product for a specialist user, so it is keyboard-first by design, with the mouse path fully available alongside it (Principle 7).

### Layout
A grid-like table of lines (item/account, description, quantity, unit price, discount, **tax code/rate column**, line total) above a locked footer showing running totals (subtotal, discount total, tax breakdown by rate/code, grand total) that recalculates as lines change. A blank "new line" row is always present at the bottom of populated lines, ready to receive input.

### Keyboard-first entry model
- `Tab` / `Shift+Tab` moves across cells left-to-right within a line; `Enter` commits the current cell and moves down to the same column on the next line (spreadsheet convention — matches Excel/Sheets muscle memory per Principle 7's borrowing).
- Typing into an item/account cell opens the async lookup (component 7) inline, in place, without opening a separate dialog — selecting a result (`Enter`, or click) fills the row's description/unit price/tax defaults from the item/account master and moves focus to Quantity.
- Pressing `Enter` on the last (blank) line creates a new blank line and moves focus into it, so a fast typist never has to reach for a mouse-driven "Add line" button — though that button is always present too, for the mouse-driven user (Principle 7).
- `Alt+Enter` (or an explicit row action) duplicates a line — common when entering many similar lines.
- `Delete`/a row-level delete action removes a line, with an undo toast (component 15) rather than a confirmation dialog for this specific low-stakes, easily-reversible action — contrast with Principle 2's stricter rule for posting/hard-delete, which this is not.

### Running totals and tax
- Totals recalculate **optimistically in the browser** the instant a quantity/price/tax cell is committed (simple arithmetic the client can do safely), then are **reconciled against the server's authoritative recalculation** on the next round trip (e.g., tax rules, rounding rules, and multi-currency conversion may be more complex than the client should reimplement) — if the two disagree, the server value wins and the UI briefly highlights the corrected total so the discrepancy is visible, never silently swapped.
- The tax column shows the resolved rate/code per line (driven by the installed Country Package's tax rules for the customer/item/company combination) and is user-overridable only where the underlying rule allows it (e.g., a tax-exempt customer flag) — an overridden tax value is visually marked (a small indicator) so a reviewer can spot manual overrides at a glance.
- Money formatting throughout follows component 5 (locale-driven currency/precision).

### States

| State | Behavior |
|---|---|
| Empty document | One blank line, ready for entry; footer totals show zero. |
| Entering | Optimistic totals update per keystroke-commit as above. |
| Line error | e.g., item is inactive, quantity exceeds available stock (soft warning vs hard block depends on tenant config) — shown inline in the offending cell, same visual language as Forms component; document-level submit is blocked only for hard errors, listed in a summary. |
| Saving/posting | Whole editor becomes read-only with a visible "Saving…"/"Posting…" state (button loading state, component 1) — no editing mid-flight, to avoid a race between a user's next keystroke and the in-flight commit. |
| Read-only (posted document) | Lines render without input affordances (no cursor, no lookup popovers) but remain fully selectable/copyable text; corrections happen via a reversal document per Principle 2, and the editor for a reversal is the same component pre-populated with inverse quantities. |
| Loading (opening an existing document with many lines) | Same paging/virtualization discipline as the data grid for documents with unusually many lines (e.g., a 500-line purchase order) — skeleton rows, not a hang. |
| Bulk paste | Pasting a block of spreadsheet cells (tab/newline-delimited) into the line grid parses it into multiple lines in one action — a specifically high-value fast path for this component, common in real ERP workflows (copying a customer's PO lines from an email/spreadsheet into a sales order). |

---

## 12. Status badges

One component, one shape, for every status in every module — order status, invoice status, stock status, approval status, tenant/subscription status. A small pill (`radius-full`) pairing a semantic color's `-muted` background with its full-saturation text color (e.g., `success-muted` background + `success` text) so the badge passes the same 4.5:1 audit as any other text. Never color alone: every badge also carries a short label (never an icon-only or color-only dot for anything status-bearing), so the distinction survives color blindness and grayscale printing (a real ERP scenario — printed invoices/reports).

| Semantic | Example uses |
|---|---|
| `info` | Draft, Pending, New |
| `warning` | On hold, Partially fulfilled, Expiring soon |
| `success` | Completed, Paid, Approved |
| `danger` | Cancelled, Overdue, Rejected |
| neutral (`text-muted` on `surface-sunken`) | Archived, Inactive — a deliberately quiet, non-alarming status |

States: default (as above), hover/focus only where the badge is itself an interactive filter toggle (e.g., clicking a status badge in a grid to filter by it) — in that case it gets the same focus ring as any control; otherwise badges are non-interactive text, not buttons wearing badge clothing.

---

## 13. Workflow stepper

For multi-stage document/process flows (e.g., Purchase Requisition → Approved → Ordered → Received → Invoiced, or an onboarding/setup wizard). Horizontal on desktop, collapses to a vertical compact list with the current step expanded on tablet.

| Step state | Presentation |
|---|---|
| Completed | Filled `success` indicator (checkmark), label in `text`. |
| Current | `accent` outlined indicator, bold label, connecting line to next step still neutral. |
| Upcoming | `text-muted` indicator and label, not yet reachable by click unless the flow explicitly allows jumping ahead. |
| Blocked/error | `danger` indicator on a step that failed or is blocked pending a prior fix, with a short reason on hover/focus. |
| Skipped/not-applicable | Present but visually de-emphasized (`text-subtle`) with a "not required for this document type" affordance — relevant given Country Packages can make some stages conditional (e.g., an e-invoicing submission step only exists where that package is installed). |

Steps are click-navigable only to already-completed or current steps by default (skipping ahead requires explicit permission/flow support) — this is a workflow guide, not a free-form tab set.

---

## 14. Dialogs

Modal, centered, `surface-raised` on `elevation-4` with an `overlay` scrim behind it. Reserved for: (a) a focused task that must interrupt the current context (confirmations, quick-create of a lookup's missing option), or (b) a blocking choice the user must resolve before continuing. Not used for anything that could instead be a side panel or inline expansion — a dialog stack more than one deep is a design smell, not a pattern to support.

| State | Behavior |
|---|---|
| Opening | Scrim fades in (`duration-fast`), dialog scales/fades in (`duration-base`), focus moves to the dialog's first focusable element or its heading. |
| Open | Focus is trapped inside the dialog (`Tab` cycles within it); `Esc` closes it **unless** it represents an in-progress, already-committed action (e.g., mid-post) — a dialog can be dismissed while merely collecting input, never while a commit is actually in flight. |
| Confirming/submitting | Primary button shows its loading state (component 1); other actions in the dialog disable to prevent a second concurrent submit. |
| Error | Inline error within the dialog (same visual language as Forms); the dialog stays open so the user doesn't lose their input. |
| Closing | Focus returns to the element that opened the dialog. |
| Destructive confirmation | Per Principle 2, restates exactly what will happen and to what record/company; for the most irreversible actions, requires typing the record's identifying name/number before the confirm button enables. |

**Danger zone panel** (added by `DESIGN-002`, first used in `screens/SPEC-003-tenant-detail.md` and `SPEC-003-tenant-offboarding.md`): where a screen holds one or more irreversible or hard-to-reverse actions alongside its normal, safe ones (deleting a tenant's database, hard-deleting a draft), those actions live in a visually separated panel — `danger`-colored border, its own heading, positioned last on the page/section, never mixed into the same button row as a safe action. This is the one place a `danger`-variant button is allowed to sit next to another button of *different* severity (e.g., "Cancel deletion" — safe, reversible — next to "Permanently delete" — irreversible); when that happens the safer action is styled `secondary`, not `danger`, so severity stays visible even within the panel, and the two are never the same size/weight so a mis-click is less likely. A destructive action never appears in a toolbar, a grid row, or a page's primary action row — only inside this panel or behind its own confirmation dialog per the "Destructive confirmation" row above.

---

## 15. Toasts

Transient, non-modal notifications for the outcome of an action the user just took (save succeeded, bulk action finished, an undoable delete). Stack in a fixed corner (bottom-right on desktop), most recent on top, auto-dismiss after 5s for success/info, but **errors and anything offering an Undo action do not auto-dismiss** — they wait for explicit dismissal, because an error that vanishes before it's read has not actually communicated anything.

| State | Behavior |
|---|---|
| Entering | Slides/fades in (`duration-base`, `easing-entrance`). |
| Visible | Icon + message in the matching semantic color pairing (same `-muted`/text pairing as badges), optional action link (e.g., "Undo," "View"). |
| Hover | Auto-dismiss timer pauses while hovered/focused, so a user reading a toast doesn't have it vanish mid-read. |
| Exiting | Fades/slides out (`duration-fast`, `easing-exit`); a screen-reader-only live region announces the message regardless of the visual toast, so it isn't a purely visual-only notification. |
| Stacked overflow | Beyond a small stack limit (e.g., 4), collapses to "+N more" rather than covering the screen. |

---

## 16. Command palette

Global, keyboard-summoned (`Ctrl/Cmd+K`) overlay for navigating to any module/screen, jumping to a specific record by number/name (backed by the same async-lookup/server-search discipline as component 7 — it queries, it doesn't preload the tenant's entire record set into the browser), and invoking common actions ("Create sales order," "Switch company"). See `app-shell.md` for its role in the shell and its recent-items integration.

| State | Behavior |
|---|---|
| Closed | Not rendered/hidden; global shortcut is the only trigger besides an explicit shell button (mouse path always present per Principle 7). |
| Open, empty query | Shows recent items and a short list of top actions — never a blank box waiting for input with no guidance. |
| Typing | Debounced server query for record matches, blended with a client-side-instant filter over the small, already-loaded set of static actions/navigation targets (navigation targets are few and static, so that part alone is fine to filter client-side — this is the one deliberate exception to "always query the server," precisely because the dataset is small and fixed, unlike customer/item lookups). |
| Results | Grouped by type (Actions, Navigate to, Records) with keyboard `↑`/`↓` to move, `Enter` to activate. |
| No results | "No matches for 'x'" with a hint of what it searches. |
| Loading | Inline spinner in the results area only, palette shell stays stable. |
| Selecting a company-scoped record from a different company than the active one | Palette surfaces the record's company inline in the result row, and selecting it triggers the same company-switch confirmation as the tenant/company switcher (component 18) if it differs from the active company — the palette is a fast path, not a bypass of Principle 5. |

---

## 17. Empty states

Every list, grid, dashboard widget, and detail pane has a designed empty state — never a bare blank area, which reads as a bug. An empty state has: a short, specific headline (not "No data"), one sentence of context, and, where the user has permission, a primary action to resolve it (e.g., "No purchase orders yet" → "Create purchase order"). Distinguish explicitly between:
- **True empty** (no records exist yet) — encouraging, action-oriented tone.
- **Filtered-to-empty** (records exist, current filters exclude all of them) — "Clear filters" as the primary action, not "Create new," which would be the wrong fix.
- **No permission** (records may exist but the user can't see them) — states that plainly, without exposing whether data exists, and does not offer a create action the user isn't authorized for. **This state must be byte-for-byte the same as the state shown when the resource does not exist for this tenant at all** (module not enabled, route not served): same heading, same body, no counts, no create action, and **no permission name** — a message naming a permission cannot also serve a tenant that has no such resource, and the difference between the two messages is exactly what turns a screen into an oracle for "does this exist here?". Whatever status the server returned, the UI collapses it to this one state. Added by `DESIGN-001`; worked example and its acceptance criterion in `screens/SPEC-002-company-create.md`.
- **Error** (couldn't load) — retry action, not styled identically to true-empty so the two are never confused.

---

## 18. Tenant/company switcher

The structural safeguard against posting into the wrong company (Principle 5). Always visible in the app shell header, never collapsed into a generic settings menu. Full behavior, layout, and the switching interstitial are specified in `app-shell.md`; the component contract here:

| State | Behavior |
|---|---|
| Default | Shows tenant name + active company name + a distinguishing color/initial badge per company (so two similarly-named companies in one tenant are still visually distinct at a glance), always on-screen. |
| Open (picker) | Popover listing the tenant's companies (grouped by tenant if the user belongs to more than one tenant — rare but must be supported, e.g. an accountant serving multiple client tenants); current company marked; a search box appears once the list is long enough to need one. |
| Switching, no unsaved work | Immediate switch, full-shell re-render (nav, data, everything scoped to the newly active company) with a brief loading state; the switch is logged (who, when, from which company to which) per the audit-logging standard in `CLAUDE.md`. |
| Switching, unsaved work exists elsewhere in the shell | Blocking confirmation interstitial naming what will be discarded, before the switch proceeds — never a silent loss of in-progress work as a side effect of a company switch. |
| Read-only / single-company tenant | Still renders (never hidden), showing the one company name as a static, unmistakable label — so the visual guarantee ("you can always see which company you're in") holds even for the tenants who never need to switch. If the tenant's display name and the company's name are identical (the common case immediately after provisioning, since the default company is named from the tenant's own display name — `SPEC-001` BR-3), the switcher shows the name once rather than repeating it as "X ▸ X"; the two segments reappear automatically the moment they differ (a rename, or a second company added). |
| Disabled mid-critical-action | If the user is mid-post/mid-submit elsewhere, the switcher itself is briefly disabled with a tooltip reason, rather than allowing a switch that could orphan an in-flight transaction against the wrong context. |

---

## 19. Attention summary strip

Added by `DESIGN-002` for `screens/SPEC-003-tenant-list.md`, the first list whose rows carry lifecycle states of genuinely unequal severity rather than states that are all equally "fine" (an order being Draft vs. Confirmed is a workflow position; a tenant being `SchemaBlocked` is an incident). A plain, uniformly-styled Status badge column is not enough on its own: it renders every state pill the same visual weight, which is exactly wrong when three rows out of ten thousand need a human today and the rest need nothing. This component is the answer, and it is written generically because the same shape will recur wherever this product shows an operator a list of long-running things with a failure mode (migration runs, package installs, background job queues) — Principle 4 applies to operator surfaces too, not only tenant-facing ones.

**What it is**: a slim, single-row strip pinned above a Data grid's own toolbar, one segment per "needs a human" category the grid's rows can be in, each segment a count plus a short label, styled in that category's semantic color (reusing the Status badge palette, never a new color). Each segment is a real button, not decorative text: activating it applies the matching filter to the grid below, the same filter a user could build manually from the toolbar's own filter chips — the strip is a shortcut into the grid's existing filtering, never a second, parallel way to filter that the grid itself doesn't also support.

| State | Behavior |
|---|---|
| Nothing needs attention | **Not rendered at all** — no empty strip, no "0 issues" reassurance banner. A healthy list costs the operator zero additional visual attention, the same reasoning `app-shell.md`'s connection banner uses for the healthy-connection case. |
| One or more categories non-zero | One segment per non-zero category, ordered by severity (most urgent first), each `{count} {label}` (e.g., "2 need action", "3 pending deletion, soonest in 2 days"). Counts are locale-formatted per Principle 6, never a literal. |
| Segment activated | Applies that category's filter to the grid below (replacing, not stacking with, any unrelated filter already active on the same dimension) and moves focus into the grid's toolbar filter-chip region so a screen-reader user lands where the state actually changed, not back at the strip. |
| Loading | The strip is absent until the first count query resolves — it never shows a skeleton, because a wrong or stale attention count is worse than a one-beat delay in showing it at all. |
| Counts stale relative to the grid (e.g., a row's state changed after the strip's own count was fetched) | Not solved by this component — the strip and the grid share one query result for the same page load; a longer-lived session re-fetches both together on the grid's normal refresh/re-query triggers, never the strip alone. |

**What it deliberately does not do**: it does not replace the grid's own state column or its filter chips (a user must still be able to filter to any state, urgent or not, the normal way); it does not auto-sort the grid (sort order is the grid's own, documented per screen — see `SPEC-003-tenant-list.md`'s severity-first default); and it never contains an action that mutates data itself (no "resolve all" button) — it navigates the operator to the rows that need a decision, and the decision itself always happens on that record's own detail screen, never in bulk from the strip.

**Accessibility**: the strip is a `<nav>`-like landmark region with an accessible name ("Needs attention"), each segment is a `<button>` with a full accessible name including the count and label (never an icon-only or color-only indicator — Principle: "never color alone" from Status badges applies identically here), and appearing/disappearing (as counts change) is announced via the same `aria-live="polite"` region pattern already established for toasts, so an operator who isn't looking at the screen when a tenant flips into `SchemaBlocked` still has a way to notice on next interaction without polling the page.

**Borrowed from**: the "alerts" summary row above an incident list in PagerDuty and Datadog, and GitHub's "N workflow runs failed" banner above an Actions list — all three put a small, severity-ordered, clickable count strip above a list whose rows are mostly fine and occasionally are not, rather than asking the operator to scan every row to find the ones that matter.
