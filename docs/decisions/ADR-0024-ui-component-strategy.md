# ADR-0024 — UI component strategy

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect (technology), ui-designer (design system)
- **Related:** ADR-0005 (Blazor Server), ADR-0022 (localization), `docs/design/components.md`, `docs/design/tokens.md`

## Context

`docs/design/` already specifies a complete, opinionated design system: tokens in `tokens.json`, 18 components with named anatomy and states, density rules, and a data grid whose non-negotiables include **server-side paging and sorting always**, **vertical and horizontal virtualization at 150 000 rows**, cell-level keyboard navigation, column pinning, saved views, and partial-failure reporting on bulk actions. WCAG 2.2 AA is required and every string is localized (ADR-0022).

The question is what we build on. This is architecturally significant because it is a dependency on the most-touched surface of the product, it has licence implications, and the data grid is the single most expensive UI component in any ERP.

## Options considered

| Option | Licence (verified 2026-09-11) | Pros | Cons |
|---|---|---|---|
| Commercial suites (Telerik, Syncfusion, DevExpress) | Commercial, per-developer | The most complete grids in the .NET ecosystem; would satisfy most of the grid spec on day one | Costs money and requires a sign-up — both **hard limits** in `CLAUDE.md`. Rejected without further evaluation |
| **MudBlazor 9.9.0** | MIT | Large community, broad component set | Material Design is a strong visual opinion that would fight `tokens.json` rather than consume it; overriding a Material theme to reach our density and chrome is a long, brittle job. Its grid does not meet the horizontal-virtualization or cell-cursor requirements anyway |
| **Microsoft.FluentUI.AspNetCore.Components 4.14.4** | MIT | Microsoft-maintained, good accessibility posture, `FluentDataGrid` supports `ItemsProvider` virtualization | Fluent is likewise a strong visual opinion; adopting it means adopting its design language or fighting it |
| **Radzen.Blazor 11.3.2** | MIT | The most capable permissively licensed grid: virtualization, frozen columns, grouping; styling is less opinionated | Still short of the spec (no cell-level keyboard cursor, no saved views); brings a large surface we would use a fraction of; the component library is a funnel for a commercial product, which is a maintenance-direction risk over a decade |
| **Own component library over plain Blazor + `Microsoft.AspNetCore.Components.QuickGrid` 10.0.12 as the grid engine** *(chosen)* | MIT, **part of ASP.NET Core**, same release cadence and support window as the framework | Zero design-system conflict — our tokens *are* the styling; QuickGrid is small, unopinionated, has an `ItemsProvider` server-paging model and an EF Core adapter; nothing to fight, nothing to theme around; supported as long as .NET 10 is | We build the grid chrome — toolbar, filter chips, column settings, saved views, pinning, bulk-action bar — ourselves. That is real work |
| Build absolutely everything, including virtualization | — | Total control | Re-implementing virtualization and an EF-aware items provider is work Microsoft has already done and maintains for free |

## Decision

**Build `Aurora.Web.Components`, an owned component library implementing `docs/design/components.md`, on plain Blazor plus the design tokens, using `Microsoft.AspNetCore.Components.QuickGrid` (+ its EF Core adapter) as the grid and `Virtualize` as the virtualization primitive.**

Rules:

1. **`tokens.json` is the only source of visual values.** No component hard-codes a colour, spacing, radius or type size. The tokens build step (`docs/design/tokens.build.py`) emits CSS custom properties; components consume those.
2. **Every component is localized and accessible by construction** — no string literal in markup (fitness rule A1), labels associated with controls, keyboard operability, focus management. Accessibility is a component-level contract, not a screen-level afterthought, because otherwise every screen re-litigates it.
3. **The grid is one component used everywhere** (`docs/design/components.md` §8). There is no "simple table" escape hatch; a second table component is how the non-negotiables get bypassed.
4. **Server paging is not optional.** The grid takes only an `ItemsProvider`-shaped source with a bounded page size (default 50, hard maximum 200) and a separate count estimate. There is no API surface that accepts a materialised `IEnumerable` of business data — the type system prevents the "it's only a small tenant" shortcut that ADR-0005 rule 3 warns about.
5. **A time-boxed spike precedes the full grid.** Before committing to the whole chrome, prove QuickGrid + `Virtualize` sustains the designer's stated worst case — **150 000 rows, 40 columns, over a SignalR circuit** — within the interaction budget (p95 < 500 ms, `overview.md` §4). If it cannot, reopen this ADR with measurements rather than opinions; Radzen is the fallback and its licence is already cleared.
6. **Components live in `Aurora.Web.Components` and may reference only `.Contracts` assemblies** — they receive DTOs, never aggregates (ADR-0005 rule 1).
7. **bUnit tests per component** cover states, localization and the accessibility attributes we can assert mechanically (`testing-strategy.md` §9).

## Consequences

- Positive: no licence cost, no design-system conflict, and the visual language in `docs/design/` is implemented directly rather than approximated through someone else's theme.
- Positive: the grid engine is part of the framework, so its support window and its security patching are the framework's — the lowest-maintenance dependency available for this job.
- Positive: rule 4 makes the most damaging Blazor Server performance mistake structurally impossible rather than merely discouraged.
- **Negative, and the largest single UI cost in the project: we are building the grid chrome ourselves.** Saved views, column settings, pinning, filter chips and the bulk-action bar are each a meaningful piece of work. This must appear in the roadmap as its own slice, not as a line item inside "build the sales order screen". Flagged to the project-manager explicitly.
- Negative: no vendor to call when a grid edge case misbehaves. Mitigated by the spike in rule 5 and by the fallback being pre-cleared.
- Negative: our component set will be smaller than any of the libraries above. That is intended — 18 well-made components that match the product beat 80 that do not.

## Revisit when

The spike in rule 5 fails its budget, a component we need turns out to be genuinely generic and expensive (a rich text editor, a charting library — adopt a permissively licensed one for that single job rather than a whole suite), or maintaining the component library measurably slows feature delivery for two consecutive milestones.
