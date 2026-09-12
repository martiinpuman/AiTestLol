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
3. The action set rendered is determined **entirely by `state`** — see the mapping below. **The disabled-vs-absent rule, stated precisely and without exception for cross-state cases** (the same wording appears in `SPEC-003-tenant-offboarding.md`, which needs the third clause below; this spec only needs the first two):
   - An action that belongs to a *different* `TenantState` than the one currently on screen is never rendered at all, disabled or otherwise — absent from the DOM, always, no exception. "Why is Suspend showing on an already-`Suspended` tenant, disabled" is a worse experience than it not being there, and this is the rule the mapping table below encodes.
   - An action that belongs to the *current* state, but that the signed-in operator lacks permission to invoke, renders disabled-with-reason (`aria-disabled` + reachable reason, per `components.md` §1's correction) — never absent, because a permission gap is discoverable information about *this* operator, not a fact about the record.
   - *(Third case, not needed on this page but stated here once so both specs can point at the same three-way rule rather than each asserting a two-way version that the other one breaks:)* an action that belongs to the current state and that the operator *can* invoke, but whose immediate availability additionally depends on a same-state condition that resolves on its own with no further click (a background job finishing, a calendar date arriving), also renders disabled-with-reason — because hiding it would hide exactly the "what's coming and when" the operator needs, and nothing about *changing state* is required for it to become clickable. This page has no such control; `SPEC-003-tenant-offboarding.md`'s "Continue to pending deletion" and "Permanently delete…" are the two real examples, and that spec states why neither is a counterexample to the first bullet above.

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

**What counts as an "action" in this table, decidably:** a real, rendered `<button>` or `<a>` that either performs a state transition, triggers a destructive/administrative operation, or navigates to another operator screen. Nothing else qualifies — not inline diagnostic text, not a same-page scroll target, not a panel that is merely visible. Under that definition, **every state below has at most one action**, so this table has a single "Action" column, not a Primary/Secondary split that the previous draft used to smuggle non-actions in under "Secondary": the previous draft listed "View audit log" as a secondary action for six of the eight states and "View last error" for one, and neither ever existed as a rendered control in the prototype — a table that names an action nothing renders is exactly the false-positive shape `CLAUDE.md`'s self-check warns about, so both are cut here, not just relabelled.

**The Activity panel (audit trail) is unconditionally rendered in every one of the eight states** and is therefore not part of this mapping at all — it needs no "view audit log" action because nothing ever hides it. This is stated once, here, rather than repeated in every row.

Every action below is a real button; every "—" is the deliberate absence of one, per the disabled-vs-absent rule above (first bullet: a different state's action is never rendered, not even disabled).

| `TenantState` | Action | What's shown instead of a second action | Why |
|---|---|---|---|
| `Provisioning` | "Refresh" (re-polls; no state transition) | Step progress 1–9 (ADR-0007 §8's own step table), current step, attempts, started time | The saga is not operator-drivable mid-run — only its own reaper resumes a stalled step. There is nothing else correct for a human to click. |
| `ProvisioningFailed` | **"Destroy database…"** (Danger zone panel — see below) | Full step history, the failing step, `last_error` verbatim (inline text, not a control) | ADR-0007 §8: "the only state from which an operator may destroy the database." No "retry in place" exists here — the saga's own reaper already exhausted 5 attempts before reaching this state, so the only forward path this spec offers is compensate (destroy) and let the caller submit a fresh provisioning request; see "Judgment calls" below. |
| `Active` | **"Begin offboarding…"** (Danger zone panel; navigates to `SPEC-003-tenant-offboarding.md`, which performs the actual `Suspend` transition as its own first step) | Installed packages, read-only | Nothing else changes this tenant's lifecycle state from Active in this milestone — no install/deactivate-package action exists here (`SPEC-001` non-goal). |
| `Suspended` | **"Manage offboarding →"** | A banner: "This tenant is being offboarded." | Every action available to a `Suspended` tenant (resume, or continue to export) is a step in the offboarding flow, not a second, competing button on this page — one place per Principle 4. |
| `SchemaBlocked` | "View migration run" (navigates to a migration-run console that **does not exist yet** — see "To route"; in this prototype the button is real and rendered but its destination is a toast saying so, never a silently dead click) | Blocked reason, from/to version, `last_error` from the quarantining migration run (inline text) | ADR-0007 doesn't describe an operator-triggered single-tenant schema repair; the documented recovery path is the migration runner's own next wave. Inventing a "Retry migration" button here would be a capability this spec has no architectural basis for — see "Judgment calls." |
| `Exporting` | **"Manage offboarding →"** | Export progress summary (both bundles' status, inline text) | Same reasoning as `Suspended` — this state's real controls live on the offboarding page, which shows live progress and the resulting download links. |
| `PendingDeletion` | **"Manage offboarding →"** | `deletion_due_at` countdown, prominently, in the header area (not buried in a field list) | Same reasoning; the two consequential actions here (cancel vs. permanently delete) get the full "deliberately uncomfortable" treatment `SPEC-003-tenant-offboarding.md` exists to design, not a compressed version on this page. |
| `Deleted` | — (terminal; no action in any state) | The deletion certificate, inline in the Activity panel: who, when, backup expiry | Rendering a "Database" or "Packages" section with empty/`—` values here would imply those concepts still apply; they don't, so the sections themselves are omitted, not emptied — only `id`, `key`, `created_at`, `deleted_at` exist on a tombstone row (ADR-0007 §11.4: "catalog row tombstoned — id, key and dates only"). |

## The destroy confirmation (`ProvisioningFailed` only)

The single most consequential control this spec places directly on this page (the offboarding spec has its own, separately designed, version of an equally consequential control, `SPEC-003-tenant-offboarding.md`'s "The permanent-delete confirmation" — read that section's "Detection, not prevention" note too, because the same structural limit applies here). Opens a Dialog (`components.md` §14) inside the Danger zone panel:

1. **The identity check runs at act time, not merely at render time.** Opening the dialog performs a first re-check of `platform.tenant_identity` against this tenant id and shows the result before anything else is interactive — `tenants.destroy.identityConfirmed` ("✓ Confirmed — this database's identity stamp matches {key}") or, if it does not match, `tenants.destroy.identityMismatch` (a `danger` banner, the whole rest of the dialog inert, only a "Close" button available). But this first check only gates whether the typed-confirmation field appears — it is **not** the check that authorizes the destroy. **Clicking "Destroy database" re-runs the identity check a second time, immediately before the drop command is sent**, exactly as `SPEC-003-tenant-offboarding.md`'s permanent-delete flow does, so a tenant that changed state or was reassigned in the time the operator spent reading the dialog and typing the key cannot ride the first, stale check through to an actual `DROP DATABASE`.
2. Only if the first check is confirmed: a text field, "Type `{key}` to confirm," matched exactly (case-sensitive — a tenant key's casing is part of its identity); the destroy button stays `aria-disabled` until it matches.
3. Submitting shows the button's loading state, during which the second, act-time identity check runs; only if it also confirms does the actual destroy command proceed. If the second check fails where the first succeeded, the dialog does **not** destroy anything — it shows `identityMismatch` again and stops, exactly like a first-check failure, and the attempt (both checks, both results) is written to `catalog.operator_audit_event` either way.
4. Success closes the dialog, returns to the list (there is nothing left at this URL — the tenant row moves to `Deleted` in the fleet-wide sense used by `ProvisioningFailed`, or more precisely is fully removed since a failed provisioning never had a catalog-worthy tombstone requirement — see "Judgment calls" for this exact ambiguity), and a toast confirms what was destroyed, naming the key.
5. Failure (a 409 because the tenant's state changed underneath the operator, or a 500) keeps the dialog open with a banner, same pattern as `SPEC-002-company-create.md`'s dialog error states.

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
| `tenants.detail.viewMigrationRun` | View migration run | Visa migreringskörning | `SchemaBlocked`'s one action |
| `tenants.detail.migrationRunNotYetAvailable` | The migration-run console isn't built yet. | Konsolen för migreringskörningar är inte byggd än. | Toast shown by "View migration run" — a real, honest destination rather than a dead click |
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
| `tenants.detail.blockedReasonBody` | Migration run #{runId} quarantined this tenant at {timestamp} — schema fell to v{fromVersion}, below the minimum supported v{minVersion}. | Migreringskörning #{runId} satte klienten i karantän {timestamp} — schema föll till v{fromVersion}, under lägsta tillåtna v{minVersion}. | `SchemaBlocked` panel body — added this round; see Design-system findings |
| `tenants.packageState.installing` | Installing | Installerar | Package badge |
| `tenants.packageState.active` | Active | Aktivt | Package badge |
| `tenants.packageState.deactivated` | Deactivated | Inaktiverat | Package badge |
| `tenants.packageState.upgradePending` | Upgrade pending | Uppgradering väntar | Package badge |
| `tenants.packageState.failed` | Failed | Misslyckades | Package badge |
| `tenants.detail.activity.provisioningRequested` | Provisioning requested | Etablering begärd | Activity entry |
| `tenants.detail.activity.provisioningFailedAfterAttempts` | Provisioning failed after {attempts} attempts | Etablering misslyckades efter {attempts} försök | Activity entry |
| `tenants.detail.activity.provisioned` | Provisioned | Etablerad | Activity entry |
| `tenants.detail.activity.suspendedOffboardingStarted` | Suspended — offboarding started | Avstängd — avveckling påbörjad | Activity entry |
| `tenants.detail.activity.suspendedByMigrationRun` | Suspended by migration run — SchemaBlocked | Avstängd av migreringskörning — schema blockerat | Activity entry |
| `tenants.detail.activity.exportStarted` | Export started | Export startad | Activity entry |
| `tenants.detail.activity.exportCompleted` | Export completed | Export slutförd | Activity entry |
| `tenants.detail.activity.permanentlyDeleted` | Permanently deleted | Permanent raderad | Activity entry |
| `tenants.detail.activity.pendingDeletionGraceElapsed` | Pending-deletion grace period elapsed | Väntetiden för radering har löpt ut | Activity entry |
| `tenants.detail.step.reserveTenant` | Reserve tenant | Reservera klient | Provisioning-progress step label |
| `tenants.detail.step.createDatabase` | Create database | Skapa databas | Provisioning-progress step label |
| `tenants.detail.step.hardenDatabase` | Harden database | Härda databas | Provisioning-progress step label |
| `tenants.detail.step.stampIdentity` | Stamp identity | Stämpla identitet | Provisioning-progress step label |
| `tenants.detail.step.migrateSchema` | Migrate schema | Migrera schema | Provisioning-progress step label |
| `tenants.detail.step.seedPlatformData` | Seed platform data | Så plattformsdata | Provisioning-progress step label |
| `tenants.detail.step.installPackages` | Install packages | Installera paket | Provisioning-progress step label |
| `tenants.detail.step.registerRouting` | Register routing | Registrera routning | Provisioning-progress step label |
| `tenants.detail.step.announce` | Announce | Meddela | Provisioning-progress step label |

**One deliberate non-key, stated rather than silently left:** `last_error` (the `ProvisioningFailed`/`SchemaBlocked` panels' verbatim exception text) is **not** localized, on purpose — it is diagnostic content (a caught .NET exception's message) for support/engineering correlation against server-side logs, not prose an operator reads for comprehension, the same category `SPEC-002`'s `{permission}` interpolation and this spec's own tenant key/database name already establish for identifiers over prose.

## Acceptance criteria

**22 machine-checkable, 4 human.**

**State-to-action mapping**

1. **[M]** For each of the eight `TenantState` values, the rendered action set is *exactly* the one named in the mapping table's "Action" column (or none, for `Deleted`) — no state renders an action named in a different row, and no state renders that action *disabled* instead of absent. Per "an action," count only a real, rendered `<button>`/`<a>` that transitions state, triggers a destructive/administrative operation, or navigates to another operator screen — inline diagnostic text (a blocked reason, a last error) never counts, and does not need its own assertion here. *This is the one criterion worth stating eight times over in the test suite, one per state, because it is the entire point of the screen.*
2. **[M]** The Activity panel is present, and identically structured, in all eight states — proving it is not, and cannot accidentally become, part of the state-gated action set the previous criterion checks.
3. **[M]** `Deleted` renders no Database, Schema or Packages section at all (not present in the DOM), and shows only `id`, `key`, `created_at`, `deleted_at`.
4. **[M]** `SchemaBlocked` renders no destroy or repair action anywhere on the page; its one action ("View migration run") does not transition any state.
5. **[M]** `ProvisioningFailed` is the only state under which the "Destroy database…" control exists in the DOM.

**Destroy confirmation**

6. **[M]** Opening the destroy dialog performs a first re-check request before rendering the typed-confirmation field; the field is absent (not merely disabled) until that first check resolves.
7. **[M]** A failed first identity check renders `identityMismatch`, disables every control except "Close," and never reveals a typed-confirmation field.
8. **[M]** The destroy button carries `aria-disabled="true"` until the typed value case-sensitively equals the tenant key.
9. **[M]** Clicking "Destroy database" triggers a **second** identity-check request, distinct from the first, before the destroy command is sent — the guard runs at act time, not only at render time. *Fails if only one identity-check request is ever observed across opening the dialog and submitting it.*
10. **[M]** If the second check fails while the first succeeded (simulated by changing the tenant's identity stamp between dialog-open and submit), no destroy command is sent, the dialog shows `identityMismatch`, and the attempted second check is recorded as its own entry distinct from the first.
11. **[M]** On success, a toast naming the key is announced via the polite live region and the operator is returned to the tenant list.
12. **[M]** A 409 response (tenant state changed concurrently) keeps the dialog open, shows a banner, and does not proceed.

**Permission**

13. **[M]** Lacking `platform.tenant.view` renders the plain missing-permission state with no identity, database, schema, package or activity content.
14. **[M]** Holding `.view` but not `.manage` renders the full page with every state-transition action `aria-disabled` and a reachable reason naming `platform.tenant.manage`.
15. **[M]** Holding `.manage` but not `.destroy` on a `ProvisioningFailed` tenant renders every other action normally and only "Destroy database…" as `aria-disabled`, reason naming `platform.tenant.destroy`.

**Data and formatting**

16. **[M]** `InstalledPackageState.Failed` renders the `danger` badge; all five enum values map to a badge with no invented sixth color.
17. **[M]** The Activity panel renders newest-first and every timestamp goes through the shared date-time formatter.
18. **[M]** The database host is never present anywhere in the rendered DOM or accessibility tree, in any state.
19. **[M]** Copying the database name writes it to the clipboard (or the test harness's clipboard stub) verbatim, byte-for-byte, and announces a confirmation toast.

**Localisation and access**

20. **[M]** Every user-facing string on this screen — visible text and assistive-only text (`aria-label`, `aria-describedby` targets, live-region announcements) alike — resolves through the resource layer under a pseudo-locale marker test, except the tenant key, region code and database name. *The population is deliberately "every user-facing string, not just every visible one" — see `SPEC-003-tenant-list.md`'s equivalent criterion for why a visible-only population is a gap, not a choice.*
21. **[M]** axe-core reports zero violations on the `Active`, `SchemaBlocked`, `ProvisioningFailed` (dialog open) and `Deleted` renders — the four states with visibly different DOM shapes.
22. **[M]** Every action in every state is reachable and operable by keyboard alone in visual order, including the Danger zone panel's position last in the tab order.

**Human pass**

23. **[H]** Screen-reader pass: a `SchemaBlocked` tenant's blocked reason reads as a complete, actionable sentence, not a fragment; the destroy dialog's identity-check result is heard before the operator can accidentally tab past it.
24. **[H]** An operator unfamiliar with this spec, shown only the `ProvisioningFailed` and `SchemaBlocked` screens side by side, correctly states which one they can currently act on and which they can't, without being told.
25. **[H]** Dark and light themes both read correctly, including the Danger zone panel's border treatment.
26. **[H]** With every en string doubled, the mapping table's longest row (`SchemaBlocked`) does not overflow its panel on a 1280px window.

## Design-system findings

**Hard-coded strings found in the prototype, past what a first review flagged.** A review of the prior round found "fourteen hard-coded English user-facing strings with no keys across the two prototypes… including all five `InstalledPackageState` labels," anchored at `tenant-offboarding.html`. A full re-scan of both prototypes for this rework — not stopping once 14 were found — turned up **24 in this screen alone**, not 14 across both: the five `InstalledPackageState` badge labels (the package badge's `textContent` was set to the raw C# enum value, e.g. `"Deactivated"`, bypassing the resource layer entirely — `ADR-0022` rule 1 states enum display names are user-facing text, not exempt), nine distinct Activity-panel action strings (`"Provisioning requested"`, `"Provisioning failed after 5 attempts"`, etc. — visible audit-trail content, not fixture-only data), and nine provisioning-saga step labels (`"ReserveTenant"`, `"CreateDatabase"`, … rendered directly as the progress list's visible content), plus one templated sentence (`blockedReasonBody`). All 24 now resolve through the resource layer (rows above); the one deliberate exception (`last_error`) is stated, not silently applied. `SPEC-003-tenant-offboarding.md` accounts for the remaining three found in that file (`resumeBody`, `startExportBody`, `bothBundlesReady`) separately, since they're a different prototype file with their own resource-key table.

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
