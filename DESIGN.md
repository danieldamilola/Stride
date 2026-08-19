# Stride — Design Document

> The design system and design philosophy behind the Stride browser: brand intent, tokens, typography, iconography, component specs, motion, and the rules for writing UI code that stays Stride.

**Last updated:** 2026-08-18
**Audience:** contributors touching any UI surface (WPF chrome, internal HTML pages, injected scripts).

---

## 1. Product & brand intent

Stride is a **fast, minimalist, privacy-focused browser for Windows** — a calm workspace that replaces noisy, bloated browsers (see `PRODUCT.md`).

- **Users:** power users, developers, and casual users seeking focus and minimalism.
- **Brand personality:** calm, minimalist, focused.
- **Positioning:** the web is the content; the chrome should be barely there. The less the browser draws attention to itself, the better it is doing its job.

### 1.1 Anti-references

Stride must never look like:
- Generic AI-generated template slop (default gradients, purple/blue SaaS cream).
- Warm-tinted "SaaS-cream" generic templates.
- Overly saturated color palettes or emoji-as-icons.
- Glassmorphism overload, excessive blur, or shadow chaining as decoration.

When in doubt, **remove it**.

---

## 2. Design principles

1. **Calmness over flash.** Interfaces reduce cognitive load, never add to it. One primary action per surface. Quiet states are the default; emphasis is earned.
2. **Authentic materials.** Real SVGs from a consistent set, precise typography, intentional spacing. No cheap tricks — no emoji icons, no gratuitous blur, no decorative gradients. Typography uses system fonts (zero download, native rendering).
3. **Show, don't tell.** Onboarding is *configuring real settings* (import data, pick a search engine), not reading feature cards. Empty states are honest; destructive actions are explicit.
4. **Content first.** The toolbar is 40px because the page deserves the rest of the window. Indeterminate progress is a 1.5px line, not a spinner the size of a thumbnail.
5. **Quiet automation.** Memory management (hibernation), ad blocking, and HTTPS upgrades all act silently. The user is informed by *subtle state* (favicon opacity, a ring around the downloads icon), never by interruptions.

---

## 3. Color tokens

All chrome colors come from exactly two resource dictionaries — `Themes/DarkTheme.xaml` and `Themes/LightTheme.xaml` — exposing the same key names. **Never hard-code colors in XAML or C# chrome code; use `DynamicResource` keys.** (Toolbar-adaptive overrides in `ToolbarTintAdapter` are the one sanctioned exception, and they fall back to the global theme.)

### 3.1 Dark theme (default)

| Token | Value | Usage |
|---|---|---|
| `BaseColor` / `BaseBrush` | `#111113` | Window content background |
| `SidebarColor` / `SidebarBrush` | `#151517` | Toolbar/tab strip, content brush |
| `SurfaceColor` / `SurfaceBrush` | `#1A1A1E` | Cards, popups, dialogs, menus |
| `SurfaceHoverColor` / `SurfaceHoverBrush` | `#252528` | Hover fills, pinned-tab wash |
| `BorderColor` / `BorderBrush` | `#2A2A2E` | Hairlines, outlines |
| `AccentColor` / `Accent` | `#D4A574` | Active tab wash, toggles, highlights, primary buttons |
| `AccentWash` | `#18D4A574` (10% accent) | Active tab pill, selected suggestion |
| `TextPrimary` | `#E8E4DF` | Body/headings |
| `TextSecondary` | `#8A8680` | Secondary info, icons |
| `TextMuted` | `#4A4844` | Placeholders, captions, disabled |
| `Danger` | `#C4453A` | Close-hover, destructive emphasis |
| `LoadingBarFadeColor` / `LoadingBarGlowColor` | `#00D4A574` / `#D4A574` | Loading comet gradient |

### 3.2 Light theme (warm paper)

| Token | Value |
|---|---|
| `Base` | `#F5F5F3` |
| `Sidebar` | `#EFEFEE` |
| `Surface` | `#FFFFFF` |
| `SurfaceHover` | `#E6E6E5` |
| `Border` | `#D4D4D2` |
| `Accent` | `#D4A574` (unchanged — the accent is theme-independent) |
| `AccentWash` | `#20D4A574` (slightly stronger for the light background) |
| `TextPrimary` | `#111113` |
| `TextSecondary` | `#666461` |
| `TextMuted` | `#A5A3A0` |
| `Danger` | `#C4453A` |

### 3.3 Semantic color usage

- **Green `#10B981`** — success/progress only (download ring, download status dot, Focus-locked confirmation).
- **Red `#EF4444` / `#E55050`** — failure, destructive confirm buttons (separate from brand `Danger` used for hover states).
- **Amber `#F59E0B`** — paused states, starred OneTab entries.
- **Accent is single and constant** across themes; the app accent is user-customizable (Settings → Appearance → Accent color) and is injected into internal pages as `--accent` / `--accent-rgb`.

---

## 4. Typography

