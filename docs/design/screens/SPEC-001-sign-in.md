# Screen spec — Sign in

Status: ready · Author: ui-designer · Date: 2026-09-11
Backlog: `DESIGN-01` (this task) · Product spec: `../../product/specs/SPEC-001-tenant-provisioning-and-first-login.md` (BR-6, AC-5)
Architecture: `../../decisions/ADR-0009-identity-and-authentication.md`, `../../decisions/ADR-0007-multi-tenancy-database-per-tenant.md` §3.2–§3.3
Prototype: `../prototypes/sign-in.html` (open directly in a browser — no build step, no network fetch)
Components referenced: Buttons (`components.md` §1), Text input (§2), Select (§6), Forms with inline validation (§9, extended by this spec — see below), Dialogs (§14), Toasts (§15)

**Note on scope vs. SPEC-001.** SPEC-001's own "UX reference" section says first sign-in needs no dedicated screen spec, because at the time it was written the only requirement was "a conventional email/password form," fully covered by the base input/button states. This task's brief asks specifically for a fuller sign-in spec ahead of B-15 so the product owner can see real interface direction early, and it surfaces three behaviours SPEC-001's own text does not fully cover (tenant-by-hostname resolution, the anti-enumeration credential response, and mid-session re-authentication). This document supersedes that one line of SPEC-001's UX reference section for the sign-in screen specifically; nothing else in SPEC-001 changes.

---

## Purpose

Let a person who already has an Aurora ERP account reach their tenant, and only their tenant, without ever typing a tenant identifier — and let a person whose session has become invalid mid-work get back to exactly where they were with the minimum possible disruption. This is BR-6 of SPEC-001 ("first login proves the isolation, not just the plumbing") made concrete as a screen, plus ADR-0009 rule 3's mandatory session revalidation.

## User

Any person with an Aurora ERP account attempting to reach a specific tenant — most often, in this milestone, the administrator SPEC-001 just provisioned a working credential for. The same screen serves every later sign-in by every user in every tenant; nothing about it is specific to a first-time administrator (that is the next screen, `SPEC-001-first-run-landing.md`).

## Task flow

1. The person navigates to a URL whose **host** is already bound to one tenant (`catalog.tenant_host`, ADR-0007 §3.2 strategy 1) — e.g. an email link, a bookmark, or their organization's own portal. They never type a tenant key or slug anywhere on this screen.
2. The page resolves the tenant from that host **before rendering the form**, and the heading names it directly ("Sign in to {tenant display name}") so the person can confirm, before typing anything, that they are about to authenticate against the right business — this restates Principle 5's "never make the user guess" one level earlier than any document header can.
3. They enter email and password (component 2) and submit (component 1). The button enters its loading state for the round trip.
4. **Success:** redirected into the app shell, landing on their tenant's Home route (see `SPEC-001-first-run-landing.md` for what a brand-new administrator sees there).
5. **Failure (wrong email or wrong password):** a single, generic banner — see "Anti-enumeration" below. They may retry immediately.
6. **Too many failed attempts:** a distinct, still non-enumerating, lockout message with a wait time.
7. **Host does not resolve to any tenant:** a different page state entirely — there is no tenant name to show, so no email/password form is offered.
8. **Mid-session re-authentication:** while already working somewhere else in the app, ADR-0009 rule 3's 30-minute revalidation check finds the principal no longer valid (password changed elsewhere, account disabled, session explicitly revoked). A blocking dialog appears over the frozen shell; completing it resumes the same route with no navigation away from the page the person was on.

## Layout

```
 ┌───────────────────────────────────────────────────────────┐
 │ 🔒 meridian.aurora.example/sign-in         (browser, mock) │
 └───────────────────────────────────────────────────────────┘

                          [Light|Dark|System]

                    ┌───────────────────────────┐
                    │  A  Aurora ERP             │
                    │                 Language ▾ │
                    │                             │
                    │  Sign in to Meridian        │
                    │  Distribution               │
                    │  Enter your Aurora ERP      │
                    │  credentials.               │
                    │                             │
                    │  [ banner region, hidden ]  │
                    │                             │
                    │  Email                      │
                    │  [_______________________]  │
                    │  Password    Forgot pw?     │
                    │  [_______________________]  │
                    │                             │
                    │  [       Sign in        ]   │
                    │                             │
                    │  Trouble signing in?         │
                    │  Contact your administrator.│
                    └───────────────────────────┘
```

Single column, centered, max-width 400px card on `canvas`, independent of the app shell (there is no tenant/company switcher, nav rail, or notifications — none of it exists yet for an unauthenticated visitor). See the prototype for the unknown-host and re-authentication variants, which replace or overlay this layout respectively.

## Tenant resolution by hostname

