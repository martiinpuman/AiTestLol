/*
 * i18n.js — shared string-keying and locale-formatting demo for the three
 * DESIGN-01 prototypes (sign-in, first-run landing, companies list).
 *
 * What this stands in for, and what it does NOT claim to be:
 *  - Real Aurora ERP resolves every user-facing string through
 *    IStringLocalizer<T> reading compiled resource files (.resx/.json) per
 *    locale, chosen once per circuit from the signed-in user's preference
 *    or the tenant's default — never re-resolved by a client-side toggle
 *    mid-session. This file's STRINGS object is a stand-in for that
 *    resource file, kept in one place so the *pattern* (lookup by key, one
 *    base-English fallback, no literal string in markup) is visible and
 *    reviewable without a build step.
 *  - Real Aurora ERP resolves dates/numbers via the active locale/Country
 *    Package at render time (docs/design/tokens.md Principle 6), server
 *    side, using .NET's culture-aware formatting. Intl.DateTimeFormat /
 *    Intl.NumberFormat below are the closest browser-side equivalent for a
 *    static prototype and demonstrate the same discipline: the same call
 *    site produces different, correct output for different locales,
 *    because the format is data (a locale tag), never a hard-coded pattern.
 *  - Which locales a given screen offers is a real product rule
 *    (app-shell.md -> User menu: base English plus whatever the tenant's
 *    installed Country Packages contribute). Where a specific prototype's
 *    fictional tenant would only legitimately offer English, the locale
 *    control is clearly marked "(review only)" in that screen — see each
 *    prototype's own inline comments for which case applies.
 */

var AURORA_LOCALES = ["en", "sv-SE"];

