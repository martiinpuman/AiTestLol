# ADR-0022 — Localization and formatting

- **Status:** Accepted (2026-09-11)
- **Deciders:** architect
- **Related:** ADR-0005 (Blazor Server), ADR-0008 §7 point 8, ADR-0017 (validation messages)

## Context

`CLAUDE.md`: English is the base locale, **every** user-facing string is localized from day one, and a Country Package must be able to ship additional locales without touching core code. Dates, numbers and currency format per locale. Business Central's model — an XLIFF translation pipeline that is **separate** from fiscal localization — is the evidence that these two concerns should be independently installable even though most products fuse them (`../research/features/localization-packages.md`).

Blazor Server adds a specific trap: culture is per **circuit**, and the familiar `CultureInfo.DefaultThreadCurrentCulture` approach is a process-wide setting that will serve one user's language to another.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **`.resx` + `IStringLocalizer`, culture per circuit, locales shippable in a Country Package** *(chosen)* | In-box, tooled, understood; `.resx` exports cleanly to XLIFF for translators; packages contribute locales through the existing extension point | `.resx` is clunky to hand-edit; plural rules need explicit keys |
| Database-stored translations | Editable at runtime by a tenant | Every string render becomes a query or a cache read; merge and versioning across releases become our problem; translators lose their tooling |
| A translation SaaS | Good translator workflow | Forbidden by the hard limits (sign-ups, spending) |
| Defer localization; hard-code English now | Faster to first screen | Explicitly forbidden, and retrofitting localization into an ERP UI is a months-long grind. Rejected |

## Decision

1. **All user-facing text comes from resources**, addressed by key. Hard-coded strings are a build failure (fitness rule A1). This includes validation messages (ADR-0017), Problem Details titles, email and document templates, and enum display names.
2. **Base locale is `en`.** Every key must exist in `en`; a missing key in another locale falls back to `en` and is reported, never rendered as the raw key to a user.
3. **Culture resolution order:** user preference → company default → tenant default → the browser's `Accept-Language` → `en`.
4. **Culture is set per circuit, not per process.** `CultureInfo.DefaultThreadCurrentCulture` and `DefaultThreadCurrentUICulture` are banned (fitness rule); the circuit handler sets culture on its own execution context, and background jobs set culture from the tenant/company of the job, never from whatever the last request happened to be.
5. **ICU is required.** `InvariantGlobalization` is `false` and the container image includes ICU (`../architecture/solution-layout.md` §4, ADR-0025). An invariant-globalization container silently formats every date and number wrongly and is a genuinely unpleasant bug to diagnose.
6. **Display culture and document culture are different things.** A screen renders in the *user's* culture. An invoice PDF or e-invoice renders in the *document's* culture — the customer's language and the company's jurisdiction conventions. A German user looking at a French customer's invoice sees the screen in German and the invoice in French. This distinction is designed in now because retrofitting it means touching every document template.
7. **Dates, times and time zones:** instants are `timestamptz` in UTC and displayed in the company's IANA time zone; accounting dates are `date` and are **never** time-zone-converted (ADR-0004 rule 4). Conflating the two produces period-boundary bugs that are painful to unwind.
8. **Plurals:** explicit keys per category (`Item_One`, `Item_Other`) via .NET's localization. ICU MessageFormat is not adopted now; when a target language needs more plural categories than .NET's model handles, that is the moment to adopt it — with a written justification, as any novelty requires.
9. **Country Packages contribute locales** through `ILocalePack` (ADR-0008 §7 point 8), and the manifest's `localeOnly: true` allows a translation-only package with no fiscal content — the Business Central lesson, adopted deliberately.
10. **Translator pipeline:** `.resx` is the source of truth; export to XLIFF for translators; import back. Nobody hand-edits `.resx` XML for a translation.
11. **Right-to-left** is supported by the design tokens' use of logical CSS properties (`docs/design/tokens.md`) and is untested until a package needs it. Recorded as a known gap rather than an implied capability.

## Consequences

- Positive: adding a language is a resource file and a package release — no core change, which is exactly what the Country Package contract promises.
- Positive: the separation of locale packs from fiscal packages means a market can be translated before it is fiscally supported, and vice versa.
- Positive: per-circuit culture avoids the most common Blazor Server localization defect.
- Negative: every string needs a key, including the trivial ones. This is a permanent, small tax on every UI change, and it is the only thing that actually works.
- Negative: document culture versus display culture doubles the formatting paths in templates. Deliberate.
- Negative: `.resx` merge conflicts on a busy branch are annoying. Mitigation: one resource file per module rather than one global file.

## Revisit when

A language requires plural or gender rules beyond `.resx` (adopt ICU MessageFormat then), a tenant needs to override individual strings at runtime (a `platform.locale_override` table already exists in the Localization module for this, but the rendering path would need caching work), or RTL becomes a real requirement.
