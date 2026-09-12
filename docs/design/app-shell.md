# App shell — Aurora ERP

The shell is the persistent chrome every module renders inside: navigation, the tenant/company switcher, global search, command palette, notifications, user menu, and the connection-state affordances Blazor Server requires. It is built once and never reimplemented per module (Principle 4).

## Layout (desktop, ≥1200px — primary target)

```
┌───────────────────────────────────────────────────────────────────────────┐
│ ☰  Aurora ERP     [Nordwind Group ▸ Nordwind Trading AB ▾]   🔍 Search  ⌘K │  ← top bar, 48px
├───────┬───────────────────────────────────────────────────────────────────┤
│       │  Sales Orders                                    [+ New order]    │
│ N     │  ───────────────────────────────────────────────────────────────  │
│ a     │  [toolbar: filters, saved views, column settings]                 │
│ v     │  ┌─────────────────────────────────────────────────────────────┐  │
│       │  │  data grid ...                                              │  │
│ r     │  │                                                              │  │
│ a     │  └─────────────────────────────────────────────────────────────┘  │
│ i     │                                                                    │
│ l     │                                                                    │
├───────┴───────────────────────────────────────────────────────────────────┤
│  🔌 status · Bell 🔔 3 · Avatar ▾                                          │
└───────────────────────────────────────────────────────────────────────────┘
```

