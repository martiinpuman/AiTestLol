import json
import os

def hex_to_rgb(h):
    h = h.lstrip('#')
    if len(h) == 3:
        h = ''.join(c*2 for c in h)
    return tuple(int(h[i:i+2], 16) for i in (0, 2, 4))

def srgb_to_lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4

def rel_lum(hexcolor):
    r, g, b = hex_to_rgb(hexcolor)
    r, g, b = srgb_to_lin(r), srgb_to_lin(g), srgb_to_lin(b)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b

def contrast(hex1, hex2):
    l1, l2 = rel_lum(hex1), rel_lum(hex2)
    l1, l2 = max(l1, l2), min(l1, l2)
    return round((l1 + 0.05) / (l2 + 0.05), 2)

# ---------------------------------------------------------------------------
# COLOR PRIMITIVES
# ---------------------------------------------------------------------------

light = {
    "canvas": "#F4F5F7",
    "surface": "#FFFFFF",
    "surfaceRaised": "#FFFFFF",
    "surfaceSunken": "#EBEDF0",
    "surfaceHover": "#F1F3F5",
    "surfaceSelected": "#E7EEFC",
    "overlay": "rgba(17,20,24,0.48)",

    "text": "#1B1F24",
    "textMuted": "#57616C",
    "textSubtle": "#818A94",
    "textDisabled": "#A7ADB5",
    "textOnAccent": "#FFFFFF",

    "border": "#D6DAE0",
    "borderStrong": "#7C8590",
    "borderFocus": "#2454CC",

    "accent": "#2454CC",
    "accentHover": "#1D45AA",
    "accentActive": "#173989",
    "accentMuted": "#E7EEFC",
    "accentSolid": "#2454CC",
    "accentSolidHover": "#1D45AA",
    "accentSolidActive": "#173989",

    "danger": "#B3261E",
    "dangerMuted": "#FCEBEA",
    "dangerSolid": "#B3261E",
    "dangerSolidHover": "#96201A",
    "dangerSolidActive": "#7A1A15",

    "warning": "#8A5200",
    "warningMuted": "#FCEEDA",
    "warningSolid": "#C77700",
    "warningSolidText": "#1B1F24",

    "success": "#146C2E",
    "successMuted": "#E6F4EA",
    "successSolid": "#146C2E",

    "info": "#0B5FA5",
    "infoMuted": "#E5F1FB",
    "infoSolid": "#0B5FA5",
}

dark = {
    "canvas": "#14171B",
    "surface": "#1C2027",
    "surfaceRaised": "#262B33",
    "surfaceSunken": "#101317",
    "surfaceHover": "#262B33",
    "surfaceSelected": "#1E2A44",
    "overlay": "rgba(0,0,0,0.6)",

    "text": "#E7E9EC",
    "textMuted": "#A9B0BA",
    "textSubtle": "#6B7280",
    "textDisabled": "#5B6472",
    "textOnAccent": "#FFFFFF",

    "border": "#333941",
    "borderStrong": "#6B7480",
    "borderFocus": "#8AB2FF",

    "accent": "#8AB2FF",
    "accentHover": "#A8C6FF",
    "accentActive": "#6C99EE",
    "accentMuted": "#1E2A44",
    "accentSolid": "#3565CE",
    "accentSolidHover": "#3A69D4",
    "accentSolidActive": "#2C56B0",

    "danger": "#FF8A80",
    "dangerMuted": "#3A1B18",
    "dangerSolid": "#C4392F",
    "dangerSolidHover": "#D8493D",
    "dangerSolidActive": "#A72F26",

    "warning": "#F2B84B",
    "warningMuted": "#3A2A0E",
    "warningSolid": "#C77700",
    "warningSolidText": "#10141A",

    "success": "#6FDB94",
    "successMuted": "#123420",
    "successSolid": "#1E7A3E",

    "info": "#7CC0FF",
    "infoMuted": "#122A3E",
    "infoSolid": "#1D6FB8",
}

