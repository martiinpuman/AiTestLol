# Screen spec — Companies (list)

Status: ready · Author: ui-designer · Date: 2026-09-11 · **Amended 2026-09-12 by `DESIGN-001`**
Backlog: `DESIGN-01` (original); the **list** half of `DESIGN-001`. The create half is `SPEC-002-company-create.md`, written 2026-09-12; the two together are the whole of `DESIGN-001`.
Product spec: `../../product/specs/SPEC-002-company-create-and-list.md` (BR-1, BR-4, BR-8, AC-6, AC-7, AC-8, AC-9)
Architecture: `../../decisions/ADR-0010-authorization-roles-and-permissions.md`
Prototype: `../prototypes/companies-list.html` (open directly in a browser — no build step, no network fetch)
Components referenced: App shell (`app-shell.md`, including its nav sub-navigation), Empty states (`components.md` §17, all four variants), Buttons (§1), Status/permission microcopy per §1's disabled-reason rule

## Amendment — 2026-09-12 (`DESIGN-001`)

`SPEC-002-company-create.md` now specifies the create dialog, and four things in this document
changed as a direct consequence. Each is applied inline below; they are listed here so a reader
does not have to diff.

1. **`+ New company` opens the create dialog.** It previously raised a toast saying the form was
   specified separately. It was; it now is.
2. **The "no permission" state became the neutral "unavailable" state.** It no longer names a
   permission, because the same state must also serve a tenant for which the resource does not
   exist at all — otherwise the screen answers "does this exist here?" for a caller who is not
   allowed to know. See the create spec, "Permission, and why this screen is not an existence
   oracle". Keys `companies.noAccessHeading`/`Body` are replaced by
   `companies.unavailableHeading`/`Body`.
3. **The table gained a visually hidden `<caption>`** (`companies.tableCaption`, naming the tenant)
   and `scope="col"` on its headers — the table previously had no accessible name (SC 1.3.1).
4. **The footer count is computed from the rendered rows and formatted per locale**, not carried
   as a literal in the markup — the same correction this document already records for the
   many-rows footer, which the default state had not received.

A skip link (SC 2.4.1) was also added to the prototype, and the scale note moved off `text-subtle`;
both are recorded as design-system findings in the create spec rather than as list behaviour.

## Out of scope (read this first)

This spec covers `GET /api/v1/companies` — the list. It does **not** cover:
- The create-company dialog — that is `SPEC-002-company-create.md`. This spec keeps only the list's
  own side of it: where the trigger sits and when it is disabled.
- A company detail/edit view — SPEC-002 itself defers editing to Milestone 2.
- The full data-grid chrome (sorting, filter chips, saved views, column settings, bulk actions) — see
  "Why not the Data grid" below, and `GRID-001`/`GRID-002`.

## Purpose

The first screen in this set that renders real, queried tenant data, and the first that must express — visibly and correctly — a permission the signed-in user might not hold. It is also the destination the first-run landing screen's primary action points to, so together the three screens in this set form one real, walkable path: sign in → see what's already set up → see the actual data.

## User

Any signed-in user in a tenant, viewing the Companies belonging to that tenant only (BR-1). Most will hold the seeded Administrator role (full access); this spec also designs for a more limited role explicitly, per SPEC-002 BR-4's own note that the query "must still be written as a properly scoped query… so it is correct without modification once Milestone 2 introduces company-scoped assignments."

## Task flow

