# Screen spec — First-run landing (Home)

Status: ready · Author: ui-designer · Date: 2026-09-11
Backlog: `DESIGN-01` (this task) · Product spec: `../../product/specs/SPEC-001-tenant-provisioning-and-first-login.md` (AC-5, BR-3)
Architecture: `../../decisions/ADR-0010-authorization-roles-and-permissions.md` (rules 3, 4, 10)
Prototype: `../prototypes/first-run-landing.html` (open directly in a browser — no build step, no network fetch)
Components referenced: App shell (`app-shell.md`), Status badges (`components.md` §12), Empty states (§17, "true empty" and "no permission" variants), Buttons (§1), Tenant/company switcher (§18, including this spec's addition below)

## Purpose

Make the moment right after provisioning read as *a system that is ready*, not *a system with nothing in it*. Per SPEC-001 AC-5, at this point exactly one Company exists, roles and the permission catalogue are seeded, and the signed-in administrator holds a Membership scoped to every company — and nothing else exists at all: no other record of any kind. The screen's whole job is to make that specific, honest state feel like a starting line rather than an error.

## User

The tenant's first administrator, immediately after their first successful sign-in (`SPEC-001-sign-in.md`). This is also, architecturally, just the tenant's ordinary **Home** route — there is no separate one-time "welcome wizard" flag anywhere in SPEC-001's data model (BR-3 lists what provisioning seeds; a "has completed onboarding" flag is not one of them). What makes it look like a first-run experience is simply that the record counts really are at their seeded floor; a returning user on day 30 sees the same route rendering differently because there is more data by then. This is deliberate — inventing a one-time flag not specified anywhere would be a guess this spec should not make.

## Task flow

1. User signs in (previous screen) and lands on Home.
2. The page's own queries (company count, role/permission count, the current user's permissions) resolve. While they do, the page shows a loading skeleton rather than a blank page (Principle 3).
3. On success, the page renders the "what's already set up" summary and one primary next action.
4. The user clicks "View companies" and reaches `SPEC-002-companies-list.md`'s screen — the one place in this milestone with real, navigable tenant data.

## Layout

```
┌───────────────────────────────────────────────────────────────────────────┐
│ ☰  Aurora ERP     [Nordwind Group ▾]                    🔍 Search    ⌘K   │
├───────┬───────────────────────────────────────────────────────────────────┤
│ Recent│  Welcome to Nordwind Group.                    [Light|Dark|System]│
│ (none │  Your system is set up and ready. Here's what's already in place  │
│  yet) │  — and where to go next.                                          │
│       │  ┌─────────────────────────────────────────────────────────────┐  │
│ Home  │  │ What's already set up                                       │  │
│ Master│  │ ✔ Done  Default company created      Nordwind Group, 2026‑09‑11│
│ data  │  │ ✔ Done  Roles and permissions ready  12 roles, 128 perms    │  │
│ Sales │  └─────────────────────────────────────────────────────────────┘  │
│ ...   │  ┌─────────────────────────────────────────────────────────────┐  │
│       │  │ ✓  Nothing else exists yet — and that's expected            │  │
│       │  │    Aurora ERP never adds sample data. Everything else       │  │
│       │  │    starts with you.                                         │  │
│       │  └─────────────────────────────────────────────────────────────┘  │
│       │  [ View companies → ]   Coming soon: inviting teammates, ...      │
└───────┴───────────────────────────────────────────────────────────────────┘
```

Uses the standard app shell (`app-shell.md`) with no page-specific chrome beyond the content area. The nav rail's "Recent" section is honestly empty (a muted "Nothing here yet" line, not omitted outright — an absent section reads as a layout bug, an explained empty one does not).

## The tenant/company switcher shows one name, not two

This tenant has exactly one Company, and per SPEC-001 BR-3 that Company was named *from the tenant's own display name* at provisioning — so, immediately after provisioning, the tenant name and the company name are always identical. Showing "Nordwind Group ▸ Nordwind Group" would read as a bug. This spec adds one rule to `components.md` §18's existing "single-company tenant" row: when the two names are identical, the switcher shows the name once; the moment either is renamed, or a second company is added, the two-segment `Tenant ▸ Company` form reappears automatically. This is the one component change a developer needs to pick up alongside this screen.

## What's already set up (why these two rows, not a longer list)

Only the two things BR-3 actually guarantees exist and are meaningfully "yours": the default Company, and the seeded roles/permissions (framed as "you hold the Administrator role" so the abstraction — a role — is grounded in what it means for this specific person). BR-3 also guarantees a working sign-in credential, but that is not worth restating as a checklist item since the user is, by definition, looking at this screen because it already worked.

Each row uses the Status badge component (§12) with the `success` semantic ("Done") — reused exactly as specified, never a bespoke checkmark shape invented for this screen (Principle 4).

## Locale and formatting (Principle 6)

