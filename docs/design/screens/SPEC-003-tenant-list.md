# Screen spec — Tenants (operator console, list)

Status: ready · Author: ui-designer · Date: 2026-09-12
Backlog: `DESIGN-002` · Unblocks: `B-07` (gives its implementer a spec instead of inventing screens)
Product spec: **none exists.** No PM spec covers the operator console (SPEC-001's own "UX reference" section explicitly scopes provisioning to an internal API call with no dedicated screen in Milestone 1). This spec derives its business rules directly from `CLAUDE.md` ("Tenant lifecycle is first-class… offboarding must be able to export and then destroy exactly one tenant's data"), ADR-0007, and the `TenantState` enum as it exists in code today. **Routed to the project-manager: a product spec should formalize who may operate this console and under what business rules — see "To route" below.**
Architecture: `../../decisions/ADR-0007-multi-tenancy-database-per-tenant.md` §7 (schema version skew), §8 (provisioning saga, reaper, compensation guard), §9 (catalog schema — `catalog.tenant` columns), §10.2 (fan-out selects on `Active`), §11 (offboarding state machine); `src/platform/Aurora.Platform.Tenancy.Contracts/TenantState.cs` (the enum this spec is built against, quoted verbatim below)
Prototype: `../prototypes/tenant-list.html` (open directly in a browser — no build step, no network fetch)
Components referenced: App shell → **Operator console** (`app-shell.md`, new section), Data grid (`components.md` §8), Status badges (§12), Attention summary strip (§19, new — added by this spec), Empty states (§17)

## Scope

**In:** the tenant list itself — every column, the severity-first default ordering, the Attention summary strip, filtering (by state, region, plan, and a "include deleted" toggle), search, loading/error/empty states, permission, keyboard and accessibility.

**Out:**
- Triggering provisioning — no "+ New tenant" button exists on this screen. SPEC-001's own non-goals rule out a self-service or operator-facing creation form for this milestone ("provisioning in this milestone is triggered by an authenticated internal call… not by a form"); adding one here would be scope this spec was not asked to design and would contradict that non-goal.
- Everything a row leads to — `SPEC-003-tenant-detail.md`.
- The offboarding flow itself — `SPEC-003-tenant-offboarding.md`.
- Bulk operator actions (no row checkboxes on this grid at all) — see "Why this grid has no bulk actions" below.
- A migration-run console, a cluster-capacity view, a Country Package catalogue admin screen — all real, all named in ADR-0007/ADR-0008 as future operator surfaces, none designed by this task.

## Purpose

The operator's home screen: every tenant Aurora runs, with enough information to answer "is anything on fire, and which tenant is it" in the time it takes the page to render, and a path into any one tenant's own detail for anything that needs a decision.

## User

An Aurora operator — internal staff, not a tenant's own user — holding a platform-level capability (see "Permission" below). There is no active tenant/company context for this user; they are looking *at* the fleet, not working *inside* one tenant, which is why this screen lives behind the **Operator console** shell (`app-shell.md`, new section) and not the tenant shell every other spec in this design system uses.

## The exact `TenantState` set (confirmed against code, not assumed)

The brief for this task supplied six states. **The code has eight.** Grepped directly from `src/platform/Aurora.Platform.Tenancy.Contracts/TenantState.cs`, in declaration order:

```csharp
public enum TenantState
{
    Provisioning,       // saga running, not routable
    ProvisioningFailed, // saga exhausted its retry budget; only state a database may be destroyed from
    Active,             // routable and trading
    Suspended,          // read-only, first offboarding step, reversible
    SchemaBlocked,      // schema cannot be trusted; maintenance page
    Exporting,          // offboarding export in progress
    PendingDeletion,    // database renamed, reversible for 30 days
    Deleted,            // DROP DATABASE has run; tombstone row only
}
```

`Suspended` and `Deleted` were missing from the brief's list. Both matter to this screen's design, not just its completeness: `Suspended` is where `SPEC-003-tenant-offboarding.md`'s stepper begins, and `Deleted` is the one state this spec argues should be **hidden by default** (see below) rather than rendered like every other row — the opposite problem from the five states that need to be *more* visible, and worth exactly as much of a design decision.

## Task flow

1. An operator signs in to the operator console (out of scope — see `app-shell.md`'s new section; this milestone assumes an existing internal authentication mechanism, not designed here) and lands on `/operator/tenants`.
2. The list loads, severity-first (see "What this screen does about severity" below).
3. If anything needs action, the Attention summary strip names it; activating a segment filters the grid to it.
4. The operator opens any row → `SPEC-003-tenant-detail.md` for that tenant.

## Layout

```
┌───────────────────────────────────────────────────────────────────────────┐
│ A  Aurora ERP · Operator Console                          J. Alvarez  ⏻   │  ← surface-sunken, not surface —
├───────────────────────────────────────────────────────────────────────────┤     see app-shell.md's operator section
│  Tenants                                                                  │
│  ⚠ 2 need action  ·  ⏳ 3 pending deletion, soonest in 2 days             │  ← Attention summary strip (§19);
│  ───────────────────────────────────────────────────────────────────────  │     absent entirely if both are zero
│  [🔍 Search name or key]  [State ▾]  [Region ▾]  [Plan ▾]  ☐ Include deleted│
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │ Tenant                    State                Plan   Region  Schema│ │
│  │ Kessler Fasteners GmbH    ● Needs action        Std   eu-west   —    │ │  ProvisioningFailed, red group
│  │  kessler-fasteners        Provisioning failed after 5 attempts      │ │
│  │ Solheim Trading           ● Needs action        Std   eu-north  9   │ │  SchemaBlocked
│  │  solheim-trading          Blocked — schema v9 is below minimum v11  │ │
│  │ Meridian Distribution     ⏳ Pending deletion    Std   eu-north  14  │ │  warning group, soonest first
│  │  meridian-distribution    Deletes in 2 days · 2026-09-14            │ │
│  │ Nordwind Group            ✓ Active              Std   eu-west  14   │ │  normal group
│  │  nordwind-group           Last activity 3 minutes ago               │ │
│  │ …                                                                    │ │
│  └─────────────────────────────────────────────────────────────────────┘ │
│  Showing 1–50 of 3,412 tenants                                            │
└───────────────────────────────────────────────────────────────────────────┘
```

## What this screen does about severity (the substance of this spec)

A status-badge column alone renders `SchemaBlocked` and `Active` as two equally-weighted pills — exactly the failure this task's brief calls out. Three mechanisms, not one, because a single mechanism (say, just sorting) is easy to miss and a single strong mechanism (say, just color) is exactly the "coloured pills of equal weight" trap:

1. **The Attention summary strip (§19, new component)** sits above the toolbar and names, in words, what needs a human: how many tenants are `ProvisioningFailed` + `SchemaBlocked` combined (one segment, "needs action" — grouped together because both mean the same thing to an operator scanning for work: *stop and look*), and how many are `PendingDeletion`, with the soonest `deletion_due_at` called out by name. **The strip renders nothing when both counts are zero** — a healthy fleet costs the operator no attention, matching the reconnection banner's own "nothing shown when healthy" precedent in `app-shell.md`.
2. **The default sort is severity, not alphabetical**, and is not merely a suggestion the operator can ignore by accident — it is the grid's named default view ("Needs attention first"), restorable via "Reset to default" the same way `components.md` §8's column settings already work. Rank, lowest (top) to highest:
   - Rank 1 — `ProvisioningFailed`, `SchemaBlocked` (needs a human now)
   - Rank 2 — `PendingDeletion`, ordered by `deletion_due_at` ascending (soonest destruction first — the one sub-ordering where "soonest" outranks "most recently changed")
   - Rank 3 — `Provisioning`, `Exporting` (in progress, ordered by `started_at`/`created_at` ascending — the longest-running one is the most likely to actually be stuck, so it surfaces first even within "just working")
   - Rank 4 — `Active`, `Suspended` (normal), alphabetical by display name
   - Rank 5 — `Deleted` — see point 3
   Clicking any column header still re-sorts normally (`components.md` §8) — severity is the *default*, not the *only*, ordering, per Principle 7 (a fast path beside the ordinary one, never instead of it).
3. **`Deleted` tenants are hidden by default**, via an "Include deleted" toggle in the toolbar, off by default. This is the opposite move from ranks 1–2, and deliberately so: a `Deleted` row is a tombstone — id, key and dates only, per ADR-0007 §11.4 — that can never again need an operator's action. Showing it by default alongside rows that might is the same category error as showing a resolved PagerDuty incident at the same visual weight as an open one; it does not need to be *invisible* (a deletion certificate is a real, auditable fact an operator sometimes needs — hence the toggle, not a hard removal), it needs to *not compete* for attention with the five states that do.

Read together: the strip answers "is anything wrong, right now" in one glance without scrolling; the sort means an operator who ignores the strip and just starts reading top-to-bottom still meets the urgent rows first; and the deleted-hidden default means the rows that no longer matter don't dilute either of the first two.

## Why this grid has no bulk actions

`components.md` §8's grid anatomy includes a bulk-action bar by default; this instance of the grid deliberately omits the row-checkbox column entirely, not merely leaves the bar unused. ADR-0007 §8's compensation guard is explicit that a destructive tenant action runs "only after re-reading `platform.tenant_identity` and confirming it matches **that tenant id**" — singular, per tenant, by design, because a batched version of that check is exactly how a takeover-adjacent bug (a database adopted for the wrong tenant, a `DROP DATABASE` fired against the wrong row) would slip through a review that only exercised the single-tenant path. A "select 40 tenants → Suspend" action would misrepresent the product's own safety model on the very screen that exists to enforce it. Every state-changing action on a tenant happens on that tenant's own detail screen (`SPEC-003-tenant-detail.md`), one at a time, every time.

## Columns

| Column | Source (`catalog.tenant`, ADR-0007 §9.2) | Format |
|---|---|---|
| Tenant | `display_name` (primary line) + `key` (secondary line, `mono`, muted) | Plain text; wraps rather than truncates (see "Long values") |
| State | `state` | Status badge (`components.md` §12) + a one-line status detail beneath it, state-specific (see table below) — the detail line is what makes two `SchemaBlocked` rows distinguishable from each other, not just from an `Active` one |
| Plan | `plan` | Plain text — set at reservation (`Tenant.Reserve`, ADR-0007 §8 step 1), so present for `Provisioning` and `ProvisioningFailed` too, unlike `Schema` below; `—` only for `Deleted`, whose catalog row is tombstoned to id/key/dates (ADR-0007 §11.4). *(An earlier draft of this row claimed Plan was blank until `Activate` — checked against `Tenant.cs` and corrected: `Plan`, like `Region`, is a constructor argument to `Reserve`, not a field `Activate` fills in.)* |
| Region | `residency_region` | Monospace region code (e.g. `eu-north`) — an infrastructure identifier, never localized, never translated (same reasoning as `SPEC-002`'s `{permission}` interpolation); `—` only for `Deleted`, same reason as `Plan` |
| Schema | `core_schema_version` | Right-aligned tabular number; `—` for `Provisioning` and `ProvisioningFailed` (neither has reached `Activate`, the only method that sets it) and for `Deleted` (tombstoned away); a real value everywhere else |
| Created | `created_at` | Locale-formatted date, same discipline as every other date in this design system |
| Last activity | `last_activity_at` | Locale-formatted date-time; `—` if never opened a scope (true for every `Provisioning` tenant) |

**State detail line, per state** (this is real content, so it renders through `text-muted`, never `text-subtle` — the same correction `SPEC-002` already made to this design system):

| State | Badge | Detail line |
|---|---|---|
| `Provisioning` | `info` | `tenants.state.provisioningDetail` — "Step {step} of 9" |
| `ProvisioningFailed` | `danger` | `tenants.state.provisioningFailedDetail` — "Failed after {attempts} attempts" |
| `Active` | `success` | `tenants.state.activeDetail` — "Last activity {relativeTime}" (or `tenants.state.activeDetailNever` if null) |
| `Suspended` | `neutral` | `tenants.state.suspendedDetail` — "Suspended {relativeTime} — offboarding" |
| `SchemaBlocked` | `danger` | `tenants.state.schemaBlockedDetail` — "Blocked — schema v{version} is below minimum v{minimum}" (or the quarantine wording if blocked by a migration-run failure rather than version skew — both are real per ADR-0007 §7.5/§7.4; the detail server resolves which applies and this spec doesn't require the UI to know the difference beyond rendering whichever message the API returns) |
| `Exporting` | `info` | `tenants.state.exportingDetail` — "Export in progress" |
| `PendingDeletion` | `warning` | `tenants.state.pendingDeletionDetail` — "Deletes in {days} · {date}" |
| `Deleted` | `neutral` | `tenants.state.deletedDetail` — "Deleted {date}" |

No badge invents a new color — all six map onto the five semantic categories `components.md` §12 already defines (`info`/`warning`/`success`/`danger`/`neutral`); `ProvisioningFailed` and `SchemaBlocked` both use `danger` because to an operator scanning for work they mean the same thing ("go look at this now"), and the detail line is what tells them apart, exactly the same division of labor the Attention strip uses to group them into one count.

## Toolbar

- **Search** — debounced, server-queried, scoped to tenant display name and key (same discipline as `components.md` §7's async lookup) — never a client-side scan across the fleet.
- **Filter chips** — State (multi-select, options grouped visually by the same severity ranks as the default sort), Region, Plan. Standard grid filtering (`components.md` §8); no filter here is bespoke to this screen.
- **Include deleted** — a toggle, not a filter chip, because it is off by default and changes what the *unfiltered* view means rather than narrowing an already-visible set — see "What this screen does about severity," point 3.
- **Saved views** — the grid component supports them generically; this spec ships exactly one, the system default "Needs attention first" described above, and does not invent a personal saved-view need nobody has asked for yet. An operator may still save their own per the component's normal behavior.
- **Column settings** — Tenant is pinned and cannot be hidden (same convention `list.html`'s bootstrap grid already uses for a grid's one always-identifying column); every other column may be hidden/reordered/resized per `components.md` §8.

## Permission

**Proposed, not locked** (same convention `SPEC-002` used for `organization.company.manage`): `platform.tenant.view` gates this screen. No ADR yet defines platform-level (as opposed to tenant-scoped) permission constants — `ADR-0010` is explicitly tenant/company scoped — so this is a naming proposal for whoever builds the operator console's authorization to confirm or rename. `SPEC-003-tenant-detail.md` proposes two further, narrower platform permissions for state-changing and destructive actions.

**No anti-enumeration collapsing here, unlike `SPEC-002`.** That spec's "unavailable" state deliberately hides *whether a resource exists in this tenant* from a caller who might not be allowed to know. There is no equivalent ambiguity to protect on an operator console: the console itself is Aurora-internal, its existence is not secret from anyone who can reach its host, and there is no second tenant whose presence/absence this screen could leak. An operator lacking `platform.tenant.view` simply sees a plain "you don't have permission" state naming the permission — the ordinary case, not the SPEC-002 special case — because naming it here tells the caller nothing about any tenant's data.

## Locale and formatting

| Value | Locale-formatted? |
|---|---|
| `Created`, `Last activity` | **Yes** — shared date/date-time formatter. en `9/14/2026`; sv-SE `2026-09-14`. |
| `{days}` in the pending-deletion detail, footer counts, Attention-strip counts | **Yes** — shared number formatter (en `1,204`; sv-SE `1 204`), same discipline `SPEC-002` established for its footer count. |
| `{relativeTime}` ("3 minutes ago") | **Yes** — via the locale's relative-time formatting (`Intl.RelativeTimeFormat` in this static prototype; `.NET`'s culture-aware equivalent server-side per ADR-0022). |
| Region code, tenant key | **No.** Infrastructure identifiers, rendered verbatim, monospace, never translated — same rule `SPEC-002` applies to `{permission}`. |
| Display name | **No.** User-entered/operator-entered data, rendered verbatim. |

## States

| State | What the operator sees |
|---|---|
| **Default** | As laid out above. |
| **Loading** | Skeleton rows; the Attention strip is **absent**, not skeletal (see §19's own rule) until its count query resolves. |
| **Empty — true empty** | Zero tenants ever provisioned — a legitimate, if rare, state for a brand-new environment (not a guarantee violation the way an empty Companies list would be, since nothing here promises a tenant exists). Tone is neutral, not encouraging: `tenants.emptyHeading`/`Body` explains that provisioning happens through the internal provisioning API/harness, not a button on this page — there is deliberately no "+ New tenant" call to action here (see "Scope," out). |
| **Empty — filtered to nothing** | Standard grid empty-filtered state (`components.md` §8) — "No tenants match your filters," with "Clear filters" as the one-click fix. |
| **Error** | Retry affordance inline where the grid would be; the toolbar and Attention strip's last-known counts stay visible but marked stale, per `components.md` §8's "toolbar stays usable" rule. |
| **Missing permission** | See "Permission" above. |
| **Long values** | A display name at the same length ceiling `SPEC-002` set for Company (200 characters) wraps in the Tenant cell; the secondary key line does not wrap (keys are short, generated identifiers) but scrolls horizontally within its own line if a future package-driven format ever produces a longer one. |
| **Thousands of rows** | The realistic case here, unlike `SPEC-002`'s companies list — ADR-0007 §6 discusses fleet sizes in the thousands to tens of thousands. This is exactly what the Data grid component (`components.md` §8) exists for: server paging (default 50/page), row virtualization, server-side sort/filter. Nothing about this screen's severity mechanisms changes at scale — the Attention strip's counts and the severity sort are both server-computed over the full fleet, never just the current page, so an operator on page 1 still sees an accurate "2 need action" even if both are on page 30. |

## Keyboard

| Action | Key |
|---|---|
| Standard shell navigation | Per `app-shell.md`'s operator console section |
| Skip past the (absent, in this milestone) shell chrome to the content | `Tab` to "Skip to main content," then `Enter` — the skip-link shell guarantee applies here too even though there is no nav rail yet, because the top bar itself still precedes the content |
| Activate an Attention-strip segment | `Tab`, `Enter`/`Space` — applies the filter and moves focus into the grid's filter-chip region (§19) |
| Open a tenant | `Tab` to the row (rows are focusable, `Enter` opens `SPEC-003-tenant-detail.md` for that tenant), or click |
| Grid sort/filter/column keyboard model | Identical to `components.md` §8's own keyboard navigation section — this grid does not redefine it |
| Toggle "Include deleted" | `Tab`, `Space` |

## Accessibility notes

- The Attention summary strip is a landmark region with a localized accessible name (`tenants.attentionLandmarkLabel`) **only while it has content** — the `role` and `aria-label` are applied and removed together with the element's `hidden` state, in one code path, specifically so an empty landmark can never linger in the accessibility tree while looking gone to a sighted reviewer (Design-system finding 1). Each segment is a real `<button>` whose accessible name includes the count and label, never color/icon alone (§19).
- The grid's `State` column groups a Status badge with a text detail line in the same cell; both are read by a screen reader as ordinary cell content — no extra ARIA is needed beyond what `components.md` §8's grid semantics already require, because nothing here is presented as an icon-only or color-only signal.
- The severity-first default sort is announced the same way any grid sort state is (`components.md` §8) — an `aria-sort` value on the effective sort column where one exists; where the default view has no single-column equivalent (it is a compound rank, not one column), the toolbar's view selector reads "Needs attention first" as its accessible name, so a screen-reader user knows *that* an ordering choice is active even where it can't be expressed as a single `aria-sort`.
- `Include deleted` is a real checkable control (`role="switch"` or a native checkbox, not a styled `<div>`), labeled, and its state change re-queries and announces the new row count via the same polite live region toasts use.

**Named against WCAG 2.2 AA, per criterion, rather than asserted:**

| SC | How this screen meets it |
|---|---|
| 1.3.1 Info and Relationships | Real `<table>`/`<th scope="col">`/`<caption>` markup, exactly like `SPEC-002-companies-list.md`'s own correction; the Attention strip is a named landmark, not a `<div>` soup. |
| 1.4.1 Use of Color | Every state is identified by a text label (badge text + detail line), never by badge color alone — the same rule `components.md` §12 already states for Status badges generally, exercised here at its sharpest point (`danger` used for two different states, told apart only by their detail-line text). |
| 1.4.3 / 1.4.11 Contrast | Badge and detail-line colors are existing, audited `tokens.md` pairs (`danger`/`danger-muted`, `warning`/`warning-muted`, `text-muted` on `surface`) — no new color pair is introduced by this screen, so no new audit row is needed; the detail line specifically uses `text-muted` (6.30:1 light / 7.47:1 dark on `surface`), never `text-subtle`, per the correction `SPEC-002` already made to this design system for exactly this kind of "real content, not a placeholder" text. |
| 2.4.6 Headings and Labels | The Attention strip's accessible name ("Needs attention") and every column header are descriptive, not generic ("Column 2"). |
| 4.1.2 Name, Role, Value | The severity-first view is exposed via the toolbar's own accessible label, not only a visual arrow, since it can't be expressed as a single column's `aria-sort`. |
| 4.1.3 Status Messages | The Attention strip's appearance/count changes, and the "Include deleted" toggle's row-count change, are both announced via a polite live region — the same mechanism `components.md` §15 already requires for toasts, reused here rather than reinvented. |

## Resource keys

English is the base locale; sv-SE demonstrates the layer is real, per Principle 6 and `ADR-0022`. New prefix `tenants.*`, plus `tenants.state.*` (shared with `SPEC-003-tenant-detail.md` and `SPEC-003-tenant-offboarding.md` — one enum, one set of display strings, per Principle 4). `tenants.demoPanelTitle` (used identically across all three of this task's prototypes' "Prototype controls" panels) is scaffolding, same as `companies.demo*` in `SPEC-002` — it does not ship.

| Key | en | sv-SE | Where |
|---|---|---|---|
| `tenants.pageTitle` | Tenants — Operator Console · Aurora ERP | Klienter — Operatörskonsol · Aurora ERP | `<title>` |
| `tenants.heading` | Tenants | Klienter | `<h1>` |
| `tenants.tableCaption` | All tenants | Alla klienter | Hidden `<caption>` |
| `tenants.colTenant` | Tenant | Klient | Column header |
| `tenants.colState` | State | Status | Column header |
| `tenants.colPlan` | Plan | Plan | Column header |
| `tenants.colRegion` | Region | Region | Column header |
| `tenants.colSchema` | Schema | Schema | Column header |
| `tenants.colCreated` | Created | Skapad | Column header |
| `tenants.colLastActivity` | Last activity | Senaste aktivitet | Column header |
| `tenants.searchPlaceholder` | Search name or key | Sök namn eller nyckel | Search box |
| `tenants.filterState` | State | Status | Filter chip label |
| `tenants.filterRegion` | Region | Region | Filter chip label |
| `tenants.filterPlan` | Plan | Plan | Filter chip label |
| `tenants.includeDeleted` | Include deleted | Inkludera raderade | Toggle label |
| `tenants.defaultView` | Needs attention first | Kräver åtgärd först | View selector |
| `tenants.attentionLandmarkLabel` | Needs attention | Kräver åtgärd | Attention strip's `aria-label` (assistive-only — see Design-system findings, #1) |
| `tenants.attentionNeedsAction` | {count} need action | {count} kräver åtgärd | Attention strip segment |
| `tenants.attentionPendingDeletion` | {count} pending deletion, soonest in {days} | {count} väntar på radering, snarast om {days} | Attention strip segment |
| `tenants.footerCount` | Showing {rangeStart}–{rangeEnd} of {total} tenants | Visar {rangeStart}–{rangeEnd} av {total} klienter | List footer |
| `tenants.loadingLabel` | Loading tenants… | Läser in klienter… | Loading caption |
| `tenants.emptyHeading` | No tenants yet | Inga klienter än | True-empty state |
| `tenants.emptyBody` | Tenants are created through the provisioning API, not from this screen. Once the first tenant is provisioned, it appears here. | Klienter skapas via etableringsgränssnittet, inte från den här sidan. Så snart den första klienten är etablerad visas den här. | True-empty state |
| `tenants.filteredEmptyHeading` | No tenants match your filters | Inga klienter matchar dina filter | Filtered-empty state |
| `tenants.clearFilters` | Clear filters | Rensa filter | Filtered-empty state |
| `tenants.errorHeading` | We couldn't load tenants | Vi kunde inte läsa in klienter | Error state |
| `tenants.retry` | Retry | Försök igen | Error state |
| `tenants.unavailableHeading` | You don't have permission to view tenants | Du har inte behörighet att visa klienter | Missing-permission state |
| `tenants.unavailableBody` | This requires the {permission} permission. Contact a platform administrator. | Det här kräver behörigheten {permission}. Kontakta en plattformsadministratör. | Missing-permission state |
| `tenants.state.provisioning` | Provisioning | Etablerar | Status badge |
| `tenants.state.provisioningDetail` | Step {step} of 9 | Steg {step} av 9 | Detail line |
| `tenants.state.provisioningFailed` | Provisioning failed | Etablering misslyckades | Status badge |
| `tenants.state.provisioningFailedDetail` | Failed after {attempts} attempts | Misslyckades efter {attempts} försök | Detail line |
| `tenants.state.active` | Active | Aktiv | Status badge |
| `tenants.state.activeDetail` | Last activity {relativeTime} | Senaste aktivitet {relativeTime} | Detail line |
| `tenants.state.activeDetailNever` | No activity yet | Ingen aktivitet än | Detail line |
| `tenants.state.suspended` | Suspended | Avstängd | Status badge |
| `tenants.state.suspendedDetail` | Suspended {relativeTime} — offboarding | Avstängd {relativeTime} — avveckling | Detail line |
| `tenants.state.schemaBlocked` | Schema blocked | Schema blockerat | Status badge |
| `tenants.state.schemaBlockedDetail` | Blocked — schema v{version} is below minimum v{minimum} | Blockerad — schema v{version} är under lägsta tillåtna v{minimum} | Detail line |
| `tenants.state.exporting` | Exporting | Exporterar | Status badge |
| `tenants.state.exportingDetail` | Export in progress | Export pågår | Detail line |
| `tenants.state.pendingDeletion` | Pending deletion | Väntar på radering | Status badge |
| `tenants.state.pendingDeletionDetail` | Deletes in {days} · {date} | Raderas om {days} · {date} | Detail line |
| `tenants.state.deleted` | Deleted | Raderad | Status badge |
| `tenants.state.deletedDetail` | Deleted {date} | Raderad {date} | Detail line |

## Acceptance criteria

**[M]** = machine-checkable (bUnit / Playwright / axe-core in CI). **[H]** = needs a human pass. **19 machine-checkable, 4 human.**

**Severity mechanisms**

1. **[M]** With at least one `ProvisioningFailed` or `SchemaBlocked` tenant and at least one `PendingDeletion` tenant in the result set, the Attention strip renders exactly two segments, each showing a count equal to the true count of its category in the *whole filtered fleet*, not just the current page. *Fails if the count reflects only the fetched page.*
2. **[M]** With zero tenants in both attention categories, the Attention strip element is `hidden` **and** carries no `role` or `aria-label` — not merely empty of visible content. *Fails if a "0 need action" segment renders, and (this is the counterexample a purely visual check would miss) fails if an empty, still-`role="navigation"`-and-named container remains in the accessibility tree — a landmark with nothing in it is still "rendered" to a screen reader even when it looks absent to a sighted reviewer.*
3. **[M]** Activating an Attention-strip segment applies the matching state filter to the grid and moves focus into the toolbar's filter-chip region.
4. **[M]** Under the default view, row order matches the five-rank severity order defined above, with `PendingDeletion` sub-ordered by ascending `deletion_due_at` and `Provisioning`/`Exporting` sub-ordered by ascending start time. *Fails if any rank is alphabetical instead of severity-first, or if a later rank precedes an earlier one.*
5. **[M]** Clicking a column header re-sorts the grid by that column and replaces the "Needs attention first" view label with the column's own `aria-sort` state; "Reset to default" restores the severity order.
6. **[M]** With "Include deleted" unchecked (the default), no `Deleted` row appears in the result set or in the footer's `{total}`; checking it includes them and updates `{total}` accordingly.

**Grid mechanics and formatting**

7. **[M]** Every row's State cell renders both a Status badge and a state-specific detail line; the detail line is styled `text-muted`, never `text-subtle`. *Fails if the detail text fails the 4.5:1 contrast check.*
8. **[M]** Per-column, per-state, exactly this and nothing looser — **the previous wording of this criterion was itself wrong, falsified by this spec's own fixture, and is corrected here rather than quietly re-stated**: `Schema` renders `—` for `Provisioning` **and** `ProvisioningFailed` (neither has reached `Tenant.Activate`, which is the only thing that sets `core_schema_version`) and for `Deleted` (a tombstone retains no schema version at all), and a real value for `Active`, `Suspended`, `SchemaBlocked`, `Exporting` and `PendingDeletion`. `Plan` and `Region` render a real value for **every state except `Deleted`** — including `Provisioning`/`ProvisioningFailed`, since both are set at reservation (`Tenant.Reserve`), not at activation — and render `—` for `Deleted`, whose catalog row is tombstoned to "id, key and dates only" (ADR-0007 §11.4). *Fails if `Plan`/`Region` are blanked for `Provisioning`/`ProvisioningFailed`, if `Schema` is non-blank for `ProvisioningFailed` or `Deleted`, or if `Plan`/`Region` are non-blank for `Deleted` — the last two are exactly the two ways the previous version of this criterion was falsifiable against this spec's own fixture (a `ProvisioningFailed` row with a real `Schema` value, and a `Deleted` row with a real `Plan`/`Region`), so a test written from the old wording would have failed against correct code and "fixed" it into a lie.*
9. **[M]** The footer's `{total}` and the two Attention-strip counts are each produced by the locale number formatter, not a literal — verified by rendering under two locales with different grouping separators and asserting the two renders differ only in punctuation, never in digits.
10. **[M]** `Created` and `Last activity` render through the shared date/date-time formatter; a null `Last activity` renders `tenants.state.activeDetailNever`, never a blank cell or "Invalid Date."
11. **[M]** No row-level checkbox column exists anywhere in this grid's markup, in any state.
12. **[M]** Region and tenant-key values are rendered `monospace` and identical across every tested locale (proving they bypass the string-translation layer entirely, per the design rule that identifiers are never translated).

**States**

13. **[M]** The true-empty state renders no "+ New tenant" control anywhere on the page.
14. **[M]** The filtered-to-empty state's primary action is "Clear filters," never a create action.
15. **[M]** The error state leaves the toolbar and any previously-rendered Attention strip interactive (marked stale, not removed).
16. **[M]** The missing-permission state names `platform.tenant.view` in its body text and renders no table, no footer count and no Attention strip.

**Localisation and access**

17. **[M]** Rendered under a pseudo-locale whose every value carries a marker prefix, **every user-facing string on this screen — visible text and assistive-only text (`aria-label`, `role`-bearing landmark names, live-region announcements) alike** — carries the prefix except region codes and tenant keys (which must **not** carry it, proving they bypass translation on purpose rather than by omission). *The population was widened from "every visible string" specifically because a visible-only population is exactly what let the Attention strip's hard-coded `aria-label="Needs attention"` (finding, below) through: the string was never on screen for a pseudo-locale sweep to find, since a `data-i18n` scan only ever looked at `textContent`. That is a population gap in the check, not a verdict gap in what it found — the mechanism was sound, it was just never pointed at assistive-only attributes. See the Attention strip's own row above (AC 2) and "Design-system findings" below.*
18. **[M]** axe-core reports zero violations on the default state, the Attention-strip-active state, and the missing-permission state.
19. **[M]** Every interactive element in the toolbar and grid (search, filter chips, include-deleted toggle, Attention-strip segments, column headers, rows) is reachable and operable by keyboard alone, in visual reading order.

**Human pass**

20. **[H]** Screen-reader pass (NVDA or VoiceOver): the Attention strip's presence/absence and its segment counts are discoverable without visual scanning; a row's badge + detail line together read as one coherent sentence, not two disconnected fragments.
21. **[H]** With every en string doubled (pseudo-locale length test), no column header truncates and the Attention strip wraps to a second line rather than clipping a segment.
22. **[H]** At 3,000+ simulated rows with the default view active, scrolling and re-sorting feel responsive (no perceptible jank) — a real performance judgment the automated suite can't make, distinct from the correctness assertions above.
23. **[H]** Dark and light themes both read correctly at `compact` and `comfortable` density, including the `danger`-badge/`text-muted`-detail-line pairing in the `SchemaBlocked`/`ProvisioningFailed` rows specifically (the two states this screen is built to make impossible to miss).

## Design-system findings

Written out rather than designed around, per the project rule that a check is only as good as the last link it follows.

**1. The Attention strip's landmark survived being "empty."** The prototype's first draft set `role="navigation"` and a hard-coded `aria-label="Needs attention"` directly in the markup and only cleared the strip's *contents* when both counts were zero — leaving a named, empty landmark in the accessibility tree exactly when `components.md` §19 requires nothing be rendered at all. A sighted reviewer would never catch this (the strip visibly disappears), which is precisely why it survived a design-direction review. Fixed: the container starts `hidden` with no `role`/`aria-label` in the markup, and `renderAttentionStrip()` adds `hidden = false` + `role` + a localized `aria-label` together only when there is something to show, removing all three together otherwise — one code path, not "clear the text and hope."

**2. AC 17's population was "every visible string," and that is exactly what missed finding 1.** A pseudo-locale sweep that only walks `textContent`/`data-i18n` targets will never see a hard-coded `aria-label`, because that attribute is never on screen for the sweep to read. The mechanism itself was fine; it was pointed at the wrong population. AC 17 above is now widened to "every user-facing string, visible or assistive," and the same widening applies to `SPEC-003-tenant-detail.md`'s and `SPEC-003-tenant-offboarding.md`'s equivalent criteria.

**3. `select.view-select` was a bootstrap prototype's private style, not a shared one.** `list.html` (pre-`DESIGN-001`) defined it in its own `<style>` block rather than `app.css`; this task's `tenant-list.html` would have been the second file to need it and the second to silently duplicate it had this not been caught while cross-checking every class this prototype uses against a defined selector. Promoted to `app.css`.

## To route (outside `docs/design/`)

- **project-manager:** this whole screen area has no product spec. Recommend a SPEC formalizing who may hold `platform.tenant.view`/`.manage`/`.destroy` (proposed in `SPEC-003-tenant-detail.md`) and what, if anything, is audited beyond what ADR-0007 §9.2's `catalog.operator_audit_event` already implies.
- **architect:** no ADR currently defines platform-level (non-tenant-scoped) permission constants or where they're checked — `ADR-0010` is explicitly tenant/company scoped. The three permission names proposed across this spec and `SPEC-003-tenant-detail.md` need a real home.
- **architect:** confirm whether the Attention strip's counts and the severity sort can be computed as a single indexed query against `catalog.tenant` at fleet scale (tens of thousands of rows) without a full-table scan on every page load — this spec assumes they can, but hasn't seen the query plan.

## What we borrowed and why

The severity-first default list ordering, paired with a small clickable "what needs you" summary above the grid, is the same shape PagerDuty's incident list and Datadog's monitor list both use: most operational lists are mostly fine, and the job of the list is to get the operator's eyes onto the minority that isn't, fast, without asking them to build a filter first. The "hide the terminal/resolved state by default, behind a toggle rather than a hard removal" pattern mirrors how GitHub hides closed issues/merged PRs by default in a list view while keeping them one click away — a resolved item is a real fact worth being able to find, but it should never compete with an open one for attention.