1. User navigates to Master data → Organization → Companies (or arrives via the first-run landing screen's "View companies" action, or the command palette).
2. The list loads, scoped to exactly what the caller's role assignments allow (BR-4).
3. If the user also holds the company-management permission, "+ New company" is available (disabled, with a stated reason, otherwise).
4. The user can view Name and Created date for every Company they're allowed to see; no other action exists on this screen.

## Layout

```
┌───────────────────────────────────────────────────────────────────────────┐
│ ☰  Aurora ERP     [Nordwind Group ▸ Nordwind Group]      🔍 Search   ⌘K   │
├───────┬───────────────────────────────────────────────────────────────────┤
│ Home  │  Master data · Organization                                       │
│▸Master│  Companies                            [Light|Dark|Sys] [+ New co.]│
│ data  │  ┌─────────────────────────────────────────────────────────────┐  │
│ ↳ Org·│  │ Name                              Created                    │ │
│ Cos   │  │ Nordwind Group                    Jun 2, 2026                │ │
│ Sales │  │ Meridian Distribution             Jul 18, 2026               │ │
│ ...   │  │ Solheim Trading — Nordic...        Sep 5, 2026                │ │
│       │  └─────────────────────────────────────────────────────────────┘  │
│       │   Showing 3 of 3 companies                                        │
└───────┴───────────────────────────────────────────────────────────────────┘
```

## Why not the Data grid

SPEC-002's own non-goals are explicit: "using the 150,000-row grid component here would create a false dependency on Milestone 1.5 and would be gold-plating. This spec uses a plain, non-virtualized list." This screen therefore deliberately looks different from `components.md` §8's Data grid — no sticky sunken header row, no sort affordance, no checkbox column, no toolbar — specifically so a developer implementing it never mistakes a two-column, unpaginated-in-practice list for the grid component and starts wiring in grid features (saved views, column settings) nobody asked for here. If a tenant is ever observed with enough companies that this distinction stops making sense, see "Thousands of rows" below.

## Fields / columns

| Column | Source | Format |
|---|---|---|
| Name | `Company.Name` (SPEC-002 BR-2: non-empty, trimmed, ≤ 200 characters) | Plain text, wraps rather than truncates (see "Long values") |
| Created | `Company.CreatedAt` | Locale-formatted date via the same `formatDate`/`formatDateTime`-style call used throughout this design system (Principle 6) — never a literal date string |

No third column is added (e.g., no "Default" flag) — SPEC-002's own schema notes list exactly `id`, `name`, `created_at`, `created_by`; a column backed by a field the schema doesn't have would be a UI promise the data model can't keep.

## Permission — what "the user might not hold" actually means here

SPEC-002 names one permission explicitly (`organization.company.manage`, exact constant left to the implementing developer) that gates **creation**. It does not name a separate permission for **listing** — but ADR-0010 rule 5 requires every command *and query* to declare a permission. This spec assumes the implementing developer introduces a distinct, lower-privilege `organization.company.view` (or equivalent) for the list query, separate from `.manage`, since a bookkeeper viewing which companies exist is a different, more common capability than one who can create them. **This is a naming proposal, not a locked decision** — flagged here explicitly, the same way SPEC-002 itself left `.manage`'s exact name open, so whoever implements B-15.2 can confirm or rename it without this spec being silently wrong either way.

Two distinct permission states follow from this:

1. **Holds `.view` but not `.manage`.** Full list renders; "+ New company" is visible but disabled, with a reachable reason ("Requires the organization.company.manage permission") per the Buttons component's disabled-state rule (`components.md` §1) — never a silently missing button, which would look like a bug rather than a permission boundary.
2. **Holds neither — or the resource is not served for this tenant at all.** One state, deliberately
   identical for both causes: the entire list surface, including the page's own "+ New company"
   action, is withheld, and the copy names **no permission** and **no count**. Per `components.md`
   §17's "No permission" rule, plus the stronger rule the create spec adds: a message that names a
   permission cannot also serve a tenant that has no such resource, and the difference between the
   two messages is exactly what would turn this screen into an oracle for whether a Company exists.
   Full rule and its acceptance criterion: `SPEC-002-company-create.md`, "Permission, and why this
   screen is not an existence oracle".

## Locale and formatting (Principle 6)

Like the first-run landing screen, this tenant (Nordwind Group) has zero installed Country Packages, so the locale control here is again a labeled **prototype review affordance**, not an in-product picker — see that screen's spec for the full reasoning, which applies identically here. Two independent formatting demonstrations exist on this screen:

**The Created column** (small numbers, date-only difference):

| Locale | Row 1 rendered |
|---|---|
| en | 6/2/2026 |
| sv-SE | 2026-06-02 |

Both footer counts are computed from the rendered rows and pushed through the number formatter; neither is a literal in the markup.

**The footer count, in the "many rows" demo state** (this is where the thousands-separator difference is actually visible — three real companies is too small a number to show it):

| Locale | Footer text |
|---|---|
| en | Showing 1–50 of 1,204 companies |
| sv-SE | Showing 1–50 av 1 204 företag |

An earlier draft of this prototype embedded the raw number `1204` directly into the translated string's interpolation, which produced identical output in both locales and silently failed to demonstrate anything — caught and fixed before this spec was written; the corrected version formats the total through the same locale-aware number formatter used everywhere else, exactly like the date column.

## States

**Default.** As laid out above.

**Loading.** Skeleton rows (the shared `.skeleton` class introduced by the first-run landing screen's prototype) replace the table body while the query is in flight; column headers stay visible so the shape of what's coming doesn't shift.

**Empty — true empty.** Per SPEC-002 AC-7, this "shouldn't happen" on a provisioned tenant (BR-3 guarantees exactly one default Company always exists) — so, deliberately, this empty state is framed as an anomaly to report, not an invitation to create the first record. This is a considered departure from `components.md` §17's usual "true empty" tone (encouraging, action-oriented, "Create your first X"): offering a "Create company" call to action here would both (a) imply this is a normal first-time state, when it is actually a guarantee violation worth surfacing as one, and (b) reach outside this spec's own scope into the create form. A tenant that legitimately starts with zero of something else later (e.g., zero sales orders) gets the normal encouraging empty state; this one specific "should be structurally impossible" case does not.

**Empty — filtered to nothing.** Not applicable — this screen has no filters (see "Why not the Data grid").

**Error.** Retry action, visually distinct from the true-empty state per `components.md` §17.

**Missing permission.** See "Permission" above — the no-`.view` case.

**Long values.** A Company name at BR-2's 200-character limit wraps across lines in the Name cell rather than truncating with an ellipsis. This is a deliberate choice, not the default grid behavior: a bounded, few-dozen-row list under no scrolling/density pressure has nothing to gain from truncation, and truncation's usual accessibility gap (a hover-only tooltip that isn't reliably keyboard- or screen-reader-reachable) is avoided entirely by not needing one. The prototype includes one deliberately long name ("Solheim Trading — Nordic Regional Operations and Bonded Warehouse Division") to prove the wrap looks intentional, not broken.

**Thousands of rows.** SPEC-002's non-goals rule out the full Data grid for *this* milestone, on the realistic assumption that a tenant holds at most a handful of companies. This spec still documents the boundary rather than ignoring it: the prototype's "many rows" demo state shows what a (realistically implausible, but illustrated for completeness) few-hundred-to-thousand-row scenario would need — the same bounded-page-size query BR-8 already requires, with working Prev/Next controls — and states explicitly, in both the UI copy and this spec, that if any real tenant is ever observed at that scale, the correct fix is moving this screen onto the Data grid component (`components.md` §8), not teaching the plain list ad hoc virtualization.

## Keyboard

| Action | Key |
|---|---|
| Standard shell navigation | Per `app-shell.md` |
| Skip past the nav rail to the content | `Tab` to the first focusable element ("Skip to main content"), then `Enter` |
| Reach "+ New company" (when enabled) | `Tab`, then `Enter`/`Space` — opens the create dialog (`SPEC-002-company-create.md` for its own focus order) |
| Reach a disabled "+ New company" and discover why | `Tab` moves focus to it — it is `aria-disabled`, not natively `disabled`, specifically so it stays in the tab order; `aria-describedby` announces the reason on focus (see `components.md` §1's correction, found while building this screen) |
| Retry after an error | `Tab` to Retry, `Enter`/`Space` |
| Page through the "many rows" demo state | `Tab` to Prev/Next, `Enter`/`Space` |

This screen has no row-level interaction (no detail view exists yet per "Out of scope"), so there is no cell-level keyboard model to specify — unlike the Data grid, a plain list's rows are not independently focusable/actionable here.

## Accessibility notes

- The table uses real `<table>`/`<th scope="col">`/`<td>` markup (not styled `<div>`s), so screen readers announce column headers per cell in the normal way, with no extra ARIA needed for a two-column, non-interactive table. It carries a visually hidden `<caption>` naming the tenant, which is the table's accessible name (SC 1.3.1) and gives a screen-reader user the same "which books am I looking at" confirmation the switcher gives a sighted one.
- The disabled "+ New company" button uses `aria-disabled="true"` plus `aria-describedby` pointing at the reason text, never the native `disabled` attribute — a control a user cannot yet use is still information a keyboard/screen-reader user needs to discover, and native `disabled` would remove it from the tab order entirely. This is a correction to `components.md` §1 itself (previously said "not focusable," which cannot coexist with "reason reachable on focus" for the same control) made while building this screen, not a one-off choice for this screen alone.
- The permission-denied panel's heading and body are read as ordinary page content (no `role="alert"` — this is not a transient error interrupting an in-progress action, it is the entire page's steady-state content for this user, and should be navigable like any other heading rather than announced urgently).
- The nav rail's expanded sub-item ("Organization · Companies") is reachable and marked current via the same `.active` treatment as its parent, so a screen reader user gets both levels of "where am I" that a sighted user gets from the breadcrumb and the highlighted nav rail together.

## What we borrowed and why

The plain, unadorned "master data list, no grid chrome" pattern — for a resource type a business realistically has only a handful of — mirrors how Xero and QuickBooks Online render short, low-cardinality configuration lists (bank accounts, tracking categories) with an ordinary table rather than their own transaction-grid components, reserving grid-grade chrome for genuinely high-volume lists (invoices, transactions). The explicit "this shouldn't happen — contact support" framing for a structurally-guaranteed-nonempty collection borrows from how mature products treat an invariant violation as a distinct, honestly-labeled case rather than silently reusing the same friendly first-time-user copy that would be correct for an actually-new user.