var STRINGS = {
  en: {
    "signin.pageTitle": "Sign in — {tenant} · Aurora ERP",
    "signin.heading": "Sign in to {tenant}",
    "signin.subhead": "Enter your Aurora ERP credentials.",
    "signin.emailLabel": "Email",
    "signin.emailPlaceholder": "you@example.com",
    "signin.passwordLabel": "Password",
    "signin.forgotPassword": "Forgot password?",
    "signin.submit": "Sign in",
    "signin.submitting": "Signing in…",
    "signin.errorInvalid": "The email or password is incorrect.",
    "signin.errorLockout": "Too many attempts. Try again in {minutes} minutes.",
    "signin.unknownHostHeading": "We couldn't find an Aurora ERP account at this address.",
    "signin.unknownHostBody": "Check the web address your administrator gave you, or contact them if you believe this is a mistake.",
    "signin.footerHelp": "Trouble signing in? Contact your administrator.",
    "signin.languageLabel": "Language",
    "signin.reauthHeading": "Sign in again to continue",
    "signin.reauthBody": "Your session ended for security reasons. Sign in again — you'll return to exactly where you left off.",
    "signin.reauthEmailLabel": "Signed in as",
    "signin.reauthPasswordLabel": "Password",
    "signin.reauthContinue": "Continue",
    "signin.reauthUnsavedNote": "You have unsaved changes on {screen}. They may not be recoverable after you sign in again.",
    "signin.demoPanelTitle": "Prototype controls",
    "signin.demoDefault": "Default",
    "signin.demoInvalid": "Invalid credentials",
    "signin.demoLockout": "Locked out",
    "signin.demoUnknownHost": "Unknown address",
    "signin.demoReauth": "Mid-session re-auth",
    "signin.demoLoading": "Submitting…",
    "signin.frozenDocNote": "Sales Order SO-2041 — Meridian Distribution (frozen while you sign in again)",
    "signin.skipLink": "Skip to sign-in form",

    "firstRun.pageTitle": "Home — {tenant} · Aurora ERP",
    "firstRun.heading": "Welcome to {tenant}.",
    "firstRun.subhead": "Your system is set up and ready. Here's what's already in place — and where to go next.",
    "firstRun.setupHeading": "What's already set up",
    "firstRun.companyDoneLabel": "Default company created",
    "firstRun.companyDoneMeta": "{company}, created {date}",
    "firstRun.rolesDoneLabel": "Roles and permissions ready",
    "firstRun.rolesDoneMeta": "{roleCount} built-in roles, {permissionCount} permissions — you hold the Administrator role",
    "firstRun.emptyHeading": "Nothing else exists yet — and that's expected",
    "firstRun.emptyBody": "Aurora ERP never adds sample data. Everything you see was created by setup; everything else starts with you — a company, a customer, an order.",
    "firstRun.primaryCta": "View companies",
    "firstRun.comingSoon": "Coming soon: inviting teammates, installing Country Packages.",
    "firstRun.loadingLabel": "Loading your tenant…",
    "firstRun.errorHeading": "We couldn't load your tenant's setup status",
    "firstRun.errorBody": "Try again, or contact support if this keeps happening.",
    "firstRun.retry": "Retry",
    "firstRun.noAccessHeading": "Welcome to {tenant}.",
    "firstRun.noAccessBody": "Sign-in was successful. Ask your administrator for access to see what's set up here.",
    "firstRun.demoPanelTitle": "Prototype controls",
    "firstRun.demoPersonaAdmin": "Administrator (default)",
    "firstRun.demoPersonaLimited": "Limited role (no company-viewing permission)",
    "firstRun.demoStateLoading": "Loading",
    "firstRun.demoStateError": "Error",
    "firstRun.previewLocale": "Preview locale (review only)",

    "companies.pageTitle": "Companies — {tenant} · Aurora ERP",
    "companies.heading": "Companies",
    "companies.breadcrumb": "Master data · Organization",
    "companies.colName": "Name",
    "companies.colCreated": "Created",
    "companies.newButton": "+ New company",
    "companies.newButtonDisabledReason": "Requires the {permission} permission",
    "companies.tableCaption": "Companies in {tenant}",
    "companies.emptyHeading": "No companies yet",
    "companies.emptyBody": "This shouldn't happen on a provisioned tenant — every tenant starts with one default company. Contact support if you see this.",
    "companies.loadingLabel": "Loading companies…",
    "companies.errorHeading": "We couldn't load your companies",
    "companies.errorBody": "Try again, or contact support if this keeps happening.",
    "companies.retry": "Retry",
    "companies.unavailableHeading": "Companies isn't available here",
    "companies.unavailableBody": "If you think this should be available to you, ask a tenant administrator.",
    "companies.create.dialogTitle": "New company",
    "companies.create.nameLabel": "Company name",
    "companies.create.requiredSuffix": "(required)",
    "companies.create.nameHint": "Up to 200 characters.",
    "companies.create.counter": "{remaining} characters left",
    "companies.create.submit": "Create company",
    "companies.create.submitting": "Creating…",
    "companies.create.cancel": "Cancel",
    "companies.create.errorRequired": "Enter a company name.",
    "companies.create.errorTooLong": "Use 200 characters or fewer — this name is {count}.",
    "companies.create.errorForbidden": "You don't have permission to create companies.",
    "companies.create.errorServer": "We couldn't create the company. Try again.",
    "companies.create.errorUnknownOutcome": "The connection dropped before we could confirm. Close this and check the list before creating {name} again.",
    "companies.create.successToast": "{name} created.",
    "companies.footerCountFew": "Showing {shown} of {total} companies",
    "companies.footerCountMany": "Showing {rangeStart}–{rangeEnd} of {total} companies",
    "companies.demoPanelTitle": "Prototype controls",
    "companies.demoStateDefault": "Default (a few companies)",
    "companies.demoStateEmpty": "Empty (should never happen)",
    "companies.demoStateLoading": "Loading",
    "companies.demoStateError": "Error",
    "companies.demoStateNoAccess": "No permission (403)",
    "companies.demoStateMany": "Many rows (scale note)",
    "companies.previewLocale": "Preview locale (review only)",
    "companies.previewPersona": "Preview as",
    "companies.demoOutcomeLabel": "Create outcome",
    "companies.demoOutcomeSuccess": "Succeeds",
    "companies.demoOutcomeForbidden": "Refused (403)",
    "companies.demoOutcomeServer": "Server error (500)",
    "companies.demoOutcomeCircuitDrop": "Circuit drops mid-submit",

    "shell.skipToContent": "Skip to main content",
    "shell.reconnecting": "Reconnecting… changes made in the last few seconds may not be saved.",
    "shell.reconnected": "Reconnected.",
    "shell.connectionLost": "Connection lost. Editing is disabled until the page is reloaded.",
    "shell.reloadPage": "Reload page",

    "operator.brand": "Aurora ERP · Operator Console",
    "operator.signOut": "Sign out",
    "tenants.demoPanelTitle": "Prototype controls",

    "tenants.pageTitle": "Tenants — Operator Console · Aurora ERP",
    "tenants.heading": "Tenants",
    "tenants.tableCaption": "All tenants",
    "tenants.colTenant": "Tenant",
    "tenants.colState": "State",
    "tenants.colPlan": "Plan",
    "tenants.colRegion": "Region",
    "tenants.colSchema": "Schema",
    "tenants.colCreated": "Created",
    "tenants.colLastActivity": "Last activity",
    "tenants.searchPlaceholder": "Search name or key",
    "tenants.filterState": "State",
    "tenants.filterRegion": "Region",
    "tenants.filterPlan": "Plan",
    "tenants.includeDeleted": "Include deleted",
    "tenants.defaultView": "Needs attention first",
    "tenants.attentionLandmarkLabel": "Needs attention",
    "tenants.attentionNeedsAction": "{count} need action",
    "tenants.attentionPendingDeletion": "{count} pending deletion, soonest in {days}",
    "tenants.footerCount": "Showing {rangeStart}–{rangeEnd} of {total} tenants",
    "tenants.loadingLabel": "Loading tenants…",
    "tenants.emptyHeading": "No tenants yet",
    "tenants.emptyBody": "Tenants are created through the provisioning API, not from this screen. Once the first tenant is provisioned, it appears here.",
    "tenants.filteredEmptyHeading": "No tenants match your filters",
    "tenants.clearFilters": "Clear filters",
    "tenants.errorHeading": "We couldn't load tenants",
    "tenants.retry": "Retry",
    "tenants.unavailableHeading": "You don't have permission to view tenants",
    "tenants.unavailableBody": "This requires the {permission} permission. Contact a platform administrator.",
    "tenants.dangerZoneHeading": "Danger zone",
    "tenants.packageState.installing": "Installing",
    "tenants.packageState.active": "Active",
    "tenants.packageState.deactivated": "Deactivated",
    "tenants.packageState.upgradePending": "Upgrade pending",
    "tenants.packageState.failed": "Failed",

    "tenants.state.provisioning": "Provisioning",
    "tenants.state.provisioningDetail": "Step {step} of 9",
    "tenants.state.provisioningFailed": "Provisioning failed",
    "tenants.state.provisioningFailedDetail": "Failed after {attempts} attempts",
    "tenants.state.active": "Active",
    "tenants.state.activeDetail": "Last activity {relativeTime}",
    "tenants.state.activeDetailNever": "No activity yet",
    "tenants.state.suspended": "Suspended",
    "tenants.state.suspendedDetail": "Suspended {relativeTime} — offboarding",
    "tenants.state.schemaBlocked": "Schema blocked",
    "tenants.state.schemaBlockedDetail": "Blocked — schema v{version} is below minimum v{minimum}",
    "tenants.state.exporting": "Exporting",
    "tenants.state.exportingDetail": "Export in progress",
    "tenants.state.pendingDeletion": "Pending deletion",
    "tenants.state.pendingDeletionDetail": "Deletes in {days} · {date}",
    "tenants.state.deleted": "Deleted",
    "tenants.state.deletedDetail": "Deleted {date}",

    "tenants.detail.backToList": "← Tenants",
    "tenants.detail.identityHeading": "Identity",
    "tenants.detail.databaseHeading": "Database",
    "tenants.detail.schemaHeading": "Schema",
    "tenants.detail.packagesHeading": "Installed Country Packages",
    "tenants.detail.activityHeading": "Activity",
    "tenants.detail.colKey": "Key",
    "tenants.detail.colPlan": "Plan",
    "tenants.detail.colRegion": "Region",
    "tenants.detail.colCreated": "Created",
    "tenants.detail.colCluster": "Cluster",
    "tenants.detail.colDatabase": "Database",
    "tenants.detail.colVersion": "Version",
    "tenants.detail.colStatus": "Status",
    "tenants.detail.progressHeading": "Provisioning progress",
    "tenants.destroy.cancel": "Cancel",
    "tenants.schema.current": "Current",
    "tenants.schema.behind": "{count} releases behind — scheduled for the next migration wave",
    "tenants.schema.blocked": "Below minimum supported v{minimum}",
    "tenants.detail.refresh": "Refresh",
    "tenants.detail.destroyAction": "Destroy database…",
    "tenants.detail.beginOffboarding": "Begin offboarding…",
    "tenants.detail.manageOffboarding": "Manage offboarding →",
    "tenants.detail.viewMigrationRun": "View migration run",
    "tenants.detail.migrationRunNotYetAvailable": "The migration-run console isn't built yet.",
    "tenants.detail.activity.provisioningRequested": "Provisioning requested",
    "tenants.detail.activity.provisioningFailedAfterAttempts": "Provisioning failed after {attempts} attempts",
    "tenants.detail.activity.provisioned": "Provisioned",
    "tenants.detail.activity.suspendedOffboardingStarted": "Suspended — offboarding started",
    "tenants.detail.activity.suspendedByMigrationRun": "Suspended by migration run — SchemaBlocked",
    "tenants.detail.activity.exportStarted": "Export started",
    "tenants.detail.activity.exportCompleted": "Export completed",
    "tenants.detail.activity.permanentlyDeleted": "Permanently deleted",
    "tenants.detail.activity.pendingDeletionGraceElapsed": "Pending-deletion grace period elapsed",
    "tenants.detail.blockedReasonBody": "Migration run #{runId} quarantined this tenant at {timestamp} — schema fell to v{fromVersion}, below the minimum supported v{minVersion}.",
    "tenants.detail.step.reserveTenant": "Reserve tenant",
    "tenants.detail.step.createDatabase": "Create database",
    "tenants.detail.step.hardenDatabase": "Harden database",
    "tenants.detail.step.stampIdentity": "Stamp identity",
    "tenants.detail.step.migrateSchema": "Migrate schema",
    "tenants.detail.step.seedPlatformData": "Seed platform data",
    "tenants.detail.step.installPackages": "Install packages",
    "tenants.detail.step.registerRouting": "Register routing",
    "tenants.detail.step.announce": "Announce",
    "tenants.detail.blockedReasonHeading": "Blocked reason",
    "tenants.detail.offboardingBanner": "This tenant is being offboarded.",
    "tenants.detail.deletionCountdown": "Deletes in {days} · {date}",
    "tenants.destroy.dialogTitle": "Destroy {key}'s database",
    "tenants.destroy.identityConfirmed": "Confirmed — this database's identity stamp matches {key}",
    "tenants.destroy.identityMismatch": "This database's identity stamp does not match {key}. Refusing to destroy it.",
    "tenants.destroy.typeToConfirm": "Type {key} to confirm",
    "tenants.destroy.confirm": "Destroy database",
    "tenants.destroy.successToast": "{key}'s database destroyed.",
    "tenants.detail.concurrentChangeBanner": "This tenant's state changed. Reloading…",
    "tenants.detail.copyDatabaseName": "Copy database name",
    "tenants.detail.copied": "Copied.",
    "tenants.detail.unavailableHeading": "You don't have permission to view this tenant",
    "tenants.detail.disabledReasonManage": "Requires the {permission} permission",

    "tenants.offboarding.pageTitle": "Offboarding {tenant} · Aurora ERP",
    "tenants.offboarding.heading": "Offboarding {tenant}",
    "tenants.offboarding.intro": "Offboarding suspends this tenant (read-only, reversible), then produces an export, then a 30-day reversible pending-deletion window, then permanent deletion. Nothing here is irreversible until the final step.",
    "tenants.offboarding.stageSuspended": "Suspended",
    "tenants.offboarding.stageExported": "Exported",
    "tenants.offboarding.stagePendingDeletion": "Pending deletion",
    "tenants.offboarding.stageDeleted": "Deleted",
    "tenants.offboarding.beginAction": "Suspend {tenant} to begin",
    "tenants.offboarding.resume": "Resume tenant",
    "tenants.offboarding.resumeBody": "Restores {tenant} to Active immediately.",
    "tenants.offboarding.startExport": "Start export →",
    "tenants.offboarding.startExportBody": "This begins producing an export. The tenant stays read-only.",
    "tenants.offboarding.bothBundlesReady": "Both bundles ready",
    "tenants.offboarding.dumpBundle": "Portability dump (.pgdump)",
    "tenants.offboarding.openFormatBundle": "Open-format bundle (CSV/JSON + interchange)",
    "tenants.offboarding.expiresOn": "Expires {date}",
    "tenants.offboarding.expired": "Expired",
    "tenants.offboarding.regenerate": "Regenerate",
    "tenants.offboarding.continueAction": "Continue to pending deletion →",
    "tenants.offboarding.continueDisabledReason": "Available once both exports complete",
    "tenants.offboarding.daysRemaining": "{days} days remaining",
    "tenants.offboarding.cancelDeletion": "Cancel deletion",
    "tenants.offboarding.cancelDeletionBody": "Restores this tenant to Active immediately.",
    "tenants.offboarding.permanentDeleteAction": "Permanently delete…",
    "tenants.offboarding.notYetEligible": "Not available until {date}",
    "tenants.offboarding.verifiedExportCheckbox": "I have verified the export at {timestamp} is complete and retrievable.",
    "tenants.offboarding.exportExpiredBlock": "The export has expired. Regenerate it before deleting.",
    "tenants.offboarding.typeToConfirm": "Type {key} to confirm",
    "tenants.offboarding.confirmDelete": "Permanently delete",
    "tenants.offboarding.gatesStatus": "{satisfied} of 3 confirmed",
    "tenants.offboarding.successToast": "{tenant} permanently deleted.",
    "tenants.offboarding.certificateHeading": "Deletion certificate",
    "tenants.offboarding.certificateBody": "Deleted {date} by {actor}. Backups expire {backupExpiry}. Recorded in the platform audit trail.",
    "tenants.offboarding.disabledReasonManage": "Requires the {permission} permission",
    "tenants.offboarding.concurrentChangeBanner": "This tenant's state changed. Reloading…"
  },
  "sv-SE": {
    "signin.pageTitle": "Logga in — {tenant} · Aurora ERP",
    "signin.heading": "Logga in på {tenant}",
    "signin.subhead": "Ange dina uppgifter för Aurora ERP.",
    "signin.emailLabel": "E-post",
    "signin.emailPlaceholder": "du@exempel.se",
    "signin.passwordLabel": "Lösenord",
    "signin.forgotPassword": "Glömt lösenordet?",
    "signin.submit": "Logga in",
    "signin.submitting": "Loggar in…",
    "signin.errorInvalid": "Fel e-postadress eller lösenord.",
    "signin.errorLockout": "För många försök. Försök igen om {minutes} minuter.",
    "signin.unknownHostHeading": "Vi kunde inte hitta något Aurora ERP-konto på den här adressen.",
    "signin.unknownHostBody": "Kontrollera webbadressen du fått av din administratör, eller kontakta dem om du tror att det är fel.",
    "signin.footerHelp": "Problem att logga in? Kontakta din administratör.",
    "signin.languageLabel": "Språk",
    "signin.reauthHeading": "Logga in igen för att fortsätta",
    "signin.reauthBody": "Din session avslutades av säkerhetsskäl. Logga in igen — du kommer tillbaka precis där du var.",
    "signin.reauthEmailLabel": "Inloggad som",
    "signin.reauthPasswordLabel": "Lösenord",
    "signin.reauthContinue": "Fortsätt",
    "signin.reauthUnsavedNote": "Du har osparade ändringar i {screen}. De går kanske inte att återställa efter att du loggat in igen.",
    "signin.demoPanelTitle": "Prototypkontroller",
    "signin.demoDefault": "Standard",
    "signin.demoInvalid": "Felaktiga uppgifter",
    "signin.demoLockout": "Spärrad",
    "signin.demoUnknownHost": "Okänd adress",
    "signin.demoReauth": "Omautentisering mitt i sessionen",
    "signin.demoLoading": "Skickar…",
    "signin.frozenDocNote": "Kundorder SO-2041 — Meridian Distribution (fryst medan du loggar in igen)",
    "signin.skipLink": "Hoppa till inloggningsformuläret",

    "firstRun.pageTitle": "Hem — {tenant} · Aurora ERP",
    "firstRun.heading": "Välkommen till {tenant}.",
    "firstRun.subhead": "Ditt system är klart att användas. Här är vad som redan finns på plats — och vad du kan göra härnäst.",
    "firstRun.setupHeading": "Det här är redan klart",
    "firstRun.companyDoneLabel": "Standardföretag skapat",
    "firstRun.companyDoneMeta": "{company}, skapat {date}",
    "firstRun.rolesDoneLabel": "Roller och behörigheter klara",
    "firstRun.rolesDoneMeta": "{roleCount} inbyggda roller, {permissionCount} behörigheter — du har rollen Administratör",
    "firstRun.emptyHeading": "Inget annat finns än — och det är förväntat",
    "firstRun.emptyBody": "Aurora ERP lägger aldrig till exempeldata. Allt du ser här skapades av installationen; allt annat börjar med dig — ett företag, en kund, en order.",
    "firstRun.primaryCta": "Visa företag",
    "firstRun.comingSoon": "Kommer snart: bjuda in medarbetare, installera landspaket.",
    "firstRun.loadingLabel": "Läser in din organisation…",
    "firstRun.errorHeading": "Vi kunde inte läsa in organisationens status",
    "firstRun.errorBody": "Försök igen, eller kontakta supporten om problemet kvarstår.",
    "firstRun.retry": "Försök igen",
    "firstRun.noAccessHeading": "Välkommen till {tenant}.",
    "firstRun.noAccessBody": "Inloggningen lyckades. Be din administratör om åtkomst för att se vad som är konfigurerat här.",
    "firstRun.demoPanelTitle": "Prototypkontroller",
    "firstRun.demoPersonaAdmin": "Administratör (standard)",
    "firstRun.demoPersonaLimited": "Begränsad roll (ingen behörighet att visa företag)",
    "firstRun.demoStateLoading": "Läser in",
    "firstRun.demoStateError": "Fel",
    "firstRun.previewLocale": "Förhandsgranska språk (endast granskning)",

    "companies.pageTitle": "Företag — {tenant} · Aurora ERP",
    "companies.heading": "Företag",
    "companies.breadcrumb": "Grunddata · Organisation",
    "companies.colName": "Namn",
    "companies.colCreated": "Skapat",
    "companies.newButton": "+ Nytt företag",
    "companies.newButtonDisabledReason": "Kräver behörigheten {permission}",
    "companies.tableCaption": "Företag i {tenant}",
    "companies.emptyHeading": "Inga företag än",
    "companies.emptyBody": "Det här ska inte hända för en etablerad klient — varje klient startar med ett standardföretag. Kontakta supporten om du ser det här.",
    "companies.loadingLabel": "Läser in företag…",
    "companies.errorHeading": "Vi kunde inte läsa in dina företag",
    "companies.errorBody": "Försök igen, eller kontakta supporten om problemet kvarstår.",
    "companies.retry": "Försök igen",
    "companies.unavailableHeading": "Företag är inte tillgängligt här",
    "companies.unavailableBody": "Om du tror att det här borde vara tillgängligt för dig, kontakta en administratör.",
    "companies.create.dialogTitle": "Nytt företag",
    "companies.create.nameLabel": "Företagsnamn",
    "companies.create.requiredSuffix": "(obligatoriskt)",
    "companies.create.nameHint": "Högst 200 tecken.",
    "companies.create.counter": "{remaining} tecken kvar",
    "companies.create.submit": "Skapa företag",
    "companies.create.submitting": "Skapar…",
    "companies.create.cancel": "Avbryt",
    "companies.create.errorRequired": "Ange ett företagsnamn.",
    "companies.create.errorTooLong": "Använd högst 200 tecken — det här namnet har {count}.",
    "companies.create.errorForbidden": "Du har inte behörighet att skapa företag.",
    "companies.create.errorServer": "Vi kunde inte skapa företaget. Försök igen.",
    "companies.create.errorUnknownOutcome": "Anslutningen bröts innan vi hann bekräfta. Stäng det här och kontrollera listan innan du skapar {name} igen.",
    "companies.create.successToast": "{name} har skapats.",
    "companies.footerCountFew": "Visar {shown} av {total} företag",
    "companies.footerCountMany": "Visar {rangeStart}–{rangeEnd} av {total} företag",
    "companies.demoPanelTitle": "Prototypkontroller",
    "companies.demoStateDefault": "Standard (några företag)",
    "companies.demoStateEmpty": "Tomt (ska aldrig hända)",
    "companies.demoStateLoading": "Läser in",
    "companies.demoStateError": "Fel",
    "companies.demoStateNoAccess": "Ingen behörighet (403)",
    "companies.demoStateMany": "Många rader (skalnotering)",
    "companies.previewLocale": "Förhandsgranska språk (endast granskning)",
    "companies.previewPersona": "Förhandsgranska som",
    "companies.demoOutcomeLabel": "Utfall vid skapande",
    "companies.demoOutcomeSuccess": "Lyckas",
    "companies.demoOutcomeForbidden": "Nekas (403)",
    "companies.demoOutcomeServer": "Serverfel (500)",
    "companies.demoOutcomeCircuitDrop": "Anslutningen bröts under skickandet",

    "shell.skipToContent": "Hoppa till huvudinnehållet",
    "shell.reconnecting": "Återansluter… ändringar från de senaste sekunderna kanske inte har sparats.",
    "shell.reconnected": "Återansluten.",
    "shell.connectionLost": "Anslutningen bröts. Redigering är avstängd tills sidan laddas om.",
    "shell.reloadPage": "Ladda om sidan",

    "operator.brand": "Aurora ERP · Operatörskonsol",
    "operator.signOut": "Logga ut",
    "tenants.demoPanelTitle": "Prototyplägen",

    "tenants.pageTitle": "Klienter — Operatörskonsol · Aurora ERP",
    "tenants.heading": "Klienter",
    "tenants.tableCaption": "Alla klienter",
    "tenants.colTenant": "Klient",
    "tenants.colState": "Status",
    "tenants.colPlan": "Plan",
    "tenants.colRegion": "Region",
    "tenants.colSchema": "Schema",
    "tenants.colCreated": "Skapad",
    "tenants.colLastActivity": "Senaste aktivitet",
    "tenants.searchPlaceholder": "Sök namn eller nyckel",
    "tenants.filterState": "Status",
    "tenants.filterRegion": "Region",
    "tenants.filterPlan": "Plan",
    "tenants.includeDeleted": "Inkludera raderade",
    "tenants.defaultView": "Kräver åtgärd först",
    "tenants.attentionLandmarkLabel": "Kräver åtgärd",
    "tenants.attentionNeedsAction": "{count} kräver åtgärd",
    "tenants.attentionPendingDeletion": "{count} väntar på radering, snarast om {days}",
    "tenants.footerCount": "Visar {rangeStart}–{rangeEnd} av {total} klienter",
    "tenants.loadingLabel": "Läser in klienter…",
    "tenants.emptyHeading": "Inga klienter än",
    "tenants.emptyBody": "Klienter skapas via etableringsgränssnittet, inte från den här sidan. Så snart den första klienten är etablerad visas den här.",
    "tenants.filteredEmptyHeading": "Inga klienter matchar dina filter",
    "tenants.clearFilters": "Rensa filter",
    "tenants.errorHeading": "Vi kunde inte läsa in klienter",
    "tenants.retry": "Försök igen",
    "tenants.unavailableHeading": "Du har inte behörighet att visa klienter",
    "tenants.unavailableBody": "Det här kräver behörigheten {permission}. Kontakta en plattformsadministratör.",
    "tenants.dangerZoneHeading": "Riskzon",
    "tenants.packageState.installing": "Installerar",
    "tenants.packageState.active": "Aktivt",
    "tenants.packageState.deactivated": "Inaktiverat",
    "tenants.packageState.upgradePending": "Uppgradering väntar",
    "tenants.packageState.failed": "Misslyckades",

    "tenants.state.provisioning": "Etablerar",
    "tenants.state.provisioningDetail": "Steg {step} av 9",
    "tenants.state.provisioningFailed": "Etablering misslyckades",
    "tenants.state.provisioningFailedDetail": "Misslyckades efter {attempts} försök",
    "tenants.state.active": "Aktiv",
    "tenants.state.activeDetail": "Senaste aktivitet {relativeTime}",
    "tenants.state.activeDetailNever": "Ingen aktivitet än",
    "tenants.state.suspended": "Avstängd",
    "tenants.state.suspendedDetail": "Avstängd {relativeTime} — avveckling",
    "tenants.state.schemaBlocked": "Schema blockerat",
    "tenants.state.schemaBlockedDetail": "Blockerad — schema v{version} är under lägsta tillåtna v{minimum}",
    "tenants.state.exporting": "Exporterar",
    "tenants.state.exportingDetail": "Export pågår",
    "tenants.state.pendingDeletion": "Väntar på radering",
    "tenants.state.pendingDeletionDetail": "Raderas om {days} · {date}",
    "tenants.state.deleted": "Raderad",
    "tenants.state.deletedDetail": "Raderad {date}",

    "tenants.detail.backToList": "← Klienter",
    "tenants.detail.identityHeading": "Identitet",
    "tenants.detail.databaseHeading": "Databas",
    "tenants.detail.schemaHeading": "Schema",
    "tenants.detail.packagesHeading": "Installerade landspaket",
    "tenants.detail.activityHeading": "Aktivitet",
    "tenants.detail.colKey": "Nyckel",
    "tenants.detail.colPlan": "Plan",
    "tenants.detail.colRegion": "Region",
    "tenants.detail.colCreated": "Skapad",
    "tenants.detail.colCluster": "Kluster",
    "tenants.detail.colDatabase": "Databas",
    "tenants.detail.colVersion": "Version",
    "tenants.detail.colStatus": "Status",
    "tenants.detail.progressHeading": "Etableringsförlopp",
    "tenants.destroy.cancel": "Avbryt",
    "tenants.schema.current": "Aktuell",
    "tenants.schema.behind": "{count} versioner efter — schemalagd för nästa migreringsomgång",
    "tenants.schema.blocked": "Under lägsta tillåtna v{minimum}",
    "tenants.detail.refresh": "Uppdatera",
    "tenants.detail.destroyAction": "Radera databas…",
    "tenants.detail.beginOffboarding": "Påbörja avveckling…",
    "tenants.detail.manageOffboarding": "Hantera avveckling →",
    "tenants.detail.viewMigrationRun": "Visa migreringskörning",
    "tenants.detail.migrationRunNotYetAvailable": "Konsolen för migreringskörningar är inte byggd än.",
    "tenants.detail.activity.provisioningRequested": "Etablering begärd",
    "tenants.detail.activity.provisioningFailedAfterAttempts": "Etablering misslyckades efter {attempts} försök",
    "tenants.detail.activity.provisioned": "Etablerad",
    "tenants.detail.activity.suspendedOffboardingStarted": "Avstängd — avveckling påbörjad",
    "tenants.detail.activity.suspendedByMigrationRun": "Avstängd av migreringskörning — schema blockerat",
    "tenants.detail.activity.exportStarted": "Export startad",
    "tenants.detail.activity.exportCompleted": "Export slutförd",
    "tenants.detail.activity.permanentlyDeleted": "Permanent raderad",
    "tenants.detail.activity.pendingDeletionGraceElapsed": "Väntetiden för radering har löpt ut",
    "tenants.detail.blockedReasonBody": "Migreringskörning #{runId} satte klienten i karantän {timestamp} — schema föll till v{fromVersion}, under lägsta tillåtna v{minVersion}.",
    "tenants.detail.step.reserveTenant": "Reservera klient",
    "tenants.detail.step.createDatabase": "Skapa databas",
    "tenants.detail.step.hardenDatabase": "Härda databas",
    "tenants.detail.step.stampIdentity": "Stämpla identitet",
    "tenants.detail.step.migrateSchema": "Migrera schema",
    "tenants.detail.step.seedPlatformData": "Så plattformsdata",
    "tenants.detail.step.installPackages": "Installera paket",
    "tenants.detail.step.registerRouting": "Registrera routning",
    "tenants.detail.step.announce": "Meddela",
    "tenants.detail.blockedReasonHeading": "Blockeringsorsak",
    "tenants.detail.offboardingBanner": "Den här klienten avvecklas.",
    "tenants.detail.deletionCountdown": "Raderas om {days} · {date}",
    "tenants.destroy.dialogTitle": "Radera {key}s databas",
    "tenants.destroy.identityConfirmed": "Bekräftat — databasens identitetsstämpel matchar {key}",
    "tenants.destroy.identityMismatch": "Databasens identitetsstämpel matchar inte {key}. Vägrar radera den.",
    "tenants.destroy.typeToConfirm": "Skriv {key} för att bekräfta",
    "tenants.destroy.confirm": "Radera databas",
    "tenants.destroy.successToast": "{key}s databas raderad.",
    "tenants.detail.concurrentChangeBanner": "Klientens status har ändrats. Läser in på nytt…",
    "tenants.detail.copyDatabaseName": "Kopiera databasnamn",
    "tenants.detail.copied": "Kopierat.",
    "tenants.detail.unavailableHeading": "Du har inte behörighet att visa den här klienten",
    "tenants.detail.disabledReasonManage": "Kräver behörigheten {permission}",

    "tenants.offboarding.pageTitle": "Avveckling av {tenant} · Aurora ERP",
    "tenants.offboarding.heading": "Avveckling av {tenant}",
    "tenants.offboarding.intro": "Avveckling stänger av klienten (skrivskyddad, återställningsbar), skapar sedan en export, därefter ett 30-dagars återställningsbart väntefönster, och slutligen permanent radering. Inget här är oåterkalleligt förrän det sista steget.",
    "tenants.offboarding.stageSuspended": "Avstängd",
    "tenants.offboarding.stageExported": "Exporterad",
    "tenants.offboarding.stagePendingDeletion": "Väntar på radering",
    "tenants.offboarding.stageDeleted": "Raderad",
    "tenants.offboarding.beginAction": "Stäng av {tenant} för att börja",
    "tenants.offboarding.resume": "Återuppta klient",
    "tenants.offboarding.resumeBody": "Återställer {tenant} till Aktiv omedelbart.",
    "tenants.offboarding.startExport": "Starta export →",
    "tenants.offboarding.startExportBody": "Det här startar en export. Klienten förblir skrivskyddad.",
    "tenants.offboarding.bothBundlesReady": "Båda paketen klara",
    "tenants.offboarding.dumpBundle": "Portabilitetsdump (.pgdump)",
    "tenants.offboarding.openFormatBundle": "Öppet format-paket (CSV/JSON + utbytesformat)",
    "tenants.offboarding.expiresOn": "Går ut {date}",
    "tenants.offboarding.expired": "Har gått ut",
    "tenants.offboarding.regenerate": "Skapa på nytt",
    "tenants.offboarding.continueAction": "Fortsätt till väntande radering →",
    "tenants.offboarding.continueDisabledReason": "Tillgänglig när båda exporterna är klara",
    "tenants.offboarding.daysRemaining": "{days} dagar kvar",
    "tenants.offboarding.cancelDeletion": "Avbryt radering",
    "tenants.offboarding.cancelDeletionBody": "Återställer klienten till Aktiv omedelbart.",
    "tenants.offboarding.permanentDeleteAction": "Radera permanent…",
    "tenants.offboarding.notYetEligible": "Inte tillgänglig förrän {date}",
    "tenants.offboarding.verifiedExportCheckbox": "Jag har verifierat att exporten från {timestamp} är fullständig och hämtningsbar.",
    "tenants.offboarding.exportExpiredBlock": "Exporten har gått ut. Skapa den på nytt innan radering.",
    "tenants.offboarding.typeToConfirm": "Skriv {key} för att bekräfta",
    "tenants.offboarding.confirmDelete": "Radera permanent",
    "tenants.offboarding.gatesStatus": "{satisfied} av 3 bekräftade",
    "tenants.offboarding.successToast": "{tenant} permanent raderad.",
    "tenants.offboarding.certificateHeading": "Raderingsintyg",
    "tenants.offboarding.certificateBody": "Raderad {date} av {actor}. Säkerhetskopior går ut {backupExpiry}. Registrerat i plattformens granskningslogg.",
    "tenants.offboarding.disabledReasonManage": "Kräver behörigheten {permission}",
    "tenants.offboarding.concurrentChangeBanner": "Klientens status har ändrats. Läser in på nytt…"
  }
};

