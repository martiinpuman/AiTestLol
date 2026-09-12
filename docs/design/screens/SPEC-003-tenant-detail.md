# Screen spec — Tenant detail (operator console)

Status: ready · Author: ui-designer · Date: 2026-09-12
Backlog: `DESIGN-002` · Unblocks: `B-07`
Product spec: none — see `SPEC-003-tenant-list.md`'s front matter; the same gap applies here and is not repeated.
Architecture: `../../decisions/ADR-0007-multi-tenancy-database-per-tenant.md` §7.5 (schema-version skew table), §8 (provisioning saga steps, reaper, compensation guard — quoted below), §9.2 (catalog columns), §11.4 (offboarding state machine); `../../decisions/ADR-0008-country-package-contract.md` §4 (`InstalledPackageState`); `src/platform/Aurora.Platform.Tenancy.Contracts/TenantState.cs`, `InstalledPackageState.cs`, `src/platform/Aurora.Platform.Tenancy/Catalog/Tenant.cs`
Prototype: `../prototypes/tenant-detail.html`
Components referenced: App shell → Operator console (`app-shell.md`), Status badges (`components.md` §12), Dialogs incl. Danger zone panel (§14), Toasts (§15), Master–detail conventions reused for layout only (§10, not its two-pane list — this is a single-record page reached from `SPEC-003-tenant-list.md`)

## Scope

**In:** one tenant's identity, lifecycle state, database/cluster, schema version, installed Country Packages, and — the substance of this spec — exactly which actions are available in each of the eight `TenantState` values, and why.