The prototype's browser-chrome mock (a non-interactive illustration, `aria-hidden="true"`) exists purely so a reviewer does not have to imagine the address bar — it is never part of the shipped page. What the shipped page actually does: the Blazor Server / ASP.NET Core host resolves the tenant from the request's `Host` header against `catalog.tenant_host` (ADR-0007 §3.2 strategy 1) in the same request that serves this page, before any Razor component renders. The form itself carries no tenant field, hidden or visible — there is nothing to tamper with, because the value never comes from the client.

## Fields

| Field | Type | Format / locale notes |
|---|---|---|
| Email | Text input, `type="email"`, `autocomplete="username"` | No locale-dependent formatting. Validated at the boundary (RFC 5321 length, one `@`); client-side `novalidate` is used deliberately in the prototype and the real form should rely on the same generic invalid-credential response on submit rather than a separate "that's not an email" client message — see "Anti-enumeration." |
| Password | Text input, `type="password"`, `autocomplete="current-password"` | No format. Never echoed, never logged (`CLAUDE.md` — no personal data in logs; a password is more sensitive than personal data and gets the same treatment). |
| Language | Select (`components.md` §6) | Options are exactly this tenant's enabled UI locales: base English plus whatever its installed Country Packages contribute (`app-shell.md` → User menu). The prototype's example tenant, Meridian Distribution, has one such package installed, so it legitimately offers English and Swedish (sv-SE) here — a tenant with none installed would show only English, with no picker rendered at all rather than a picker with one disabled-looking option (an empty choice is a worse empty state than no choice control). Selecting a locale re-renders every string on the page through the lookup described below; it does **not** persist past this page load until the person actually signs in (a real preference belongs to their user profile, set post-authentication). |

## Locale and formatting (Principle 6)

Every visible string on this page is looked up by key, never written literally in a component — the prototype's `data-i18n="signin.heading"` attributes and `docs/design/prototypes/i18n.js`'s `STRINGS` table are the reviewable stand-in for what a real `IStringLocalizer<T>` call and compiled resource file do. Representative keys and both locales this spec ships (`en` is the base per `CLAUDE.md`; `sv-SE` is the demonstration locale showing the layer is real, not English-shaped):

| Key | en | sv-SE |
|---|---|---|
| `signin.heading` | Sign in to {tenant} | Logga in på {tenant} |
| `signin.emailLabel` | Email | E-post |
| `signin.passwordLabel` | Password | Lösenord |
| `signin.forgotPassword` | Forgot password? | Glömt lösenordet? |
| `signin.submit` | Sign in | Logga in |
| `signin.errorInvalid` | The email or password is incorrect. | Fel e-postadress eller lösenord. |
| `signin.errorLockout` | Too many attempts. Try again in {minutes} minutes. | För många försök. Försök igen om {minutes} minuter. |
| `signin.unknownHostHeading` | We couldn't find an Aurora ERP account at this address. | Vi kunde inte hitta något Aurora ERP-konto på den här adressen. |
| `signin.reauthHeading` | Sign in again to continue | Logga in igen för att fortsätta |

The full table (every key used by this screen) is in `i18n.js`. This screen has no numeric or date value to format, so the locale demonstration here is purely string content; `SPEC-002-companies-list.md` and `SPEC-001-first-run-landing.md` carry the date/number-formatting demonstration (a "Created" column and a setup timestamp respectively).

## States

**Empty (initial load).** Form rendered with both fields blank, no banner, submit button enabled. (The prototype pre-fills a demo email for reviewer convenience only — a real first paint has an empty field.)

**Loading (submitting).** Per Buttons component §1: the button's label is replaced by a fixed-width spinner, the button disables, minimum visible duration 150ms. No optimistic navigation — Blazor Server means this is a real round trip and the person stays on this page until the server answers one way or the other (Principle 3).

**Error — invalid credential.** See "Anti-enumeration" below. Both fields get a `danger` border; a single banner above the form states the generic message; focus moves to the banner (`role="alert"`, focused programmatically) so a screen reader announces it immediately without the person having to find it. This is the components.md §9 "non-field-specific error" variant added by this spec.

**Error — locked out.** A distinct `warning`-colored (not `danger`) banner naming the wait time: "Too many attempts. Try again in {N} minutes." This is deliberately a *different* message from the invalid-credential one, but it must never be shown or withheld based on whether the submitted email actually has an account — the failed-attempt counter is keyed on the normalized email address regardless of whether that address exists, specifically so a distinguishable lockout response can never be used to enumerate accounts (OWASP ASVS L2, referenced by ADR-0009). This is a backend requirement this spec is recording so the developer implementing ADR-0009's lockout does not treat it as obvious.

**Error — unknown address (host does not resolve to a tenant).** A different card entirely, no form: a headline stating the address isn't linked to an account, and one line of guidance. No language picker (no tenant is resolved, so there is no locale set to offer). This is the "no permission"-adjacent state for this screen — there is no data to withhold since no tenant identity exists yet, so the honest response is "not found," not a disguised permission error.

