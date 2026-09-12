# Design tokens — Aurora ERP

`tokens.json` in this folder is the **single machine-readable source of truth**. This file explains what each group means, why the values are what they are, and documents the WCAG 2.2 AA contrast audit that was run against every meaningful color pair before it was allowed into `tokens.json`. If you change a color in `tokens.json`, re-run `tokens.build.py` (in this folder) and re-check any new pair before shipping it — do not hand-wave a new hex value into the file.

`tokens.build.py` is the reference generator: it defines the palette, runs the same contrast-ratio math this document reports, and writes `tokens.json` (including the `contrastAudit` array, so the JSON and this document can never silently drift apart — the numbers below are copied directly from that array). `prototypes/tokens.css` is a generated CSS custom-properties file produced from `tokens.json`; it is the reference implementation for how the real app should turn this JSON into CSS variables (or Blazor CSS-isolation variables, or a Sass map — whatever the app's build uses, it must be generated from this JSON, not retyped by hand).

## How a developer should implement this

1. Treat `tokens.json` as the only place color/type/space/radius/elevation/motion values are defined. Never hard-code a hex value, a `px` size, or a `ms` duration in component code.
2. Generate CSS custom properties (or your framework's equivalent) from it at build time. `prototypes/tokens.css` shows the exact shape: a `:root` block for light values, a `@media (prefers-color-scheme: dark)` block guarded by `:root:not([data-theme="light"])`, and a `:root[data-theme="dark"]` block for an explicit user toggle — so system preference is respected by default and an explicit choice always wins.
3. Component code references the CSS variable (`var(--color-danger)`), never the raw value, so a future palette revision is a one-file change.
4. Money, date, number and address formatting are **not** design tokens — they come from the active locale / installed Country Package at render time (see `principles.md` #6). Tokens only govern the visual system.

## Color

Every semantic color exists once per theme (`color.light`, `color.dark` in `tokens.json`). Components reference the semantic name (`surface`, `accent`, `danger`, …), never a raw hex value, so retheming is a token-file change only.

### Semantic roles

| Token | Role |
|---|---|
| `canvas` | The outermost app background, behind all panels. |
| `surface` | Default panel/card/table background. |
| `surface-raised` | Modals, popovers, menus — content floating above `surface`. In light theme this equals `surface` (white cannot get "more white"; separation comes from `elevation` shadow alone). In dark theme it is a lighter fill than `surface`, because shadows barely read on dark backgrounds — the same reasoning Material Design 3 uses for its dark elevation overlays. |
| `surface-sunken` | Inset areas: grid header row, code/id blocks, well-like containers. |
| `surface-hover` / `surface-selected` | Row/item hover and selection backgrounds in lists and grids. |
| `overlay` | Modal/dialog backdrop scrim. |
| `text` | Primary reading text. |
| `text-muted` | Secondary text: helper text, secondary grid columns, metadata — still real content, so it must clear 4.5:1, not just "look subtle." |
| `text-subtle` | Placeholder text and disabled-control labels only. WCAG explicitly exempts placeholder text and disabled UI from the contrast requirement, so this value is intentionally lower-contrast — never route real content through it. |
| `text-disabled` | Label text inside a disabled control. Same exemption as above. |
| `text-on-accent` | Text/icon color for anything painted on a solid accent/danger/warning/success/info fill (e.g., a primary button label). Always `#FFFFFF` except on `warning-solid`, where the amber fill is light enough that only a dark label clears AA (see `warning-solid-text`). |
| `border` | Decorative dividers, card outlines, table row separators — where an adjacent visual cue (padding, a different surface, shadow) already marks the boundary. Not required to hit 3:1 (WCAG's non-text contrast rule applies only where the border is the *sole* indicator of a boundary). |
| `border-strong` | Input, select, textarea, and button outlines — the sole indicator of the control's boundary. Must and does clear 3:1 against both `surface` and `canvas` (WCAG 1.4.11). |
| `border-focus` | The focus ring color, equal to `accent`. |
| `accent` | Interactive text, links, icons, and the "text-safe" reading of the brand color. Also doubles as `accent-solid`'s base value in light theme. |
| `accent-solid` / `-hover` / `-active` | Primary button/control fill and its interaction states. |
| `accent-muted` | Selected-nav-item and chip backgrounds. |
| `danger` / `danger-solid` / `danger-muted` | Errors, destructive actions, negative/void states. |
| `warning` / `warning-solid` / `warning-solid-text` / `warning-muted` | Caution states (e.g., "stock below reorder point," "period closes in 2 days"). Note `warning` (text use) and `warning-solid` (fill use) are deliberately different ambers — see below. |
| `success` / `success-solid` / `success-muted` | Confirmations, positive/completed states. |
| `info` / `info-solid` / `info-muted` | Neutral informational banners and badges. |

### Why light and dark aren't just inverted

- **Dark accent/danger/success/info text colors are lighter and more saturated** (e.g., `accent` is `#2454CC` in light but `#8AB2FF` in dark) because a color dark enough to read on white fails outright on a near-black surface. The *solid fill* variants (`accent-solid`, `danger-solid`, `success-solid`) stay closer to their light-theme hue in both themes — deliberately, because a button should not change its brand color identity across themes, only the surrounding chrome should.
- **`warning` has two distinct hex values on purpose, in both themes**: a deepened/brightened value for running text (`warning`, e.g. `#8A5200` light / `#F2B84B` dark, tuned to clear 4.5:1 as small text) and a separate, more saturated amber for solid fills (`warning-solid`, `#C77700` in both themes) paired with a **dark** label (`warning-solid-text`) rather than white — amber is the one hue in this palette where white-on-fill fails AA at any fill saturation worth calling "amber," so the button label flips to dark text instead. This is the one asymmetric rule in the system; every other solid fill uses a white label.
- **Hover/active direction is theme-aware**: light-theme solid buttons darken on hover then darken further on press (matches how ink looks "pressed into" a light surface); dark-theme solid buttons brighten on hover then darken on press. Both directions were re-verified against the 4.5:1 button-label requirement, not just eyeballed.

### Extending the palette safely

`accent-solid` and `danger-solid` ship pre-verified hover/active shades in both themes (see `tokens.json`). If a future screen needs a filled `success` or `info` button (rare — those are normally badges/banners, not buttons), derive hover/active by mixing the base fill with black (light theme, ~10% for hover / ~18% for active) or white (dark theme, ~12% for hover / ~20% for active), then **re-run the contrast check against the paired label color before shipping** — do not assume a derived shade still clears AA; several near-misses were caught this way while building this palette (see `tokens.build.py`'s `solids2` exploration in the git history of this session, kept informally — the discipline is what matters, not the specific rejected hex values).

## WCAG 2.2 AA contrast audit

Method: relative luminance and contrast ratio computed per the WCAG formula (`(L1+0.05)/(L2+0.05)` on relative luminance from linearized sRGB), scripted in `tokens.build.py`, not eyeballed. Thresholds applied: **4.5:1** for normal text (criterion 1.4.3) and **3.0:1** for large text and non-text UI component boundaries/indicators (criterion 1.4.11 — input borders, focus rings, button fills against their surrounding surface). **68 of the 70 pairs below pass; none of those 68 were rounded up or asterisked past a threshold.** The remaining 2 are a real, deliberate exception — a combination no component actually uses — explained where they appear in the dark-theme table below, not hidden from the count.

### Light theme

| Pair | Foreground | Background | Ratio | Required | Result |
|---|---|---|---|---|---|
| text on canvas | `#1B1F24` | `#F4F5F7` | 15.18:1 | 4.5:1 | Pass |
| text on surface | `#1B1F24` | `#FFFFFF` | 16.56:1 | 4.5:1 | Pass |
| text on surface-sunken | `#1B1F24` | `#EBEDF0` | 14.12:1 | 4.5:1 | Pass |
| text-muted on canvas | `#57616C` | `#F4F5F7` | 5.78:1 | 4.5:1 | Pass |
| text-muted on surface | `#57616C` | `#FFFFFF` | 6.30:1 | 4.5:1 | Pass |
| text-muted on surface-sunken | `#57616C` | `#EBEDF0` | 5.37:1 | 4.5:1 | Pass |
| accent text/link on surface | `#2454CC` | `#FFFFFF` | 6.53:1 | 4.5:1 | Pass |
| accent text/link on canvas | `#2454CC` | `#F4F5F7` | 5.99:1 | 4.5:1 | Pass |
| on-accent on accent-solid (button label) | `#FFFFFF` | `#2454CC` | 6.53:1 | 4.5:1 | Pass |
| danger text on surface | `#B3261E` | `#FFFFFF` | 6.54:1 | 4.5:1 | Pass |
| danger text on danger-muted | `#B3261E` | `#FCEBEA` | 5.67:1 | 4.5:1 | Pass |
| white on danger-solid (button label) | `#FFFFFF` | `#B3261E` | 6.54:1 | 4.5:1 | Pass |
| warning text on surface | `#8A5200` | `#FFFFFF` | 6.39:1 | 4.5:1 | Pass |
| warning text on warning-muted | `#8A5200` | `#FCEEDA` | 5.59:1 | 4.5:1 | Pass |
| warning-solid-text on warning-solid (button label) | `#1B1F24` | `#C77700` | 4.78:1 | 4.5:1 | Pass |
| success text on surface | `#146C2E` | `#FFFFFF` | 6.53:1 | 4.5:1 | Pass |
| success text on success-muted | `#146C2E` | `#E6F4EA` | 5.75:1 | 4.5:1 | Pass |
| info text on surface | `#0B5FA5` | `#FFFFFF` | 6.57:1 | 4.5:1 | Pass |
| info text on info-muted | `#0B5FA5` | `#E5F1FB` | 5.73:1 | 4.5:1 | Pass |
| border-strong vs surface (input boundary) | `#7C8590` | `#FFFFFF` | 3.74:1 | 3.0:1 | Pass |
| border-strong vs canvas (input boundary) | `#7C8590` | `#F4F5F7` | 3.43:1 | 3.0:1 | Pass |
| focus ring vs surface | `#2454CC` | `#FFFFFF` | 6.53:1 | 3.0:1 | Pass |
| focus ring vs canvas | `#2454CC` | `#F4F5F7` | 5.99:1 | 3.0:1 | Pass |
| accent-solid fill vs surface (button visibility) | `#2454CC` | `#FFFFFF` | 6.53:1 | 3.0:1 | Pass |
| danger-solid fill vs surface (button visibility) | `#B3261E` | `#FFFFFF` | 6.54:1 | 3.0:1 | Pass |
| success-solid fill vs surface (icon/button visibility) | `#146C2E` | `#FFFFFF` | 6.53:1 | 3.0:1 | Pass |
| warning-solid fill vs surface (button visibility) | `#C77700` | `#FFFFFF` | 3.46:1 | 3.0:1 | Pass |
| text on surface-raised | `#1B1F24` | `#FFFFFF` | 16.56:1 | 4.5:1 | Pass |
| text-muted on surface-raised | `#57616C` | `#FFFFFF` | 6.30:1 | 4.5:1 | Pass |
| border-strong vs surface-raised (input boundary) | `#7C8590` | `#FFFFFF` | 3.74:1 | 3.0:1 | Pass |
| focus ring vs surface-raised | `#2454CC` | `#FFFFFF` | 6.53:1 | 3.0:1 | Pass |
| accent-solid fill vs surface-raised (button visibility) | `#2454CC` | `#FFFFFF` | 6.53:1 | 3.0:1 | Pass |
| danger-solid fill vs surface-raised (button visibility) | `#B3261E` | `#FFFFFF` | 6.54:1 | 3.0:1 | Pass |
| accent-solid fill vs surface-sunken (dialog action row) | `#2454CC` | `#EBEDF0` | 5.57:1 | 3.0:1 | Pass |
| danger-solid fill vs surface-sunken (dialog action row) | `#B3261E` | `#EBEDF0` | 5.57:1 | 3.0:1 | Pass |

### Dark theme

| Pair | Foreground | Background | Ratio | Required | Result |
|---|---|---|---|---|---|
| text on canvas | `#E7E9EC` | `#14171B` | 14.78:1 | 4.5:1 | Pass |
| text on surface | `#E7E9EC` | `#1C2027` | 13.43:1 | 4.5:1 | Pass |
| text on surface-sunken | `#E7E9EC` | `#101317` | 15.31:1 | 4.5:1 | Pass |
| text-muted on canvas | `#A9B0BA` | `#14171B` | 8.22:1 | 4.5:1 | Pass |
| text-muted on surface | `#A9B0BA` | `#1C2027` | 7.47:1 | 4.5:1 | Pass |
| text-muted on surface-sunken | `#A9B0BA` | `#101317` | 8.52:1 | 4.5:1 | Pass |
| accent text/link on surface | `#8AB2FF` | `#1C2027` | 7.70:1 | 4.5:1 | Pass |
| accent text/link on canvas | `#8AB2FF` | `#14171B` | 8.47:1 | 4.5:1 | Pass |
| on-accent on accent-solid (button label) | `#FFFFFF` | `#3565CE` | 5.38:1 | 4.5:1 | Pass |
| danger text on surface | `#FF8A80` | `#1C2027` | 7.16:1 | 4.5:1 | Pass |
| danger text on danger-muted | `#FF8A80` | `#3A1B18` | 6.81:1 | 4.5:1 | Pass |
| white on danger-solid (button label) | `#FFFFFF` | `#C4392F` | 5.29:1 | 4.5:1 | Pass |
| warning text on surface | `#F2B84B` | `#1C2027` | 9.13:1 | 4.5:1 | Pass |
| warning text on warning-muted | `#F2B84B` | `#3A2A0E` | 7.73:1 | 4.5:1 | Pass |
| warning-solid-text on warning-solid (button label) | `#10141A` | `#C77700` | 5.34:1 | 4.5:1 | Pass |
| success text on surface | `#6FDB94` | `#1C2027` | 9.52:1 | 4.5:1 | Pass |
| success text on success-muted | `#6FDB94` | `#123420` | 7.96:1 | 4.5:1 | Pass |
| info text on surface | `#7CC0FF` | `#1C2027` | 8.43:1 | 4.5:1 | Pass |
| info text on info-muted | `#7CC0FF` | `#122A3E` | 7.60:1 | 4.5:1 | Pass |
| border-strong vs surface (input boundary) | `#6B7480` | `#1C2027` | 3.45:1 | 3.0:1 | Pass |
| border-strong vs canvas (input boundary) | `#6B7480` | `#14171B` | 3.80:1 | 3.0:1 | Pass |
| focus ring vs surface | `#8AB2FF` | `#1C2027` | 7.70:1 | 3.0:1 | Pass |
| focus ring vs canvas | `#8AB2FF` | `#14171B` | 8.47:1 | 3.0:1 | Pass |
| accent-solid fill vs surface (button visibility) | `#3565CE` | `#1C2027` | 3.04:1 | 3.0:1 | Pass |
| danger-solid fill vs surface (button visibility) | `#C4392F` | `#1C2027` | 3.09:1 | 3.0:1 | Pass |
| success-solid fill vs surface (icon/button visibility) | `#1E7A3E` | `#1C2027` | 3.04:1 | 3.0:1 | Pass |
| warning-solid fill vs surface (button visibility) | `#C77700` | `#1C2027` | 4.72:1 | 3.0:1 | Pass |
| text on surface-raised | `#E7E9EC` | `#262B33` | 11.70:1 | 4.5:1 | Pass |
| text-muted on surface-raised | `#A9B0BA` | `#262B33` | 6.51:1 | 4.5:1 | Pass |
| border-strong vs surface-raised (input boundary) | `#6B7480` | `#262B33` | 3.01:1 | 3.0:1 | **Pass — by 0.01** |
| focus ring vs surface-raised | `#8AB2FF` | `#262B33` | 6.70:1 | 3.0:1 | Pass |
| accent-solid fill vs surface-raised (button visibility) | `#3565CE` | `#262B33` | 2.65:1 | 3.0:1 | **Fail** |
| danger-solid fill vs surface-raised (button visibility) | `#C4392F` | `#262B33` | 2.69:1 | 3.0:1 | **Fail** |
| accent-solid fill vs surface-sunken (dialog action row) | `#3565CE` | `#101317` | 3.46:1 | 3.0:1 | Pass |
| danger-solid fill vs surface-sunken (dialog action row) | `#C4392F` | `#101317` | 3.52:1 | 3.0:1 | Pass |

**The two `Fail` rows above are real, measured, and deliberately left in this table** — added by `DESIGN-002`'s rework after a review found that `surface-raised` (the background of every dialog, popover and menu in the system) had **zero** audited pairs despite two solid-fill buttons depending on it, a gap `SPEC-002-company-create.md` finding 2 had already measured by hand in prose and never promoted into this table — the mechanism that is supposed to keep this audit authoritative had never actually been re-run over it. The fix is not a palette change: `components.md` §14's dialog anatomy and `prototypes/app.css`'s `.dialog .actions` rule now seat every dialog's button row on `surface-sunken` rather than the bare `surface-raised` background, which is exactly the two `Pass` rows immediately below the failures — measured on the *same* two fills, the *same* dark theme, the only variable being which surface token the button sits on. **No component in this system renders a solid `accent`/`danger` button directly on bare `surface-raised`** — this table keeps the failing combination visible anyway, as the permanent, checkable record of why the CSS rule has to exist, rather than removing an inconvenient row now that a fix exists elsewhere.

Three pairs are the closest margins in the whole system and deserve a flag for anyone editing the palette later: **light `warning-solid-text` on `warning-solid`** (4.78:1, needs 4.5:1), **dark `accent-solid`/`success-solid` fill vs `surface`** (3.04:1, needs 3.0:1), and **dark `border-strong` vs `surface-raised`** (3.01:1, needs 3.0:1 — the newest and tightest of the three). All three pass today; do not nudge those specific hex pairs without re-running `tokens.build.py`'s audit.

**The audit script itself had a bug that mattered here.** `tokens.build.py` wrote its output to a path in a previous agent's temporary scratch directory, not to `docs/design/tokens.json` — meaning running it, as this file's own header instructs after any edit, never actually updated the checked-in token values or the audit table at all. Found while adding the rows above (the script needed to genuinely write them back for this section to be real). Fixed: it now writes next to itself, `docs/design/tokens.json`, regardless of the working directory it's invoked from. Re-run this round; printed **70 total audit rows, 2 failed** (both are the deliberate, documented `surface-raised` rows above) — the exact output is reproducible by running `python3 docs/design/tokens.build.py`.

`text-subtle` and `text-disabled` are intentionally excluded from this audit — WCAG 1.4.3/1.4.11 explicitly exempt placeholder text and disabled controls, and these tokens exist precisely to be used only in those two exempt cases. Never use them for a value the user is meant to read as data.

## Typography

- **Font stack**: `"Segoe UI", -apple-system, BlinkMacSystemFont, "Inter", Roboto, Helvetica, Arial, sans-serif` for UI text — a system-font-first stack so Blazor Server pages render text immediately with zero web-font download latency, which matters when every navigation is already a round trip. A monospace stack (`"Cascadia Mono", "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace`) is reserved for identifiers, account codes, and anywhere columns of digits must align visually.
- **Tabular numerals**: every numeric/money column sets `font-feature-settings: "tnum" 1, "lnum" 1` so digits are fixed-width and right-aligned columns of amounts stay visually aligned — non-negotiable for a data grid showing a column of money values.
- **Scale** (`typography.scale` in `tokens.json`): `micro` (11px, grid meta/timestamps) → `small` (12px, compact grid cells/badges) → `body` (13px, the default for forms and comfortable grids — deliberately smaller than a typical 16px web body because this is dense professional software, not a marketing page) → `body-lg` (15px, dialog copy) → `heading-sm` (17px, panel/section titles) → `heading-md` (20px, page/dialog titles) → `heading-lg` (24px, rare full-page moments) → `display` (30px, dashboard KPI numbers only). Every size in this scale was included in the contrast audit's worst case (smallest, lowest-weight text), so there is no "the small print doesn't need to pass" exception anywhere in this system.

## Spacing

4px base grid: `space-0` through `space-24` map to `n × 4px` (see `tokens.json` → `spacing`). Components compose from these tokens only; no ad hoc pixel values in component CSS.

## Radius

`radius-xs` (2px: checkboxes, small chips) · `radius-sm` (4px: inputs, buttons — the default control radius) · `radius-md` (6px: cards, panels) · `radius-lg` (10px: modals, dialogs) · `radius-full` (999px: pills, badges, avatars).

## Elevation

Four steps (`elevation-1` through `elevation-4`), from a barely-there card shadow up to the highest layer (toasts). Light theme expresses elevation purely through shadow depth/spread; dark theme pairs shadow with a lighter surface fill per step (`surface` → `surface-raised`) because shadow alone reads poorly on a dark background — see `tokens.json` → `elevation.note` for the exact reasoning, borrowed from Material Design 3's dark-elevation model.

## Motion

Durations from `duration-instant` (80ms — checkbox/toggle feedback) to `duration-slower` (320ms — rare page-level transitions), with `easing-standard`/`-entrance`/`-exit` cubic-béziers. **Motion decorates a state change; it is never the thing a user waits on.** Because Blazor Server means every mutating action already went over a SignalR round trip before any animation plays, no duration in this file should ever be interpreted as "how long the network call takes" — see `components.md` for the actual pending/saving state machine, and `app-shell.md` for the reconnection banner. `prefers-reduced-motion: reduce` collapses all durations to 1ms, except a busy/spinner indicator, which instead runs at half speed so a pending state stays perceivable rather than disappearing entirely.

## Breakpoints & density

Desktop-first: **1200-1439px** is the primary design target (dense multi-pane layouts); **≥1440px** gets more breathing room, not more density; **834-1199px** (tablet) collapses the nav rail to icons and turns master-detail into one visible pane at a time with a back action. Below 834px is not a supported target for v1 — see `app-shell.md` for the exact responsive behavior per breakpoint.

Two density presets ship (`tokens.json` → `density`): `compact` (32px rows / 12px type — the default for grids and the document line editor) and `comfortable` (44px rows / 13px type — forms, tablet/touch sessions, and available as a per-user preference for anyone who wants larger targets). This is a corollary of Principle 1 (density is a feature) balanced against Principle 7 (fast paths beside, not instead of, the accessible path).