**Out:** the offboarding stepper's own screens (`SPEC-003-tenant-offboarding.md` — this spec only covers the single "Manage offboarding →" link into it); installing, upgrading or deactivating a Country Package (no UI for a second package into a live tenant exists in this milestone, per `SPEC-001`'s own non-goals — packages render here read-only); a migration-run console (`SchemaBlocked`'s diagnostic panel links to one, which does not exist yet — flagged in "To route").

## Purpose

Answer, for one tenant, "what state is it in, what does that mean, and what — if anything — can I actually do about it right now." The state-to-action mapping is the whole point: a `Provisioning` tenant and a `SchemaBlocked` one must never show the same button, and a tenant that is fine must not show a destructive one at all.

## User

The same Aurora operator as `SPEC-003-tenant-list.md`, having clicked one row.

## Task flow

1. Operator arrives from the list (or a direct link) at `/operator/tenants/{tenantId}`.
2. The page loads the tenant's full record: identity, state, database/cluster, schema, packages, and a short activity/audit panel.
3. The action set rendered is determined **entirely by `state`** — see the mapping below. Nothing on this page ever shows an action that the tenant's current state doesn't support, even in a disabled form (a disabled-with-reason button is for a *permission* gap, per `components.md` §1's correction; an action that is simply not valid in this state does not exist on the page at all, because "why is Suspend showing on an already-`Suspended` tenant, disabled" is a worse experience than it not being there).

## Layout

```
┌───────────────────────────────────────────────────────────────────────────┐
│ A  Aurora ERP · Operator Console                          J. Alvarez  ⏻   │
├───────────────────────────────────────────────────────────────────────────┤
│  ← Tenants                                                                │
│  Solheim Trading                              ● Schema blocked           │
│  solheim-trading                                                         │
│  ───────────────────────────────────────────────────────────────────────  │
│  Identity                     Database                    Schema         │
│  Key        solheim-trading   Cluster    eu-north-1        Version  9    │
│  Plan       Standard          Database   aurora_t_solheim  Status  Blocked│
│  Region     eu-north          …                             (below v11)  │
│  Created    2026-06-02                                                   │
│                                                                           │
│  Blocked reason                                                          │
│  Migration run #4821 quarantined this tenant at 2026-09-10 08:14 UTC —   │
│  schema fell to v9, below the minimum supported v11. [View migration run]│
│                                                                           │
│  Installed Country Packages                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐│
│  │ se-fiscal        v3.2   ● Active      Installed 2026-06-02          ││
│  └─────────────────────────────────────────────────────────────────────┘│
│                                                                           │
│  Activity                                                                │
│  2026-09-10 08:14  System        Suspended by migration run — SchemaBlocked│
│  2026-06-02 09:02  J. Alvarez    Provisioned                             │
│                                                                           │
│  (No action panel is rendered for this state — diagnostics only.)       │
└───────────────────────────────────────────────────────────────────────────┘
```

## Fields

| Section | Field | Source | Format |
|---|---|---|---|
| Identity | Key | `catalog.tenant.key` | Monospace, never translated |
| | Plan | `plan` | Plain text |
| | Region | `residency_region` | Monospace |
| | Created | `created_at` | Locale date |
| Database | Cluster | `cluster_id` → `database_cluster.region`/`host` (host **never** rendered — connection endpoints are not operator-console content, per `CLAUDE.md`'s "no secrets in the repo" spirit extended to "no infrastructure endpoints in a UI screenshot") | Region + cluster id only |
| | Database name | `database_name` | Monospace, copyable (a copy-to-clipboard icon-button, `components.md` §1 icon button) |
| Schema | Version | `core_schema_version` | Right-aligned number; `—` while `Provisioning` |
| | Status | Derived from ADR-0007 §7.5's skew table | One of: `tenants.schema.current`, `tenants.schema.behind` ("{n} releases behind — scheduled for the next wave"), `tenants.schema.blocked` ("below minimum supported v{min}") — text only, no second badge language (see `SPEC-003-tenant-list.md`'s reasoning for why `SchemaBlocked`'s own state badge already carries the alarm; this field explains it, it doesn't repeat it in a different color) |
| Packages | Package id, version, state, installed at/by | `catalog.installed_package` | `InstalledPackageState` → badge, mapped below; read-only, no row action |
| Activity | Timestamp, actor, action | `catalog.operator_audit_event`, filtered to this tenant | Locale date-time; newest first; paginated the same way a grid would be if it grows long (not expected to, for one tenant, but not hard-capped either) |

**`InstalledPackageState` → badge** (grepped from `src/platform/Aurora.Platform.Tenancy.Contracts/InstalledPackageState.cs`, all five values):

| State | Badge |
|---|---|
| `Installing` | `info` |
| `Active` | `success` |
| `Deactivated` | `neutral` |
| `UpgradePending` | `info` |
| `Failed` | `danger` |

## The state-to-action mapping (the substance of this screen)

Every action below is a real button; every "—" is the deliberate absence of one, not a disabled one, per the task-flow rule above.

| `TenantState` | Primary action | Secondary | What's shown instead of an action | Why |
|---|---|---|---|---|
| `Provisioning` | — | "Refresh" (re-poll; no state transition) | Step progress 1–9 (ADR-0007 §8's own step table), current step, attempts, started time | The saga is not operator-drivable mid-run — only its own reaper resumes a stalled step. There is nothing correct for a human to click. |
| `ProvisioningFailed` | **"Destroy database…"** (Danger zone panel — see below) | "View last error" (already shown inline; not really a second action, listed for completeness) | Full step history, the failing step, `last_error` verbatim | ADR-0007 §8: "the only state from which an operator may destroy the database." No "retry in place" exists here — the saga's own reaper already exhausted 5 attempts before reaching this state, so the only forward path this spec offers is compensate (destroy) and let the caller submit a fresh provisioning request; see "Judgment calls" below. |
| `Active` | **"Begin offboarding…"** (Danger zone panel; navigates to `SPEC-003-tenant-offboarding.md`, which performs the actual `Suspend` transition as its own first step) | "View audit log" (scrolls to the Activity panel) | Installed packages, read-only | Nothing else changes this tenant's lifecycle state from Active in this milestone — no install/deactivate-package action exists here (`SPEC-001` non-goal). |
| `Suspended` | **"Manage offboarding →"** | "View audit log" | A banner: "This tenant is being offboarded." | Every action available to a `Suspended` tenant (resume, or continue to export) is a step in the offboarding flow, not a second, competing set of buttons on this page — one place per Principle 4. |
| `SchemaBlocked` | — | "View migration run" (links to a migration-run console that **does not exist yet** — see "To route"), "View audit log" | Blocked reason, from/to version, `last_error` from the quarantining migration run | ADR-0007 doesn't describe an operator-triggered single-tenant schema repair; the documented recovery path is the migration runner's own next wave. Inventing a "Retry migration" button here would be a capability this spec has no architectural basis for — see "Judgment calls." |
| `Exporting` | **"Manage offboarding →"** | "View audit log" | Export progress summary (both bundles' status) | Same reasoning as `Suspended` — this state's real controls live on the offboarding page, which shows live progress and the resulting download links. |
| `PendingDeletion` | **"Manage offboarding →"** | "View audit log" | `deletion_due_at` countdown, prominently, in the header area (not buried in a field list) | Same reasoning; the two consequential actions here (cancel vs. permanently delete) get the full "deliberately uncomfortable" treatment `SPEC-003-tenant-offboarding.md` exists to design, not a compressed version on this page. |
| `Deleted` | — | "View deletion certificate" (scrolls to Activity, which carries the certificate entry: who, when, backup expiry) | Only `id`, `key`, `created_at`, `deleted_at` — no cluster, no database name, no packages, because none of those fields exist on a tombstone row (ADR-0007 §11.4: "catalog row tombstoned — id, key and dates only") | Terminal. Rendering a "Database" or "Packages" section with empty/`—` values here would imply those concepts still apply; they don't, so the sections themselves are omitted, not emptied. |

## The destroy confirmation (`ProvisioningFailed` only)

The single most consequential control this spec places directly on this page (the offboarding spec has its own, separately designed, version of an equally consequential control). Opens a Dialog (`components.md` §14) inside the Danger zone panel:

1. On open, the dialog performs a **live re-check** of `platform.tenant_identity` against this tenant id (a real round trip, not a cached assumption) and shows the result before anything else is interactive: `tenants.destroy.identityConfirmed` ("✓ Confirmed — this database's identity stamp matches {key}") or, if it does not match, `tenants.destroy.identityMismatch` (a `danger` banner, the whole rest of the dialog inert, only a "Close" button available) — this is ADR-0007 §8's own compensation guard, made visible rather than assumed.
2. Only if confirmed: a text field, "Type `{key}` to confirm," matched exactly (case-sensitive — a tenant key's casing is part of its identity); the destroy button stays `aria-disabled` until it matches.
3. Submitting shows the button's loading state; success closes the dialog, returns to the list (there is nothing left at this URL — the tenant row moves to `Deleted` in the fleet-wide sense used by `ProvisioningFailed`, or more precisely is fully removed since a failed provisioning never had a catalog-worthy tombstone requirement — see "Judgment calls" for this exact ambiguity), and a toast confirms what was destroyed, naming the key.
4. Failure (a 409 because the tenant's state changed underneath the operator, or a 500) keeps the dialog open with a banner, same pattern as `SPEC-002-company-create.md`'s dialog error states.

## Permission

Two further platform-level proposals, alongside `SPEC-003-tenant-list.md`'s `platform.tenant.view`:

- `platform.tenant.manage` — gates "Begin offboarding," "Manage offboarding," "Refresh." Holding `.view` without `.manage` renders every action on this page as disabled-with-reason (`aria-disabled` + reachable reason, per `components.md` §1's correction — this **is** a permission gap, unlike the state-based absences above, so it follows the disabled-not-absent rule).
- `platform.tenant.destroy` — gates "Destroy database…" specifically, **separate from** `.manage`. An operator who can pause/export/cancel-deletion a tenant is not automatically trusted to `DROP DATABASE` — the same least-privilege reasoning `CLAUDE.md`'s OWASP ASVS L2 reference calls for, applied to the one action on this screen with no undo. Lacking `.destroy` while holding `.manage` renders the Danger zone panel's button disabled-with-reason, never absent (same rule).

Both remain proposals, exactly like `organization.company.manage` in `SPEC-002` and `platform.tenant.view` in the list spec — for the implementing developer and architect to confirm or rename.

## States

| State | What the operator sees |
|---|---|
| **Loaded** | As above, varying by `TenantState`. |
| **Loading** | Skeleton for the identity/database/schema panels; the Activity panel shows its own skeleton rows independently (it may resolve later, being a separate query). |
| **Error** | Inline error with retry, scoped to whichever panel failed — the identity header (name, key, state badge) is fetched first and separately, so a failure loading, say, the Activity panel never blanks the whole page (same principle as `components.md` §10's master-detail "error loading detail, list stays usable," adapted to "one panel's error doesn't blank the others"). |
| **Missing `.view`** | Plain "you don't have permission" state, same reasoning as the list spec — no anti-enumeration collapsing needed on an internal console. |
| **Missing `.manage`/`.destroy` only** | Full page renders; the relevant action(s) are disabled-with-reason, per "Permission" above. |
| **Long values** | A 200-character display name wraps in the header rather than truncating (same rule as every other long-name case in this design system). |
| **Concurrent change** | If another operator changes this tenant's state while this page is open (e.g., a migration wave moves it from `SchemaBlocked` back to `Active` mid-review), the next action attempt returns a 409; the page shows a banner ("This tenant's state changed — reloading") and refetches rather than letting a stale action set stay clickable against a state that no longer exists. |

## Keyboard

| Action | Key |
|---|---|
| Back to the list | `Tab` to "← Tenants," `Enter` — or the browser back button, since this is a real, deep-linkable URL |
| Reach the Danger zone panel's action | `Tab` through the page in visual order; the Danger zone panel is last, matching `components.md` §14's new "Danger zone panel" convention |
| Destroy confirmation dialog | Same focus-trap/return-focus contract as every dialog in this system (`components.md` §14); the typed-confirmation field is not autofocused until the identity re-check resolves (autofocusing a field the operator can't yet use would be worse than a one-beat delay) |
| Copy database name | `Tab` to the copy icon-button, `Enter`/`Space`; a toast confirms "Copied" |

## Accessibility notes

- The page's `<h1>` is the tenant's display name; the state badge sits beside it as a second, separately-readable element (not baked into the heading's own text), so a screen reader announces "Solheim Trading, heading level 1" followed distinctly by "Schema blocked" rather than one run-on string.
- Sections with no content for the current state (Database/Packages on a `Deleted` tenant, an action panel on `Provisioning`/`SchemaBlocked`) are **omitted from the DOM**, not rendered empty and hidden — a screen reader user never lands on a heading for a section with nothing under it.
- The Danger zone panel (`components.md` §14) has its own heading ("Danger zone" / `tenants.dangerZoneHeading`) so it is independently reachable via heading navigation, not just visually last.
- The destroy dialog's identity re-check result is announced via `role="status"`/`aria-live="polite"` the moment it resolves, since it gates whether the rest of the dialog is usable at all.

**Named against WCAG 2.2 AA, per criterion, rather than asserted:**

| SC | How this screen meets it |
|---|---|
| 1.3.1 Info and Relationships | Identity/Database/Schema render as a real `info-grid` of labeled fields (`components.md`'s existing pattern), not a two-column table masquerading as prose; a section that doesn't apply to the current state is absent from the DOM, not present-but-empty. |
| 1.4.1 Use of Color | `InstalledPackageState`'s five values are each a badge with its own text label; no package status is color-only. |
| 2.4.3 Focus Order | The destroy dialog's typed-confirmation field is not autofocused until the identity check resolves — autofocusing a control the operator can't yet use would place focus somewhere temporarily meaningless, the same reasoning `components.md` §1 and §14 already apply elsewhere in this design system. |
| 2.4.6 Headings and Labels | The Danger zone panel has its own heading, reachable by heading navigation independent of visual position — see `components.md` §14's new convention. |
| 4.1.2 Name, Role, Value | Every action button's accessible name states the action ("Destroy database…", never a bare icon); `aria-disabled` (not native `disabled`) is used everywhere a permission gap disables a control that still needs a reachable reason, per `components.md` §1's correction. |
| 4.1.3 Status Messages | The destroy dialog's identity-check result, and the success toast naming the destroyed key, are both announced through the live-region mechanism `components.md` §15 already specifies — this screen does not invent a second one. |

## Resource keys

New prefix `tenants.detail.*`; reuses `tenants.state.*` from the list spec.

| Key | en | sv-SE | Where |
|---|---|---|---|
| `tenants.detail.backToList` | ← Tenants | ← Klienter | Back link |
| `tenants.detail.identityHeading` | Identity | Identitet | Section heading |
| `tenants.detail.databaseHeading` | Database | Databas | Section heading |
| `tenants.detail.schemaHeading` | Schema | Schema | Section heading |
| `tenants.detail.packagesHeading` | Installed Country Packages | Installerade landspaket | Section heading |
| `tenants.detail.activityHeading` | Activity | Aktivitet | Section heading |
| `tenants.detail.colKey` | Key | Nyckel | Field label |
| `tenants.detail.colPlan` | Plan | Plan | Field label |
| `tenants.detail.colRegion` | Region | Region | Field label |
| `tenants.detail.colCreated` | Created | Skapad | Field label |
| `tenants.detail.colCluster` | Cluster | Kluster | Field label |
| `tenants.detail.colDatabase` | Database | Databas | Field label |
| `tenants.detail.colVersion` | Version | Version | Field label |
| `tenants.detail.colStatus` | Status | Status | Field label |
| `tenants.dangerZoneHeading` | Danger zone | Riskzon | Danger zone panel heading |
| `tenants.detail.progressHeading` | Provisioning progress | Etableringsförlopp | `Provisioning`/`ProvisioningFailed` panel heading |
| `tenants.destroy.cancel` | Cancel | Avbryt | Destroy dialog's own Cancel button — distinct from `tenants.offboarding.cancelDeletion`, which cancels a *pending deletion*, not this dialog |
| `tenants.schema.current` | Current | Aktuell | Schema status |
| `tenants.schema.behind` | {count} releases behind — scheduled for the next migration wave | {count} versioner efter — schemalagd för nästa migreringsomgång | Schema status |
| `tenants.schema.blocked` | Below minimum supported v{minimum} | Under lägsta tillåtna v{minimum} | Schema status |
| `tenants.detail.refresh` | Refresh | Uppdatera | Provisioning action |
| `tenants.detail.destroyAction` | Destroy database… | Radera databas… | ProvisioningFailed action |
| `tenants.detail.beginOffboarding` | Begin offboarding… | Påbörja avveckling… | Active action |
| `tenants.detail.manageOffboarding` | Manage offboarding → | Hantera avveckling → | Suspended/Exporting/PendingDeletion action |
| `tenants.detail.viewAuditLog` | View audit log | Visa granskningslogg | Secondary action |
| `tenants.detail.viewMigrationRun` | View migration run | Visa migreringskörning | SchemaBlocked secondary action |
| `tenants.detail.blockedReasonHeading` | Blocked reason | Blockeringsorsak | SchemaBlocked panel |
| `tenants.detail.offboardingBanner` | This tenant is being offboarded. | Den här klienten avvecklas. | Suspended/Exporting/PendingDeletion banner |
| `tenants.detail.deletionCountdown` | Deletes in {days} · {date} | Raderas om {days} · {date} | PendingDeletion header |
| `tenants.destroy.dialogTitle` | Destroy {key}'s database | Radera {key}s databas | Destroy dialog |
| `tenants.destroy.identityConfirmed` | Confirmed — this database's identity stamp matches {key} | Bekräftat — databasens identitetsstämpel matchar {key} | Destroy dialog |
| `tenants.destroy.identityMismatch` | This database's identity stamp does not match {key}. Refusing to destroy it. | Databasens identitetsstämpel matchar inte {key}. Vägrar radera den. | Destroy dialog |
| `tenants.destroy.typeToConfirm` | Type {key} to confirm | Skriv {key} för att bekräfta | Destroy dialog field label |
| `tenants.destroy.confirm` | Destroy database | Radera databas | Destroy dialog submit |
| `tenants.destroy.successToast` | {key}'s database destroyed. | {key}s databas raderad. | Toast |
| `tenants.detail.concurrentChangeBanner` | This tenant's state changed. Reloading… | Klientens status har ändrats. Läser in på nytt… | Concurrent-change state |
| `tenants.detail.copyDatabaseName` | Copy database name | Kopiera databasnamn | Icon button |
| `tenants.detail.copied` | Copied. | Kopierat. | Toast |
| `tenants.detail.unavailableHeading` | You don't have permission to view this tenant | Du har inte behörighet att visa den här klienten | Missing `.view` |
| `tenants.detail.disabledReasonManage` | Requires the {permission} permission | Kräver behörigheten {permission} | Disabled-action reason |

## Acceptance criteria

**19 machine-checkable, 4 human.**

**State-to-action mapping**

1. **[M]** For each of the eight `TenantState` values, the rendered action set matches the mapping table exactly — no state renders an action outside its row, and no state renders a *disabled* action that the mapping marks as absent (absent means not in the DOM). *This is the one criterion worth stating eight times over in the test suite, one per state, because it is the entire point of the screen.*
2. **[M]** `Deleted` renders no Database, Schema or Packages section at all (not present in the DOM), and shows only `id`, `key`, `created_at`, `deleted_at`.
3. **[M]** `SchemaBlocked` renders no destroy or repair action anywhere on the page.
4. **[M]** `ProvisioningFailed` is the only state under which the "Destroy database…" control exists in the DOM.

**Destroy confirmation**

5. **[M]** Opening the destroy dialog performs a re-check request before rendering the typed-confirmation field; the field is absent (not merely disabled) until the check resolves.
6. **[M]** A failed identity check renders `identityMismatch`, disables every control except "Close," and never reveals a typed-confirmation field.
7. **[M]** The destroy button carries `aria-disabled="true"` until the typed value case-sensitively equals the tenant key.
8. **[M]** On success, a toast naming the key is announced via the polite live region and the operator is returned to the tenant list.
9. **[M]** A 409 response (tenant state changed concurrently) keeps the dialog open, shows a banner, and does not proceed.

**Permission**

10. **[M]** Lacking `platform.tenant.view` renders the plain missing-permission state with no identity, database, schema, package or activity content.
11. **[M]** Holding `.view` but not `.manage` renders the full page with every state-transition action `aria-disabled` and a reachable reason naming `platform.tenant.manage`.
12. **[M]** Holding `.manage` but not `.destroy` on a `ProvisioningFailed` tenant renders every other action normally and only "Destroy database…" as `aria-disabled`, reason naming `platform.tenant.destroy`.

**Data and formatting**

13. **[M]** `InstalledPackageState.Failed` renders the `danger` badge; all five enum values map to a badge with no invented sixth color.
14. **[M]** The Activity panel renders newest-first and every timestamp goes through the shared date-time formatter.
15. **[M]** The database host is never present anywhere in the rendered DOM or accessibility tree, in any state.
16. **[M]** Copying the database name writes it to the clipboard (or the test harness's clipboard stub) verbatim, byte-for-byte, and announces a confirmation toast.

**Localisation and access**

17. **[M]** Every visible string on this screen resolves through the resource layer under a pseudo-locale marker test, except the tenant key, region code and database name.
18. **[M]** axe-core reports zero violations on the `Active`, `SchemaBlocked`, `ProvisioningFailed` (dialog open) and `Deleted` renders — the four states with visibly different DOM shapes.
19. **[M]** Every action in every state is reachable and operable by keyboard alone in visual order, including the Danger zone panel's position last in the tab order.

**Human pass**

20. **[H]** Screen-reader pass: a `SchemaBlocked` tenant's blocked reason reads as a complete, actionable sentence, not a fragment; the destroy dialog's identity-check result is heard before the operator can accidentally tab past it.
21. **[H]** An operator unfamiliar with this spec, shown only the `ProvisioningFailed` and `SchemaBlocked` screens side by side, correctly states which one they can currently act on and which they can't, without being told.
22. **[H]** Dark and light themes both read correctly, including the Danger zone panel's border treatment.
23. **[H]** With every en string doubled, the mapping table's longest row (`SchemaBlocked`) does not overflow its panel on a 1280px window.

## Judgment calls (recorded, not hidden)

- **No "retry provisioning" action on `ProvisioningFailed`.** ADR-0007 §8 describes the reaper exhausting 5 attempts before this state is reached and describes only compensation (`DROP DATABASE`) as the operator's lever from here — nothing describes a manual "try the saga again in place." This spec does not invent one. If a future task wants that capability, it needs its own ADR change, because the safe-adoption rule in ADR-0007 §8 step 2 is written around a *fresh* reservation, not an in-place resume of a failed one.
- **What happens to the catalog row after a `ProvisioningFailed` destroy is unspecified by ADR-0007** — §11.4's tombstone rule is written for the *offboarding* `Deleted` state (a tenant that was once `Active`), and it's not clear a `ProvisioningFailed` tenant that never activated should leave a tombstone at all versus being removed outright. This spec's copy ("returned to the tenant list… the tenant row moves to `Deleted`, or is fully removed") is deliberately hedged rather than picking one silently. **Routed to the architect below.**
- **`SchemaBlocked` has no operator-triggered repair action.** This spec treats it as diagnostic-only and links to a migration-run console that doesn't exist yet, rather than inventing a "Retry migration for this tenant" button the architecture doesn't describe. If that capability is wanted, it belongs in whatever spec designs the migration-run console, informed by whether ADR-0007's runner design actually supports a targeted single-tenant retry outside its normal wave mechanism.

## To route (outside `docs/design/`)

- **architect:** does destroying a `ProvisioningFailed` tenant leave a tombstone row (`Deleted`) or delete the catalog row outright? ADR-0007 §11.4 only specifies the tombstone for the offboarding path. This spec's copy is written to be correct either way, but the underlying behavior needs a real answer before B-07's implementer builds the command.
- **architect:** a migration-run console is referenced by this spec's `SchemaBlocked` panel and does not exist as a design or a backlog row yet — worth its own task once the migration runner (`B-08`) has enough shape to design against.
- **project-manager / architect:** `platform.tenant.manage` and `platform.tenant.destroy` are proposed here as two separate permissions specifically so an operator can be trusted with pause/export/cancel without being trusted with `DROP DATABASE`; confirm or rename both, alongside `platform.tenant.view` from the list spec.

## What we borrowed and why

State-gated action visibility (an action simply isn't in the DOM when the record's state makes it meaningless, rather than a universally-present button set with situational disabling) mirrors how AWS's and Stripe's own consoles render resource-state-specific action bars — an EC2 instance that is `stopped` doesn't show a greyed-out "Stop" button, it shows "Start" instead, because a button for an action that's currently meaningless is worse than its absence. Separating "can operate" from "can permanently destroy" into two different permissions is standard least-privilege practice in exactly this kind of admin console (the same split GCP and AWS IAM apply to compute "stop/start" versus "delete" on the same resource type).