**Missing permission.** Not applicable in the ADR-0010 sense (permissions are evaluated inside a resolved tenant; nothing is tenant-scoped yet at sign-in). The nearest equivalent — an authenticated principal whose account has no membership in *this* tenant at all — is out of this spec's scope; SPEC-001's tests only exercise a single administrator signing into the one tenant they were provisioned into. Flagged here rather than silently assumed away, for whoever specifies multi-tenant membership screens later.

**Re-authentication mid-session.** A blocking dialog (`components.md` §14) with `data-modal-blocking` semantics: it does **not** close on `Esc`, unlike the command palette or the company-switch confirmation, because dismissing it would leave the person silently unauthenticated rather than cancel a reversible action — the one way out besides completing it is the explicit "Not you? Sign in as a different user" link, which performs a full sign-out (losing the current route, unlike completing re-authentication, which does not). The dialog shows the account's email as static, non-editable text (the identity is already known; only the credential needs reverifying) and a password field that receives initial focus. If the shell can detect unsaved work elsewhere in the session (the same detection the company switcher already relies on, `components.md` §18), a warning line names the affected screen, matching the tone and placement of the switcher's own unsaved-work interstitial. On success, the dialog closes and a toast confirms; the underlying route does not change.

**Long values.** An email address near RFC 5321's 254-character limit does not truncate in the input (native text inputs scroll internally); the display heading's tenant name is bounded by BR-2's 200-character Company-name limit and wraps rather than truncating, since a sign-in heading has no row-density pressure to justify losing information.

**Thousands of rows / large data.** Not applicable — this screen has no collection to render.

## Anti-enumeration (the requirement this screen exists to make concrete)

Per the brief and ADR-9's ASVS L2 reference, the response to a submitted email/password pair must never let a caller distinguish "no such account" from "account exists, wrong password" from, within reason, "account exists but is disabled." All three collapse to the single banner in `signin.errorInvalid`. This is why components.md §9 needed a new state row rather than reusing its existing per-field error pattern: the existing pattern's whole point is to attach an error to the most actionable field, which is exactly the information this screen must not reveal.

## Keyboard

| Action | Key |
|---|---|
| Move between Language, Email, Forgot-password link, Password, Sign in (in that DOM order — the forgot-password link sits inline with the password field's label, so it is reached just before the field it relates to) | `Tab` / `Shift+Tab` |
| Submit the form from either field | `Enter` |
| Activate a focused link/button | `Enter` / `Space` |
| Skip the (decorative) browser-chrome mock straight to the form | A visually-hidden-until-focused "Skip to sign-in form" link is the first focusable element on the page |
| Close the re-authentication dialog | No shortcut — `Esc` is deliberately inert; see "Re-authentication mid-session" above |
| Dismiss the unknown-address state | Not applicable — there is no form to return to; the person must navigate to a correct address |

## Accessibility notes

- Page `<title>` and the visible `<h1>` both name the resolved tenant, so a screen reader user gets tenant confirmation immediately on landing, matching the sighted restatement in "Tenant resolution by hostname" above.
- The error/lockout banner is `role="alert"` and receives programmatic focus on appearance, so its content is announced without requiring the person to tab to find it — chosen over the Forms component's default "focus the first invalid field" because no individual field is marked as *the* problem here (see "Anti-enumeration").
- Both fields visually marked `danger` on an invalid submission carry no additional per-field `aria-describedby` error text, again deliberately, so an assistive technology user is not given more granular information than a sighted user gets from the shared banner alone.
- The re-authentication dialog follows `components.md` §14's dialog contract: `role="dialog"`, `aria-modal="true"`, `aria-labelledby` pointing at its heading, initial focus on its first real interactive control (the password field, since the account-email line is static text, not an input). Full keyboard focus-trapping inside the dialog is a components.md §14 requirement the Blazor implementation must add; the static prototype demonstrates correct initial focus and non-`Esc`-dismissal only, at the same fidelity as the existing command-palette and company-switch prototypes.
- The language `<select>` has a visible label ("Language") and a matching `aria-label`, per Select component conventions (§6) — never an icon-only, unlabeled control.
- Color is never the only signal: the lockout banner differs from the invalid-credential banner in wording, not merely in `warning` vs. `danger` color, so the distinction survives grayscale/print and screen-reader use identically.

## What we borrowed and why

Per-tenant hostname-resolved sign-in with a named workspace/organization at the top of the form, rather than a tenant-id text field, mirrors Slack's and Notion's "reach your workspace via its own subdomain, never by typing an ID" pattern — adapted here with the explicit tenant-name restatement in the heading itself (Principle 5), since the cost of confirming the wrong business at sign-in is categorically different in an ERP than in a chat app. The forced-re-authentication-without-losing-place pattern mirrors Google's and GitHub's "confirm it's you" interstitials, which keep the underlying page's state intact and re-verify only the credential, rather than a hard sign-out-and-redirect.