# ---------------------------------------------------------------------------
# CONTRAST AUDIT — every pair actually used as text-on-background or as a
# non-decorative UI boundary/indicator. threshold 4.5 = WCAG 2.2 AA normal
# text (1.4.3); 3.0 = AA large text OR non-text UI component contrast (1.4.11).
# ---------------------------------------------------------------------------

def audit_theme(name, p):
    rows = [
        ("text on canvas",              p["text"], p["canvas"], 4.5, "1.4.3"),
        ("text on surface",             p["text"], p["surface"], 4.5, "1.4.3"),
        ("text on surface-sunken",      p["text"], p["surfaceSunken"], 4.5, "1.4.3"),
        ("text-muted on canvas",        p["textMuted"], p["canvas"], 4.5, "1.4.3"),
        ("text-muted on surface",       p["textMuted"], p["surface"], 4.5, "1.4.3"),
        ("text-muted on surface-sunken",p["textMuted"], p["surfaceSunken"], 4.5, "1.4.3"),
        ("accent text/link on surface", p["accent"], p["surface"], 4.5, "1.4.3"),
        ("accent text/link on canvas",  p["accent"], p["canvas"], 4.5, "1.4.3"),
        ("on-accent on accent-solid (button label)", p["textOnAccent"], p["accentSolid"], 4.5, "1.4.3"),
        ("danger text on surface",      p["danger"], p["surface"], 4.5, "1.4.3"),
        ("danger text on danger-muted", p["danger"], p["dangerMuted"], 4.5, "1.4.3"),
        ("on-accent on danger-solid (button label)", "#FFFFFF", p["dangerSolid"], 4.5, "1.4.3"),
        ("warning text on surface",     p["warning"], p["surface"], 4.5, "1.4.3"),
        ("warning text on warning-muted", p["warning"], p["warningMuted"], 4.5, "1.4.3"),
        ("warning-solid-text on warning-solid (button label)", p["warningSolidText"], p["warningSolid"], 4.5, "1.4.3"),
        ("success text on surface",     p["success"], p["surface"], 4.5, "1.4.3"),
        ("success text on success-muted", p["success"], p["successMuted"], 4.5, "1.4.3"),
        ("info text on surface",        p["info"], p["surface"], 4.5, "1.4.3"),
        ("info text on info-muted",     p["info"], p["infoMuted"], 4.5, "1.4.3"),
        ("border-strong vs surface (input boundary)", p["borderStrong"], p["surface"], 3.0, "1.4.11"),
        ("border-strong vs canvas (input boundary)",  p["borderStrong"], p["canvas"], 3.0, "1.4.11"),
        ("focus ring vs surface",       p["borderFocus"], p["surface"], 3.0, "1.4.11"),
        ("focus ring vs canvas",        p["borderFocus"], p["canvas"], 3.0, "1.4.11"),
        ("accent-solid fill vs surface (button visibility)", p["accentSolid"], p["surface"], 3.0, "1.4.11"),
        ("danger-solid fill vs surface (button visibility)", p["dangerSolid"], p["surface"], 3.0, "1.4.11"),
        ("success-solid fill vs surface (icon/button visibility)", p["successSolid"], p["surface"], 3.0, "1.4.11"),
        ("warning-solid fill vs surface (button visibility)", p["warningSolid"], p["surface"], 3.0, "1.4.11"),
        # surface-raised pairs — added by DESIGN-002 rework. Every dialog,
        # popover and menu in this system is painted on surfaceRaised
        # (tokens.md's own semantic-roles table says so), and until this
        # pass NOT ONE of the 54 rows above used it — every dialog-hosted
        # text, input boundary, focus ring and button fill in the audit had
        # only ever been checked against `surface`. SPEC-002's own finding 2
        # measured the accent-solid/dark case ad hoc in prose and never
        # promoted it into this array, so the mechanism that is supposed to
        # keep this audit authoritative never actually re-ran over it. Fixed
        # here, not just written about here.
        ("text on surface-raised",        p["text"], p["surfaceRaised"], 4.5, "1.4.3"),
        ("text-muted on surface-raised",  p["textMuted"], p["surfaceRaised"], 4.5, "1.4.3"),
        ("border-strong vs surface-raised (input boundary)", p["borderStrong"], p["surfaceRaised"], 3.0, "1.4.11"),
        ("focus ring vs surface-raised",  p["borderFocus"], p["surfaceRaised"], 3.0, "1.4.11"),
        ("accent-solid fill vs surface-raised (button visibility)", p["accentSolid"], p["surfaceRaised"], 3.0, "1.4.11"),
        ("danger-solid fill vs surface-raised (button visibility)", p["dangerSolid"], p["surfaceRaised"], 3.0, "1.4.11"),
        # The two rows above are a combination `components.md` #14 / app.css
        # now guarantee never actually occurs (a dialog's own solid button
        # never sits on the bare surface-raised background — its action row
        # is seated on surface-sunken instead, checked below). Both rows
        # stay in this audit anyway, deliberately failing, as the permanent
        # record of why that CSS rule exists — removing an inconvenient
        # failing row is not how this project fixes a contrast problem.
        ("accent-solid fill vs surface-sunken (dialog action row)", p["accentSolid"], p["surfaceSunken"], 3.0, "1.4.11"),
        ("danger-solid fill vs surface-sunken (dialog action row)", p["dangerSolid"], p["surfaceSunken"], 3.0, "1.4.11"),
    ]
    out = []
    for label, fg, bg, threshold, sc in rows:
        r = contrast(fg, bg)
        out.append({
            "pair": label, "theme": name, "foreground": fg, "background": bg,
            "ratio": r, "threshold": threshold, "criterion": sc,
            "pass": r >= threshold,
        })
    return out

