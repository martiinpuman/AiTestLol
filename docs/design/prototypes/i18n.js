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
    "companies.newButtonDisabledReason": "Requires the organization.company.manage permission",
    "companies.newButtonToast": "The create-company form is specified separately (see companies-list.html spec, “Out of scope”).",
    "companies.emptyHeading": "No companies yet",
    "companies.emptyBody": "This shouldn't happen on a provisioned tenant — every tenant starts with one default company. Contact support if you see this.",
    "companies.loadingLabel": "Loading companies…",
    "companies.errorHeading": "We couldn't load your companies",
    "companies.errorBody": "Try again, or contact support if this keeps happening.",
    "companies.retry": "Retry",
    "companies.noAccessHeading": "You don't have access to view companies",
    "companies.noAccessBody": "Ask a tenant administrator for the organization.company.view permission.",
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
    "companies.previewPersona": "Preview as"
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
    "companies.newButtonDisabledReason": "Kräver behörigheten organization.company.manage",
    "companies.newButtonToast": "Formuläret för att skapa företag specificeras separat (se companies-list.html-specen, “Out of scope”).",
    "companies.emptyHeading": "Inga företag än",
    "companies.emptyBody": "Det här ska inte hända för en etablerad klient — varje klient startar med ett standardföretag. Kontakta supporten om du ser det här.",
    "companies.loadingLabel": "Läser in företag…",
    "companies.errorHeading": "Vi kunde inte läsa in dina företag",
    "companies.errorBody": "Försök igen, eller kontakta supporten om problemet kvarstår.",
    "companies.retry": "Försök igen",
    "companies.noAccessHeading": "Du har inte åtkomst att visa företag",
    "companies.noAccessBody": "Be en administratör om behörigheten organization.company.view.",
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
    "companies.previewPersona": "Förhandsgranska som"
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
