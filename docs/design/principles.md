# Design principles — Aurora ERP

These are the rules every screen spec, component and review in `docs/design/` is judged against. An ERP user spends their whole working day in this product, often 6-8 hours, doing repetitive high-volume tasks (entering invoice lines, matching payments, checking stock). At that exposure, small frictions compound into real cost and small trust failures compound into real risk (a wrongly-posted invoice, a payment run against the wrong company). Speed, density, clarity and trust beat decoration every time.

Each principle below has a concrete **Do** and **Don't** grounded in a real ERP screen, and notes what we borrowed from existing, well-regarded business software and why.

---

## 1. Density is a feature, not a compromise

A power user reading a sales order line list, a bank reconciliation grid, or a chart of accounts wants to see as many correct, scannable rows as fit on screen without scrolling — that is faster than a spacious card-per-item layout, and speed is the product for this audience. "Clean" for us means *legible at high density*, not *lots of whitespace*.

- **Do**: default data grids to the `compact` density (32px rows, 12px type, see `tokens.json` → `density.compact`), with a per-user toggle to `comfortable` (44px) for anyone who wants more breathing room or is on a touch/tablet session.
- **Don't**: ship marketing-site-style cards with 24px of padding around every field for a 200-line purchase order. That forces scrolling that a flat grid would not need, and it hides the row count from peripheral vision.
- **Borrowed from**: enterprise grids in Excel, Google Sheets and Airtable, and dense accounting tools (QuickBooks Desktop register view, Fortnox's list views) — all default to small row heights for transactional lists, and reserve generous spacing for forms and dialogs, not lists.

## 2. Every destructive or financial action is confirmed, reversible-by-audit, or both

Posting, voiding, and deleting are not equally dangerous, and the UI must not treat them as if they were. A misdirected post (wrong company, wrong period) is exactly the class of error this product must make structurally hard, per the multi-tenant/multi-company brief.

- **Do**: a "Post invoice" action shows the target company and period inline on the confirming control itself (button microcopy: `Post to Nordwind Trading AB — Sep 2026`, not just `Post`), and once posted, corrections happen only through a reversal document, never an edit — matching the immutable-ledger rule in `CLAUDE.md`. A destructive, non-reversible action (hard-delete a draft, deactivate a user) requires a typed confirmation of the record's name in the dialog.
- **Don't**: a bare `Delete` icon-button in a grid row with no confirmation, or a generic "Are you sure?" dialog that does not restate what is about to happen and where.
- **Borrowed from**: SAP Fiori's object-page "delete requires typed confirmation for financial documents" pattern, and GitHub's "type the repo name to confirm" pattern for irreversible actions — both make the cost of a slip proportional to the blast radius, and both refuse to let muscle-memory clicking substitute for reading.

## 3. The system is always honest about where the data is and how fresh it is

Blazor Server means every save, filter, or recalculation is a round trip over a SignalR circuit. Users forgive latency; they do not forgive silent uncertainty about whether their click registered, or working for ten minutes against a UI that is quietly disconnected from the server.

- **Do**: every mutating control (save, post, line recalculation) goes through an explicit `pending → saved/error` sequence with a visible affordance (button spinner + disabled state, or a "Saving…" chip near the field) — see `components.md` for the exact states. A dropped SignalR circuit shows a persistent, unmissable reconnection banner (`app-shell.md`) and freezes editable controls until the circuit is confirmed back.
- **Don't**: optimistically re-render a grid total as if a line edit is already saved, then silently roll it back on a failed round trip with no message. Don't let a user keep typing into a form for minutes while the circuit is actually dead, only to lose the work.
- **Borrowed from**: Linear's near-instant optimistic UI paired with a visible sync indicator, and Google Docs' "you are offline" banner — the lesson taken is: optimism in the pixel, honesty in a corner of the screen that is always there when it matters.

## 4. One way to do each thing, everywhere

A user who has learned how filtering, bulk actions, or saving works in Sales Orders should never have to relearn it in Purchase Orders, Inventory, or a future module built by a different developer months later. Consistency is what makes speed possible after the first week; novelty per-screen is what makes every screen slow forever.

- **Do**: exactly one data grid component (`components.md` → Data grid) used everywhere a tabular list of records appears, one status badge component for every kind of status in every module, one document line editor for every line-based document (sales order, purchase order, invoice, goods receipt).
- **Don't**: let the Inventory module invent its own "stock status" pill with different colors and shapes than the Sales module's "order status" badge, or let one screen's grid support column reordering while another's doesn't.
- **Borrowed from**: Microsoft Fluent/Dynamics 365 and Odoo — both enforce one grid, one form pattern, one kanban pattern across radically different modules (finance, inventory, HR), which is exactly why long-time users of either can move to an unfamiliar module and already know how to operate it.

## 5. Never make the user guess which company they are in

Database-per-tenant with multi-company-per-tenant is a structural invitation to post something to the wrong book. The interface's job is to make the active company impossible to miss and effortful to get wrong, not just theoretically visible somewhere in a corner.

- **Do**: a persistent, high-contrast tenant/company indicator in the app shell header at all times (`app-shell.md` → tenant/company switcher), restated again on every document's title/header ("Sales Order SO-2041 — Nordwind Trading AB"), and a full-screen confirmation interstitial when switching company while unsaved changes exist elsewhere.
- **Don't**: bury the active company in a small dropdown that looks like a generic settings menu, with no restatement on the document itself — that is exactly how a bookkeeper posts Company B's invoice into Company A's ledger.
- **Borrowed from**: multi-account patterns in Google Workspace (persistent, colored account avatar/badge that never disappears) and multi-workspace patterns in Slack/Notion (workspace name always visible in the shell chrome) — adapted here to also restate the company on the record itself, because in Slack posting to the wrong workspace is embarrassing; in an ERP it can misstate a company's legal financials.

## 6. Formats are data, not code

The core is country-agnostic; a Country Package can change how a date, number, currency, address or identifier is displayed or validated, and can add fields to an existing screen. A component that hard-codes `MM/DD/YYYY` or a two-line address format is a bug, even if it looks fine for one pilot customer.

- **Do**: every input and displayed value that is a date, number, currency, or address goes through the shared locale-formatting layer and the component's spec says "renders per active locale/Country Package," never a literal example format presented as the only one. Forms reserve a documented insertion point (see `components.md` → Forms) where a Country Package can add a field (e.g., a VAT number field) without the base layout breaking.
- **Don't**: a screen spec or a component that says "amounts show as `$1,234.56`" as if that were universal, or a fixed-height address block that assumes exactly street/city/postal/country and cannot grow a region/province line.
- **Borrowed from**: how well-internationalized products like Shopify Admin and Xero treat currency/date formatting as a resolved-at-render locale setting, never a template literal — the closest existing analogue to our Country Package model.

## 7. Fast paths exist beside the mouse-driven path, never instead of it

Data-entry-heavy ERP work (invoice lines, journal entries, order lines) is done by specialists who will do it thousands of times; for them, reaching for the mouse on every field is the single biggest tax on their day. But this product also serves occasional users (a manager approving one PO a week) who will never learn a shortcut and must never be blocked by one.

- **Do**: the document line editor and data grid are fully keyboard-operable (Tab/Shift+Tab across cells, Enter to commit and drop to a new line, arrow-key cell navigation, a command palette for cross-module navigation) documented per component, while every action reachable by keyboard is equally reachable by a visible, labeled control for the mouse-only user.
- **Don't**: a "power user mode" that hides buttons and requires memorized shortcuts to operate at all, or a keyboard flow that traps focus with no visible escape.
- **Borrowed from**: the spreadsheet-grade keyboard model in Excel/Google Sheets and the command palette popularized by Linear/Superhuman — both proven at making expert throughput dramatically faster without removing the discoverable, mouse-driven path underneath.