audit = audit_theme("light", light) + audit_theme("dark", dark)
failed = [a for a in audit if not a["pass"]]

typography = {
    "fontFamily": {
        "ui": "\"Segoe UI\", -apple-system, BlinkMacSystemFont, \"Inter\", Roboto, Helvetica, Arial, sans-serif",
        "mono": "\"Cascadia Mono\", \"SFMono-Regular\", Consolas, \"Liberation Mono\", Menlo, monospace",
    },
    "numericFeatureSettings": "\"tnum\" 1, \"lnum\" 1",
    "scale": {
        "micro":   {"size": 11, "lineHeight": 16, "weight": 400, "use": "grid meta text, timestamps, footnotes"},
        "small":   {"size": 12, "lineHeight": 16, "weight": 400, "use": "compact grid cells, secondary labels, badges"},
        "body":    {"size": 13, "lineHeight": 20, "weight": 400, "use": "default UI text: forms, comfortable grid cells, menus"},
        "bodyLg":  {"size": 15, "lineHeight": 22, "weight": 400, "use": "dialog body copy, primary document values"},
        "headingSm": {"size": 17, "lineHeight": 24, "weight": 600, "use": "panel/section titles, card headers"},
        "headingMd": {"size": 20, "lineHeight": 28, "weight": 600, "use": "page titles, dialog titles"},
        "headingLg": {"size": 24, "lineHeight": 32, "weight": 600, "use": "rare: full-page empty states, onboarding"},
        "display":   {"size": 30, "lineHeight": 36, "weight": 600, "use": "KPI numbers on dashboards only"},
    },
    "weights": {"regular": 400, "medium": 500, "semibold": 600, "bold": 700},
}

spacing_steps = [0, 1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 24]
spacing = {f"space-{n}": n * 4 for n in spacing_steps}

radius = {
    "radius-xs": 2,
    "radius-sm": 4,
    "radius-md": 6,
    "radius-lg": 10,
    "radius-full": 999,
}