- **Top bar** (48px, `surface`, `elevation-1` on scroll only — flat when the page is at the top so it doesn't compete visually at rest): app mark + collapse toggle for the nav rail, the tenant/company switcher (always here, never anywhere else), global search, command-palette entry point, notification bell, user menu.
- **Nav rail** (left, 240px expanded / 56px icon-only collapsed, `surface`, `border` right edge): module list (Sales, Purchasing, Inventory, Manufacturing, Finance, etc. — module set depends on tenant's licensed modules), each expandable to its sub-areas. Collapses to icon-only via the top-bar toggle or automatically on tablet widths. State remembered per user.
- **Content area** (`canvas` background, content panels `surface`): page header (title, primary action, breadcrumb if nested) + the page's own layout (grid, form, master-detail, dashboard).
- **Status strip** (bottom of the shell, thin, only visible when there's something to say — connection status, background job progress — otherwise not rendered, so it never wastes permanent vertical space for an idle state).
- **Skip link** — the first focusable element on *every* shell screen is a visually-hidden-until-focused "Skip to main content" link targeting the content area, because the nav rail repeats roughly ten links ahead of the content on every page (WCAG 2.2 SC 2.4.1 Bypass Blocks, Level A). It is a shell guarantee, not a per-screen decision: a screen that has to remember to add one is a screen that will forget. Added by `DESIGN-001`; `prototypes/app.css` carries the shared `.skip-link` style and the string is `shell.skipToContent`.

## Module navigation

- Top-level modules in the nav rail map to bounded contexts (Sales, Purchasing, Inventory, Manufacturing, Finance, Master Data, Settings), each with its own sub-navigation (e.g., Sales → Orders, Quotes, Customers, Price Lists). A tenant only sees modules it has licensed/enabled.
- The active module and sub-area are always highlighted (`accent-muted` background + `accent` text on the active item) and reflected in the URL, so a refresh or a shared link lands on the same screen — routing is a first-class part of the shell contract, not an afterthought for a Blazor Server app (deep-linkability matters even though rendering happens server-side).
- **Recent items**: a "Recent" section pinned near the top of the nav rail (collapsible), showing the last ~8 records the user opened across any module (e.g., "SO-2041 — Contoso Wholesale," "Item — Steel Bracket 40mm"), each tagged with its type icon and, when relevant, its company — since a user may have recent items across companies once switching is used, and Principle 5 requires that context never be ambiguous even in a shortcut list.

## Global search

A single search box in the top bar, always present, distinct from the command palette (search finds *data*; the palette finds *actions and navigation*, though the palette also includes a "jump to record" mode that overlaps deliberately — redundant entry points to the same destination are fine, redundant *mental models* are not, so both are documented as leading to the same result screen). Search is scoped to the active company by default, with an explicit, visible toggle to widen it to "this tenant, all companies" — never a silent cross-company result set, per Principle 5. Results follow the same async, debounced, server-queried, paginated discipline as component 7 (async lookup) — never a client-side scan.

## Command palette

`Ctrl/Cmd+K` from anywhere in the shell. Full behavior spec lives in `components.md` §16. In the shell specifically: it is the fastest path to (a) navigate to any module/screen without using the mouse on the nav rail, (b) jump straight to a record by number/name, (c) invoke the tenant/company switcher itself (typing "switch company" or a company name surfaces it), and (d) trigger a small set of global actions ("New sales order," "New customer") that then route into the relevant module's create flow. It never bypasses a permission check or the company-switch confirmation — it is a faster route to the same guarded actions, not a side door around them.

## Tenant/company switcher

This is the single most safety-critical element in the shell, directly addressing the brief's structural risk: a tenant may have multiple companies, and posting to the wrong one must be hard to do by accident.

**Always-visible label**: the top bar never shows just a company name in isolation — it shows `Tenant name ▸ Company name` with a small colored badge (a stable per-company color + initial, assigned once and never reused for a different company within the same tenant, so a user builds a fast visual association: "the teal badge is always Nordwind Trading AB"). This label is present on every single screen in the shell, with no state in which it can scroll out of view (the top bar is fixed/sticky).

**Restated on the record**: beyond the shell, every document's own header restates its owning company in text (e.g., a sales order's page title area shows "Sales Order SO-2041 · Nordwind Trading AB"), so even a screenshot or a printed screen taken out of the shell's context still identifies the company unambiguously. This is deliberate belt-and-suspenders on top of the shell badge, per Principle 5 and Principle 2.

**Switching flow**:
1. Click the switcher (or invoke via command palette) → popover opens listing companies. If the user's account spans more than one tenant (e.g., an accountant with access to several client tenants), companies are grouped under their tenant with a clear separator — cross-tenant confusion is the same class of error as cross-company confusion and gets the same visual weight.
2. If the user has unsaved work anywhere in the current session (an open form, an in-progress document edit), switching is blocked by a confirmation interstitial naming what would be discarded. Otherwise the switch is immediate.
3. On switch, the entire shell content area re-renders scoped to the new company (nav counts, recent items filtered, any open list re-queried) with a brief loading state — never a partial re-render that leaves some panel showing stale data from the old company.
4. The switch itself is audit-logged (who, when, old company → new company) per the audit-logging standard in `CLAUDE.md` — this is treated as a security-relevant event, not a cosmetic UI action.

**Single-company tenants** (the common case for a small business) still render the full label — the guarantee that "you can always see which company you're in" must hold even when there's only one, so the affordance never has to be relearned the day a tenant adds a second company.

## Notifications

Bell icon in the top bar with an unread-count badge. Opens a panel (not a full page) listing recent notifications (approval requests, completed background jobs, mentions/assignments, system messages), each scoped visibly to its company where relevant (same badge convention as the switcher) since a user's notification feed can span companies even though their working context is single-company at a time. Read/unread state, mark-all-read, and a link to a full notifications history page for anything needing more than a skim. Real-time delivery arrives over the same SignalR circuit as the rest of the Blazor Server session — a dropped circuit (see below) also means notifications pause, and the panel makes that visible rather than silently going stale.

## User menu

Avatar/initials in the top bar corner → profile, preferences (including the grid density preference from `tokens.md`/`components.md` and light/dark theme choice), language (from the set of locales enabled by installed Country Packages plus base English), help/keyboard-shortcuts reference, and sign out. Not used for company switching — that ambiguity (is "switch company" a user-account setting or a document-context choice?) is exactly what a dedicated, always-visible switcher avoids.

## Responsive behavior

| Breakpoint | Nav rail | Master–detail | Data grid | Notes |
|---|---|---|---|---|
| **Desktop wide** ≥1440px | Expanded (240px), can be pinned collapsed by choice | Side-by-side, resizable split | Full column set, comfortable column widths | Extra width goes to breathing room, not more density (per `tokens.md`). |
| **Desktop** 1200-1439px | Expanded by default | Side-by-side | Full column set | Primary design target — every screen is designed to work well here first. |
| **Tablet** 834-1199px | Auto-collapses to icon-only (56px), expandable by tap, closes after selecting a destination | One pane at a time: list → tap a row → full-width detail with a back action | Lower-priority columns hide automatically (a documented per-grid priority order, not arbitrary), horizontal scroll available for the rest; grid defaults to `comfortable` density here since touch targets need the room | Command palette and search remain fully available; document line editor remains keyboard-first but every action also has a visible, tappable control since a tablet session may be touch-only. |
| **<834px (phone)** | Not a supported target for v1 | — | — | The shell degrades gracefully (nothing breaks) but is not a design target; no phone-specific layouts are speced yet — flag any phone usage discovered in the field back to this document before committing to support it. |

## Reconnection / offline banner (Blazor Server-specific)

Every interaction in this product is a round trip over a SignalR circuit. The shell owns a single, consistent way of surfacing that connection's health so no module has to invent its own — this is one of the most important contracts in this file precisely because it's invisible when it's working and critical when it isn't.

| Connection state | Shell behavior |
|---|---|
| **Connected** (normal) | No banner. Nothing shown — a healthy connection is not something the user needs to think about. |
| **Reconnecting** (circuit dropped, client is attempting to re-establish — Blazor Server's default behavior) | A persistent, non-dismissable banner appears at the very top of the shell (above the top bar), `warning` colors, e.g. "Reconnecting… your changes since [time] may not be saved." All editable controls in the content area — dialogs included — become **`readonly` plus `aria-disabled`**, not hidden and **not** natively `disabled`: the user's in-progress data stays visible, just frozen, and focus stays where it was. A circuit drops exactly when someone is mid-keystroke in a field, and the native `disabled` attribute on the element that currently holds focus moves focus to `<body>` (WCAG 2.2 SC 2.4.3) — the same contradiction `components.md` §1 resolves for buttons. Corrected by `DESIGN-001`; see `screens/SPEC-002-company-create.md`, finding 8. A subtle countdown/spinner communicates it's actively retrying, not stalled. |
| **Reconnected** | Banner briefly flips to `success` ("Reconnected") for ~2s, then disappears; controls re-enable. Any in-flight action that was pending when the circuit dropped is explicitly resolved one way or the other (re-confirmed success, or surfaced as failed and needing retry) — never left in permanent limbo. |
| **Reconnection failed / circuit permanently lost** (Blazor Server gives up retrying) | Banner escalates to `danger`, non-dismissable, with a single clear action: "Reload page." No editable control in the shell remains interactive at this point — a lost circuit with no server-side state backing it is not a state where any client-side interaction can mean anything, and pretending otherwise would be dishonest per Principle 3. |
| **Slow round trip** (connected, but a specific action is taking longer than expected — e.g., 2s+) | Not a shell-level banner; handled per-component via the loading states already specified in `components.md` (button spinners, grid loading bars) — a slow save is not the same problem as a dropped circuit and must not be visually confused with one. |

This banner behavior is a single shared component (`ConnectionStatusBanner`), not something each screen re-implements — consistent with Principle 4, and especially important here because a per-screen reimplementation is exactly how a dropped circuit could go unnoticed on one screen while correctly shown on another.

## Operator console (a separate shell, not this one)

Added by `DESIGN-002` for `screens/SPEC-003-tenant-list.md`, `SPEC-003-tenant-detail.md` and `SPEC-003-tenant-offboarding.md`. Everything above this section is the shell a **tenant's own users** see. The tenant-lifecycle screens are for **Aurora's operators** — internal staff managing every tenant in the fleet — and that is a different audience with a different composition root, per ADR-0029's note that "the operator console does not exist, and when it does it arrives as a separate factory type… resolvable only in the operator composition root," and per ADR-0007 §8's "operator console only" compensation guard. Reusing the shell above for it would be actively misleading, not just imprecise: that shell's entire top bar is built around one always-visible tenant/company identity (Principle 5), and an operator screen has no such identity — an operator is looking *at* every tenant, not working *inside* one. A screenshot that could be mistaken for either surface is itself a risk in a product whose worst failure mode is acting against the wrong tenant.

**What the operator console shell has, deliberately little:**

- **A top bar that cannot be confused with the tenant shell's**, `surface-sunken` background rather than `surface` (a persistent, unmistakable visual tell, not just a different word in the corner) with `Aurora ERP · Operator Console` as the brand mark, the signed-in operator's name/initials and sign-out on the right. **No tenant/company switcher** — there is no active tenant to restate, so this is not an omission of component 18, it is that component's precondition not existing here.
- **No global search box and no command palette in this milestone.** Both are real, useful ideas for an operator managing thousands of tenants, and both are deliberately deferred rather than half-specified: `SPEC-003-tenant-list.md`'s own toolbar search (scoped to tenant name/key, per that grid's own toolbar) covers this milestone's one real need. Extending the palette to jump across tenants is future work, flagged in that spec's routing section, not designed here.
- **No nav rail yet.** Three screens exist behind this shell (list, detail, offboarding); a left-hand module list for three destinations is chrome with nothing to organize. The list screen is the landing page (`/operator/tenants`) and the only way in from a sign-in redirect. The day a second operator surface ships (a migration-run console, a cluster-capacity view — both named as future work in ADR-0007 but not designed by this task), a nav rail returns to this document rather than being invented ad hoc by whichever screen needs it second — Principle 4 applies here as everywhere.
- **The same reconnection banner contract** (`ConnectionStatusBanner`, above), because this is still a Blazor Server `InteractiveServer` surface and a dropped circuit is exactly as real here as in the tenant shell — an operator mid-typed-confirmation on a destroy dialog when the circuit drops must see the same frozen-not-hidden, honestly-uncertain treatment as any other user, arguably more so given what the dialog is about to do.
- **Every other visual value — color, spacing, radius, elevation, motion, the Data grid, Status badges, Dialogs, Toasts, Forms — is the same design system**, `tokens.json` and `components.md`, unchanged. The operator console is a different shell around the same components, not a second design language; Principle 4 forbids a second grid or a second badge shape just because the audience changed.

**Permission model.** Every screen behind this shell requires a platform-level capability distinct from any tenant-scoped permission in `ADR-0010` (there is no tenant to scope it to, the same reasoning `SPEC-001` BR-1 applies to triggering provisioning itself). `SPEC-003-tenant-list.md` proposes concrete permission names; as with `organization.company.manage` in `SPEC-002`, these are naming proposals for the implementing developer and architect to confirm, not locked decisions.

**Resource keys owned at the shell level** (referenced, not redefined, by every screen behind this shell — the same relationship `shell.skipToContent` already has to every tenant-shell screen): `operator.brand` ("Aurora ERP · Operator Console" / "Aurora ERP · Operatörskonsol") for the top-bar wordmark, and `operator.signOut` ("Sign out" / "Logga ut") for the sign-out control. Sign-in/authentication for this console is out of scope for this task (see `SPEC-003-tenant-list.md`'s "Scope") and is assumed to exist, not designed here.

## What we borrowed and why

- **Persistent tenant/workspace identity in the header, never a settings-menu afterthought** — borrowed from Slack/Notion's always-visible workspace name and Google Workspace's persistent account badge; adapted here with mandatory restatement on the record itself, because the cost of getting it wrong in an ERP (a misposted financial document) is categorically higher than in a chat app (a message in the wrong workspace).
- **Icon-collapsible nav rail + command palette as a keyboard-first accelerant beside it, not instead of it** — borrowed from Linear, which proved a command palette can serve expert throughput without removing a discoverable, mouse-driven nav rail underneath it for everyone else (Principle 7).
- **A single, un-skippable connection-health banner above all page chrome** — borrowed from Google Docs' "you're offline" treatment and Figma's multiplayer-disconnect banner, both of which freeze/guard editing rather than silently letting a user "edit" against a connection that isn't really there — directly applicable to a Blazor Server circuit, which has the same "the UI can render but the server isn't actually listening" failure mode.
- **A visually distinct internal-tooling shell, unmistakable for the product it manages** — borrowed from how Stripe's and AWS's own internal operator/admin consoles are deliberately skinned so an engineer can never mistake a screenshot of the internal tool for the customer-facing product, even though both share an underlying component library; adopted here for exactly the same reason, and because Principle 5's "never make the user guess which company they are in" has an operator-console mirror image: never let an operator mistake "I am looking at everyone's data" for "I am inside one tenant."