- **UI font:** `Segoe UI Variable, Segoe UI` — system, invisible, fast, zero download.
- **Internal pages:** system stacks by default (`-apple-system, "Segoe UI", Roboto…`). Two deliberate exceptions:
  - New Tab page: **Outfit** (Google Fonts) — the page *is* the personalized canvas, so it earns a display face.
  - History page: **Inter** (Google Fonts) — for dense list legibility.
- **Three sizes in chrome** (per App.xaml): **11** captions · **13** body · **14** brand/command input. HTML pages scale slightly richer (12–16px) since they carry more information.
- **Weights:** Regular everywhere. `SemiBold` only for the "Stride" brand and dialog titles; `600` for page headings and active nav.
- HTML pages may use 300–600 weights of their display font for hierarchy (Outfit 300/400/500/600).
- Text never exceeds ~75 chars per line in content columns (Settings container max-width 720px, History/OneTab full-width lists are exempt).

---

## 5. Iconography

- **Source set:** Lucide-inspired, **24×24 viewBox**, stroke-based.
- **Style rules:** `stroke-width` 1–1.4 (toolbar 1.4, tiny glyphs 1–1.2), round caps and joins (`StrokeStartLineCap/EndLineCap/LineJoin="Round"`), never filled except refresh and small decorative marks.
- **Sizes:** 10–12px for toolbar glyphs; 14–18px for page/HTML icons; 24px max inside buttons.
- **Consistency:** every icon is a geometry in `App.xaml` (or an inline SVG in pages) — **no emoji, no raster icons, no third-party icon fonts** in chrome. (Favicons are the one sanctioned raster content, and they're the site's, not ours.)
- **Color:** icons inherit `TextSecondary` by default, brighten on hover, and take `Accent` when selected/active.

---

## 6. Spacing & geometry

| Token | Value | Used for |
|---|---|---|
| Micro | 2–4px | Icon padding, pill gaps, hairlines |
| Tight | 8px | Button padding, card gutters, icon gaps |
| Base | 12–16px | Row padding, dialog padding, list gutters |
| Loose | 20–24px | Page gutters (40px in Settings/Downloads columns), dialog outer margins |
| Section | 28–40px | Between sections/cards, page headers |

**Radii:** 4px (buttons, chips) · 6px (inputs, menu items, nav rows) · 8px (cards, dialogs, popups, command bar, tabs 8px) · 10px (settings cards) · 12–16px (download rows, new-tab tiles, search field). Windows chrome is square.

**Hit targets:** icon buttons ≥ 26×26 (window controls 42×40); menu/dialog rows ≥ 34px; toggle 36×20.

---

## 7. Elevation

Real shadows, restrained:
- Popups/menus/command bar: `DropShadowEffect` blur 12–40px, opacity 0.25–0.6, depth 4–8px.
- Dialogs: blur 24px, 50% black.
- New Tab tiles: lift −4px with 24px/30% shadow on hover.
- No persistent drop shadows on static UI.

---

## 8. Component specs

### 8.1 Buttons
- **Icon button** — transparent, 4px radius, 30×30, hover `#0AFFFFFF`, pressed `#14FFFFFF`.
- **Window controls** — square, 42×40, no radius; close hover = `Danger` fill.
- **Primary** (dialogs, pages) — accent fill, dark `#111113` text, radius 8px.
- **Ghost/action** (Settings) — transparent, 1px accent outline; hover inverts to accent fill.
- **Danger** — red fills (`#EF4444`) for destructive confirms only.

### 8.2 Favicon pill tabs
See `UI_UX.md` §2.1 for the full state table. The invariant: **inactive = icon only; active = pill with title; pinned = icon + wash, always.** Max title 160px, 12px type, `CharacterEllipsis`.

### 8.3 Command bar
540px × auto, radius 8, `#2C2C32` surface, `#444449` border, 40px blur shadow, 50% black backdrop, 80ms open/close, 10px vertical slide.

### 8.4 Suggestion rows
3-column grid: 3px accent bar (opacity 0) · 10px clock icon · text. Hover `#0EFFFFFF`; selected = `AccentWash` + accent bar + accent icon. 4px radius, padding `10,7,12,7`.

### 8.5 Context menus & tooltips
- Menu: surface fill, 1px border, radius 8, padding 4, shadow 12px/25%; items radius 4, padding `8,6`, gesture hints right-aligned in `TextSecondary`; separators are 1px `BorderBrush` with `4,4` margins.
- Tooltip: radius 4, padding `8,5`, 12px.

### 8.6 Scrollbars
6px, transparent track, pill thumb (`#26FFFFFF`, hover `#40FFFFFF`, drag `#59FFFFFF`). HTML pages: 6px, `--border` thumb.

### 8.7 Toggles
36×20 pill, `--border` off / accent on, 14px knob, 200ms slide. Always paired with a label + optional description.

### 8.8 Loading bar
1.5px, transparent track, 100px accent comet sweeping 1.2s loop (sine ease-in-out), pinned to content top. See `LoadingAnimationController`.