elevation = {
    "light": {
        "elevation-0": "none",
        "elevation-1": "0 1px 2px rgba(16,20,24,0.06), 0 1px 1px rgba(16,20,24,0.04)",
        "elevation-2": "0 2px 6px rgba(16,20,24,0.10), 0 1px 2px rgba(16,20,24,0.06)",
        "elevation-3": "0 8px 24px rgba(16,20,24,0.16), 0 2px 6px rgba(16,20,24,0.08)",
        "elevation-4": "0 16px 40px rgba(16,20,24,0.22), 0 4px 10px rgba(16,20,24,0.10)",
    },
    "dark": {
        "elevation-0": "none",
        "elevation-1": "0 1px 2px rgba(0,0,0,0.40)",
        "elevation-2": "0 2px 8px rgba(0,0,0,0.48)",
        "elevation-3": "0 10px 28px rgba(0,0,0,0.56)",
        "elevation-4": "0 20px 48px rgba(0,0,0,0.64)",
    },
    "note": "Dark theme pairs shadow with a lighter surface fill per elevation step (surface -> surfaceRaised) because shadows read poorly on dark backgrounds; light theme relies on shadow alone since surface is already the lightest available fill (Material Design 3 dark-elevation approach).",
}

motion = {
    "duration-instant": "80ms",
    "duration-fast": "120ms",
    "duration-base": "180ms",
    "duration-slow": "240ms",
    "duration-slower": "320ms",
    "easing-standard": "cubic-bezier(0.2, 0, 0, 1)",
    "easing-entrance": "cubic-bezier(0, 0, 0.2, 1)",
    "easing-exit": "cubic-bezier(0.4, 0, 1, 1)",
    "reducedMotion": "All durations collapse to 1ms and transform/opacity-only entrances are replaced with instant show/hide when prefers-reduced-motion: reduce is set, except the busy/spinner indicator which keeps a slowed-down (2x duration) animation so pending state stays perceivable.",
    "note": "Blazor Server renders after a SignalR round trip. Motion here decorates a state change that already happened on the server's response, or gives immediate optimistic feedback before that response arrives; motion is never the mechanism the user waits on, and no duration here should be read as expected network latency.",
}

breakpoints = {
    "tablet-min": 834,
    "desktop-min": 1200,
    "desktop-wide": 1440,
    "note": "Desktop-first. 1200-1439px is the primary design target (dense multi-pane layouts). >=1440px gets more breathing room, not more density. 834-1199px (tablet) collapses the nav rail and master-detail to one pane at a time. Below 834px is not a supported target for v1.",
}

density = {
    "compact": {"rowHeight": 32, "fontSize": 12, "use": "data grids, ledgers, document line editor by default"},
    "comfortable": {"rowHeight": 44, "fontSize": 13, "use": "forms, tablet/touch input, first-time users (toggle per user preference)"},
}

tokens = {
    "$schema": "https://aurora-erp.internal/design/tokens.schema.json",
    "meta": {
        "product": "Aurora ERP",
        "version": "1.0.0",
        "generated": "2026-09-12",
        "generator": "docs/design/tokens.build.py — re-run after editing any value in this file's source arrays; do not hand-edit the color blocks without re-running the WCAG audit.",
        "usage": "This file is the single source of truth. Generate CSS custom properties from color.light/color.dark (as [data-theme] blocks), typography.scale, spacing, radius, elevation and motion. Do not hand-author a second copy of these values in code.",
    },
    "color": {"light": light, "dark": dark},
    "contrastAudit": audit,
    "typography": typography,
    "spacing": spacing,
    "radius": radius,
    "elevation": elevation,
    "motion": motion,
    "breakpoints": breakpoints,
    "density": density,
}

# Regenerates docs/design/tokens.json IN PLACE, next to this script, no
# matter what directory the script is invoked from. This was previously a
# hard-coded path into a specific agent's temp scratchpad — meaning running
# this script never actually updated the checked-in tokens.json at all, so
# this file's own "re-run after editing any value" instruction pointed at a
# mechanism that did not do what it claimed. Fixed by DESIGN-002's rework,
# found while adding the surface-raised pairs below and needing this script
# to genuinely write them back.
OUTPUT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "tokens.json")
with open(OUTPUT_PATH, "w") as f:
    json.dump(tokens, f, indent=2)
    f.write("\n")

print("Total audit rows:", len(audit))
print("Failed rows:", len(failed))
for a in failed:
    print(a)
print("Wrote", OUTPUT_PATH)