function currentLocale() {
  var stored = null;
  try { stored = localStorage.getItem("aurora-locale"); } catch (e) { /* ignore */ }
  return (stored && AURORA_LOCALES.indexOf(stored) >= 0) ? stored : "en";
}

// t(key, vars) — the one lookup function every piece of markup below goes
// through. Falls back to the base locale (en), then to the raw key itself,
// so a missing translation is visible as a literal key rather than blank.
function t(key, vars) {
  var loc = currentLocale();
  var table = STRINGS[loc] || STRINGS.en;
  var str = (table[key] !== undefined) ? table[key] : (STRINGS.en[key] !== undefined ? STRINGS.en[key] : key);
  if (vars) {
    Object.keys(vars).forEach(function (k) {
      str = str.split("{" + k + "}").join(vars[k]);
    });
  }
  return str;
}

function formatDate(isoString) {
  return new Intl.DateTimeFormat(currentLocale(), { year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(isoString));
}

function formatDateTime(isoString) {
  return new Intl.DateTimeFormat(currentLocale(), {
    year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit"
  }).format(new Date(isoString));
}

function formatCount(n) {
  return new Intl.NumberFormat(currentLocale()).format(n);
}

// formatRelativeMinutes/Days — the closest browser-side equivalent to the
// server-side, culture-aware relative formatting ADR-0022 requires (SPEC-003
// tenant list/detail: "Last activity 3 minutes ago", offboarding's day
// countdowns). Intl.RelativeTimeFormat, not a hand-built "X minutes ago"
// string template, so the wording itself is locale data, not literal English
// with a number spliced in (the same discipline formatCount already applies
// to grouping separators).
function formatRelativeMinutes(minutesAgo) {
  return new Intl.RelativeTimeFormat(currentLocale(), { numeric: "auto" }).format(-minutesAgo, "minute");
}
function formatRelativeDays(days) {
  return new Intl.RelativeTimeFormat(currentLocale(), { numeric: "auto", style: "long" }).format(days, "day");
}
// formatRelativeAuto — picks minutes/hours/days automatically, used by the
// SPEC-003 tenant list/detail "Last activity" and "Suspended" detail lines so
// a 3-minute-old event and a 5-day-old one both read naturally instead of
// "0.0035 days ago".
function formatRelativeAuto(minutesAgo) {
  var rtf = new Intl.RelativeTimeFormat(currentLocale(), { numeric: "auto" });
  if (minutesAgo < 60) return rtf.format(-minutesAgo, "minute");
  if (minutesAgo < 1440) return rtf.format(-Math.round(minutesAgo / 60), "hour");
  return rtf.format(-Math.round(minutesAgo / 1440), "day");
}

// applyI18n scans data-i18n[-*] attributes and fills them in for the
// current locale — the closest a static prototype can get to a real
// IStringLocalizer render pass. Call it again after setLocale() and after
// injecting any dynamic markup (e.g. generated grid rows).
function applyI18n(root) {
  var scope = root || document;
  scope.querySelectorAll("[data-i18n]").forEach(function (el) {
    var vars = el.getAttribute("data-i18n-vars");
    el.textContent = t(el.getAttribute("data-i18n"), vars ? JSON.parse(vars) : null);
  });
  scope.querySelectorAll("[data-i18n-html]").forEach(function (el) {
    var vars = el.getAttribute("data-i18n-vars");
    el.innerHTML = t(el.getAttribute("data-i18n-html"), vars ? JSON.parse(vars) : null);
  });
  scope.querySelectorAll("[data-i18n-placeholder]").forEach(function (el) {
    el.setAttribute("placeholder", t(el.getAttribute("data-i18n-placeholder")));
  });
  scope.querySelectorAll("[data-i18n-aria-label]").forEach(function (el) {
    el.setAttribute("aria-label", t(el.getAttribute("data-i18n-aria-label")));
  });
  scope.querySelectorAll("[data-i18n-title]").forEach(function (el) {
    el.setAttribute("title", t(el.getAttribute("data-i18n-title")));
  });
  scope.querySelectorAll("[data-i18n-date]").forEach(function (el) {
    el.textContent = formatDate(el.getAttribute("data-i18n-date"));
  });
  scope.querySelectorAll("[data-i18n-datetime]").forEach(function (el) {
    el.textContent = formatDateTime(el.getAttribute("data-i18n-datetime"));
  });
  document.documentElement.lang = currentLocale();
  document.querySelectorAll(".locale-select").forEach(function (sel) { sel.value = currentLocale(); });
  if (typeof onLocaleApplied === "function") onLocaleApplied();
}

function setLocale(loc) {
  try { localStorage.setItem("aurora-locale", loc); } catch (e) { /* ignore */ }
  applyI18n();
}

document.addEventListener("DOMContentLoaded", function () { applyI18n(); });