### 8.9 Dialogs
450px, radius 8, surface fill, 24px shadow, title 16px semibold, message 14px muted (max 80px scroll), optional input, Cancel (ghost) + OK (accent) right-aligned, OK is default.

### 8.10 Internal page CSS variables
Pages receive a theme token block defining: `--bg-base`, `--bg-dark`, `--bg-surface`, `--bg-hover`, `--border`, `--text-main`, `--text-bright`, `--text-muted`, `--text-dim`, `--white` for `dark`, `light`, and `system` (via `prefers-color-scheme`), plus `--accent` and `--accent-rgb` injected from settings.

---

## 9. Motion

| Duration | Easing | Use |
|---|---|---|
| 80ms | cubic ease-out | Command bar, hover reveals, fades |
| 100–200ms | quadratic ease-in-out | Tab hover/selection, toggles, buttons |
| 300–400ms | `cubic-bezier(0.2, 0.8, 0.2, 1)` | Focus states, toolbar tint, progress |
| 600ms | same bezier | New Tab entrance |
| 1.2s loop | sine ease-in-out | Loading sweep |

Rules:
- Hover reveals **fade**; selection **expands**; arrival **slides up**.
- Never animate layout-affecting properties on the main thread beyond 400ms.
- `prefers-reduced-motion` → collapse durations to ~0 on HTML pages; keep instant state swaps in WPF.

---

## 10. Theming rules

1. Dark is default; Light is warm paper; System follows `prefers-color-scheme`. The app accent is user-chosen, stored in settings, injected into pages.
2. Chrome must use `DynamicResource` (so runtime theme swap works); pages use the CSS variable block from §8.10.
3. The **toolbar tint** is the only chrome element that adapts to content — 400ms animation, near-white and strong-green rejection, luminance-driven contrast swap (§12 of `UI_UX.md`). Never extend content-adaptation beyond the toolbar.
4. The active theme is applied at startup by merging the theme dictionary in `App.xaml`; do not introduce a third theme file — extend the token pairs.

---

## 11. Writing UI code — rules of the road

1. **New chrome controls:** XAML + `DynamicResource` tokens + shared styles from `App.xaml`. Reuse `IconBtn`, `TitleBtn`, `CloseTitleBtn`, `TabCloseBtn`, `SidebarActionBtn`, `AddressBarStyle`, `CommandBarInputStyle`, `FaviconTabListStyle`, and the global `ContextMenu`/`MenuItem`/`Separator`/`ToolTip`/`ScrollBar` styles. Don't inline new chrome styles in windows unless truly local.
2. **New internal pages:** HTML under `Resources/Pages/`, token-templated via `TemplateRenderer`, IPC over `window.chrome.webview.postMessage` with the `{{IPC_TOKEN}}` prefix. Themed via the CSS variable block; accent via `--accent`/`--accent-rgb`.
3. **No emoji as UI icons** anywhere (the Downloads empty-state 📂 glyph is legacy — new work must use SVG).
4. **Every clickable thing** gets `Cursor="Hand"` + a tooltip stating its shortcut where one exists.
5. **Never hard-code hex in XAML/C# chrome.** Exceptions: transient hover washes (`#0EFFFFFF`, `#14FFFFFF`), toolbar-tint overrides, and page-local styles.
6. **Keep state out of the visual layer.** Controllers live in `Services/UI/`; windows stay thin. A UI change that requires a C# rewrite of a controller method is a red flag.
7. **Dark-first authoring:** write for the dark theme, then verify the light theme (contrast of muted text, accent wash strength, shadow visibility).
8. **Add the setting, then the control:** new toggles go into `Settings.html` with a description that names the trade-off ("requires restart", "privacy trade-off") where true.

---

## 12. Accessibility requirements

- Text tokens must hold WCAG AA on their surface (see token pairs §3). `TextMuted`/`TextDim` only for captions, placeholders, and metadata — never primary content.
- All interactive elements keyboard-reachable; `focus-visible` rings on pages, keyboard-focus styles instead of removed `FocusVisualStyle` where keyboard is the primary path (tabs, command bar, dialogs).
- `prefers-reduced-motion` respected.
- Hit targets ≥ 26px; titles ellipsized; high-DPI rendering (`BitmapScalingMode=HighQuality` for favicons).
- Destructive actions always confirm; permanent actions (Focus lock) double-confirm.

---

## 13. Design review checklist

Before submitting UI work, verify:

- [ ] Uses tokens, not literals (except sanctioned exceptions).
- [ ] Icons from the stroke set; no emoji; consistent stroke weight.
- [ ] Type from the 11/13/14 scale (chrome) or the page's stack.
- [ ] Motion within the duration/easing table; reduced-motion handled.
- [ ] Works in dark **and** light; toolbar tint sanitizers respected.
- [ ] Empty state exists; destructive actions confirm; tooltips carry shortcuts.
- [ ] Hit targets and focus behavior pass §12.
- [ ] No new chrome feature is visible *and* noise: if it can be quieter, make it quieter.