This tenant has installed **zero Country Packages** — a deliberate choice for this screen specifically (valid per ADR-0008: "a tenant may enable zero or more"), so that nothing on it has to assume a jurisdiction, a legal-entity suffix, or a second UI locale being genuinely on offer. Its user menu therefore shows "Language: English" as a static value, not a picker — there is nothing else enabled to pick (`app-shell.md` → User menu: base English plus whatever installed Country Packages contribute). This is itself a real, specified state worth showing once, since every other prototype in this set happens to show a tenant with at least one package.

Because a genuine locale picker is not legitimate for *this* tenant, the date/number formatting demonstration on this screen is a clearly labeled **prototype review control** ("Preview locale (review only)"), not presented as an in-product feature — distinct from the sign-in screen's real, in-story language picker. What it demonstrates is real, though: the "created" date is stored once as an ISO instant (`data-created-at="2026-09-11T09:14:00Z"`) and rendered through the same `formatDateTime()` locale-aware call every time, in both locales:

| Locale | `firstRun.companyDoneMeta` rendered |
|---|---|
| en | Nordwind Group, created 9/11/2026, 9:14 AM |
| sv-SE | Nordwind Group, created 2026-09-11 09:14 |

Note the three simultaneous differences: month/day order, 12-hour vs. 24-hour clock, and separator — none of which is a literal template in the code, all of which come from the same call site with a different locale tag (see `prototypes/i18n.js`'s `formatDateTime`).

## States

**Empty (true empty — this screen's normal state).** As designed above. This is not a special case to handle; it is the only state this screen shows for a genuinely fresh tenant, which is why "make emptiness feel like a starting point" is the whole design brief rather than an edge case within a larger design.

**Loading.** A skeleton (new shared `.skeleton` class in `prototypes/app.css` — several `components.md` entries already call for a skeleton state; this is the first prototype to render one concretely) replaces the "what's set up" panel and the reassurance panel while the company/role/permission counts are being fetched. The heading and subhead render immediately since they need no server data beyond the already-resolved tenant name.

**Error.** If the setup-status query fails, the two content panels are replaced by a single error panel (`components.md` §17 "Error" variant: retry action, visually distinct from true-empty) — the page header and nav stay usable, consistent with the Data grid component's "error" rule that surrounding chrome never goes blank just because one query failed.

**Permission-denied.** This is the honest edge case the brief requires every screen to specify, even though it cannot occur for the *specific* first-time administrator this screen is written for (they always hold the seeded Administrator role, BR-3). It becomes real the moment a second, more limited role reaches this same Home route later: a user whose role grants no company-viewing permission at all sees a plain welcome with no setup checklist and no "View companies" action — per `components.md` §17's "No permission" rule, stating plainly and not offering an action the viewer is not authorized for, rather than showing a grayed-out, unusable version of the full screen.

**Long values.** The company name (bounded at 200 characters, SPEC-002 BR-2) wraps rather than truncates in the "what's already set up" row, matching this spec's own company-list screen's handling of the same field, so a long name is treated identically everywhere it appears (Principle 4).

**Thousands of rows.** Not applicable — this screen has no collection, by construction (its entire premise is that nothing else exists yet).

## Keyboard

| Action | Key |
|---|---|
| Standard shell navigation (nav rail, switcher, search, command palette) | As specified in `app-shell.md` |
| Reach the primary action | `Tab` to "View companies", `Enter`/`Space` to activate |
| Retry after an error | `Tab` to the Retry button, `Enter`/`Space` |

No document-specific keyboard model is needed — this is a read-only summary screen, not a data-entry surface.

## Accessibility notes

- The page's single `<h1>` is the welcome heading; "What's already set up" and the reassurance panel are each their own labeled section (`<h2>`/`<h3>`), so a screen reader user can jump between them with heading navigation rather than reading the whole page linearly.
- Each setup row's Status badge carries its own text ("Done"), never color alone, consistent with `components.md` §12's rule — this matters here specifically because green-on-white is the only visual signal a sighted user gets that something succeeded, and it must not be the *only* signal for anyone else.
- The empty nav "Recent" section has real, announced text ("Nothing here yet…"), not an empty, silently-collapsed list item that a screen reader would skip over without explanation.
- The loading skeleton is marked in a way that does not read its (meaningless) placeholder content aloud; the "Loading your tenant…" line above it is the only thing a screen reader announces during that state.
- The permission-denied variant's heading is the same "Welcome to {tenant}" as the normal state (not a bare "Access denied") — per `components.md` §17, stating things plainly without exposing whether further data exists, while still keeping the tone consistent with the rest of this screen's design goal (a limited-permission user is not being punished, just accurately scoped).

## What we borrowed and why

The "what's already set up, framed as done, not as a task list you're behind on" pattern is closer to Linear's or Notion's post-signup "here's your workspace" summary than to a step-gated onboarding wizard — deliberately, since SPEC-001 does not model onboarding as a sequence of steps the user must complete, only as facts that are already true. The explicit "this emptiness is expected, not a bug" panel borrows directly from how well-designed empty states in general (Component §17, and products like Linear's own empty backlog view) turn silence into a stated fact rather than leaving a blank area for the user to interpret unaided.
