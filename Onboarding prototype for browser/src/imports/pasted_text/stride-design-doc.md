# Stride Design Document

> The design system and design philosophy behind the Stride browser: brand intent, tokens, typography, iconography, component specs, motion, and the rules for writing UI code that stays Stride.

**Last updated:** 2026-08-20
**Audience:** contributors touching any UI surface: WPF chrome, internal HTML pages, injected scripts.

---

## 1. Product & brand intent

Stride is a fast, minimalist, privacy-focused browser for Windows. It is a calm workspace that replaces noisy, bloated browsers.

- **Users:** power users, developers, and casual users seeking focus and minimalism.
- **Brand personality:** calm, minimalist, focused.
- **Positioning:** the web is the content; the chrome should be barely there. The less the browser draws attention to itself, the better it is doing its job.

### 1.1 Anti-references

Stride must never look like:

- Generic AI-generated template slop with default gradients and purple-blue SaaS cream.
- Warm-tinted SaaS-cream generic templates.
- Overly saturated color palettes or emoji-as-icons.
- Glassmorphism overload, excessive blur, or shadow chaining as decoration.

When in doubt, **remove it**.

---

## 2. Design principles

1. **Calmness over flash.** Interfaces reduce cognitive load, never add to it. One primary action per surface. Quiet states are the default; emphasis is earned.
2. **Authentic materials.** Real SVGs from a consistent set, precise typography, intentional spacing. No cheap tricks: no emoji icons, no gratuitous blur, no decorative gradients. Typography uses system fonts, so there is zero download and native rendering.
3. **Show, don't tell.** Onboarding is configuring real settings: import data, pick a search engine. Not reading feature cards. Empty states are honest; destructive actions are explicit.
4. **Content first.** The toolbar is 40px because the page deserves the rest of the window. Indeterminate progress is a 1.5px line, not a spinner the size of a thumbnail.
5. **Quiet automation.** Memory management, like hibernation, and ad blocking and HTTPS upgrades all act silently. The user is informed by subtle state, like favicon opacity and a ring around the downloads icon, never by interruptions.

---

## 3. Color tokens

All chrome colors come from exactly two resource dictionaries, `Themes/DarkTheme.xaml` and `Themes/LightTheme.xaml`, exposing the same key names. Never hard-code colors in XAML or C# chrome code; use `DynamicResource` keys. The sanctioned exceptions are listed in §11.5, and they are few.

### 3.1 Dark theme (default)

| Token | Value | Usage |
| --- | --- | --- |
| `BaseColor` / `BaseBrush` | `#111113` | Window content background |
| `SidebarColor` / `SidebarBrush` | `#151517` | Toolbar and tab strip |
| `ContentBrush` | `#151517` | Defined, unused |
| `SurfaceColor` / `SurfaceBrush` | `#1A1A1E` | Cards, popups, dialogs, menus |
| `SurfaceHoverColor` / `SurfaceHoverBrush` | `#252528` | Hover fills, pinned-tab wash |
| `BorderColor` / `BorderBrush` | `#2A2A2E` | Hairlines, outlines |
| `AccentColor` / `Accent` | `#D4A574` | Active tab wash, toggles, highlights, primary buttons |
| `AccentWashColor` / `AccentWash` | `#18D4A574` | Active tab pill, selected suggestion |
| `TextPrimaryColor` / `TextPrimary` | `#E8E4DF` | Body and headings |
| `TextSecondaryColor` / `TextSecondary` | `#8A8680` | Secondary info, icons |
| `TextMutedColor` / `TextMuted` | `#4A4844` | Placeholders, captions, disabled |
| `DangerColor` / `DangerBrush` | `#C4453A` | Close-hover, destructive emphasis |
| `LoadingBarFadeColor` / `LoadingBarGlowColor` | `#00D4A574` / `#D4A574` | Loading comet gradient |

### 3.2 Light theme (warm paper)

| Token | Value |
| --- | --- |
| `BaseColor` / `BaseBrush` | `#F5F5F3` |
| `SidebarColor` / `SidebarBrush` | `#EFEFEE` |
| `SurfaceColor` / `SurfaceBrush` | `#FFFFFF` |
| `SurfaceHoverColor` / `SurfaceHoverBrush` | `#E6E6E5` |
| `BorderColor` / `BorderBrush` | `#D4D4D2` |
| `AccentColor` / `Accent` | `#D4A574`, unchanged. The accent is theme-independent |
| `AccentWashColor` / `AccentWash` | `#20D4A574`, slightly stronger for the light background |
| `TextPrimaryColor` / `TextPrimary` | `#111113` |
| `TextSecondaryColor` / `TextSecondary` | `#666461` |
| `TextMutedColor` / `TextMuted` | `#A5A3A0` |
| `DangerColor` / `DangerBrush` | `#C4453A` |

### 3.3 Semantic color usage

- **Green `#10B981`**: success and progress. Download ring, download status dot, Focus-locked confirmation.
- **Red `#EF4444` and `#E55050`**: failure and destructive confirm buttons. Separate from the brand `DangerBrush`, which is only for hover states.
- **Amber `#F59E0B`**: paused states and starred OneTab entries.
- **Accent is single and constant** across themes. The user picks it in Settings under Appearance, and it is injected into internal pages as `--accent` and `--accent-rgb`.
- **Update badge `#F85149`**: the only element in this color, hard-coded in `MainWindow.xaml`.
- **HTTPS lock `#6B8F71`**: hard-coded stroke in `SecurityBadgeHelper`, outside the token system.
- **Focus-lock and settings** hard-code their own greens and reds. The Settings lock button is `#e53935` and its confirmation is `#4caf50`.

When the user customizes the accent, the app rebuilds `Accent`, `AccentWash`, and both loading-bar tokens in `Application.Current.Resources`. The rebuilt `AccentWash` always uses a `0x1A` alpha, ignoring the theme's `0x18` dark and `0x20` light.

---

## 4. Typography

- **UI font:** `Segoe UI Variable, Segoe UI`. System, invisible, fast, zero download.
- **Internal pages:** system stacks by default, `-apple-system, "Segoe UI", Roboto…`. Two exceptions load display faces from Google Fonts:
  - New Tab page: **Outfit** 300/400/500/600. The page is the personalized canvas, so it earns a display face.
  - History page: **Inter** 300/400/500/600, for dense list legibility.
- **Chrome size scale:** 11 captions, 13 body, 14 brand and command input. Three off-scale sizes exist in the chrome: 12px tab titles, 12px standard address bar text, and 12px tooltips. Dialog titles are 16px semibold with 14px message and input.
- **Weights:** Regular everywhere. `SemiBold` for the Stride brand and dialog titles, `600` for page headings and active nav. The Focus page heading is 700.
- OneTab and Error name Inter in their font stacks but never load it, so they render in Segoe UI.
- Text never exceeds about 75 chars per line in content columns. The Settings container max-width is 720px; History and OneTab full-width lists are exempt.

---

## 5. Iconography

- **Source set:** Lucide-inspired, 24×24 viewBox, stroke-based.
- **Style rules:** `stroke-width` 1 to 1.4. Toolbar glyphs use 1.4, tiny glyphs 1 to 1.2. Round caps and joins everywhere. Only the refresh glyph is filled, plus small decorative marks.
- **Sizes:** 10 to 14px in the toolbar. The settings glyph is 14px, the largest. Page and HTML icons run 14 to 18px. Nothing exceeds 24px inside a button.
- **Consistency:** every chrome icon is a geometry in `App.xaml`, and pages use inline SVGs. No raster icons and no third-party icon fonts in chrome. Favicons are the one sanctioned raster, and they are the site's, not ours.
- **Legacy emoji:** four emoji and Unicode glyphs still ship as icons. The Downloads empty state uses the folder glyph, the Error page uses a warning triangle, OneTab marks starred entries with Unicode stars, and the dark-mode banner uses a moon. New work must not add more.
- **Color:** icons inherit `TextSecondary` by default, brighten on hover, and take `Accent` when selected or active.

---

## 6. Spacing & geometry

| Token | Value | Used for |
| --- | --- | --- |
| Micro | 2-4px | Icon padding, pill gaps, hairlines |
| Tight | 8px | Button padding, card gutters, icon gaps |
| Base | 12-16px | Row padding, dialog padding, list gutters |
| Loose | 20-24px | Page gutters, dialog outer margins. 40px in the Settings column and Downloads page |
| Section | 28-40px | Between sections and cards, page headers |

**Radii:** 3px scrollbar thumb and inactive tab close, 4px buttons and chips, 6px inputs, history entries, and sidebar nav, 8px tabs, cards, dialogs, popups, command bar, context menus, and download rows, 10px settings cards, 16px new-tab search field and shortcut tiles, 12px History confirm dialog. Dialog buttons and window controls are square.

**Hit targets:** icon buttons 26×26 in the toolbar and 30×30 by style default, window controls 42×40, menu and dialog rows at least 34px, toggles 36×20. The tab close buttons are 12×12 inactive and 14×14 active. They are the one place that drops below the 26px floor on purpose.

---

## 7. Elevation

Real shadows, restrained:

- Menus: blur 12px, depth 4px, 25% black.
- Dialogs: blur 24px, depth 8px, 50% black.
- Command bar: blur 40px, depth 8px, 60% black.
- New Tab tiles: hover lifts 4px with a `0 12px 24px` shadow at 30% black.
- No persistent drop shadows on static UI.

---

## 8. Component specs

### 8.1 Buttons

- **Icon button:** transparent, 4px radius, 30×30 by style with 26×26 toolbar instances, hover `#0AFFFFFF`, pressed `#14FFFFFF`.
- **Window controls:** square, 42×40, no radius. Close hover is a `DangerBrush` fill.
- **Primary:** accent fill with dark `#111113` text. Radius 8px on pages, square in dialogs because dialog buttons use the default WPF template.
- **Ghost/action:** transparent with a 1px accent outline, radius 8px. Hover inverts to accent fill. Lives on the Settings page.
- **Danger:** red fills `#EF4444` for destructive confirms only. The Settings lock button uses `#e53935`.

### 8.2 Favicon pill tabs

The pill is the signature element. The `FaviconTabListStyle` in `App.xaml` owns the state visuals; the item template in `MainWindow.xaml` renders them. The invariant: inactive is icon only, active is a pill with title, pinned is icon plus a persistent wash. Max title 160px, 12px type, `CharacterEllipsis`.

| State | Pill background | Favicon opacity | Title | Close |
| --- | --- | --- | --- | --- |
| Inactive | transparent | 0.4 | hidden unless `ShowTabNames` | 12×12 overlay on the favicon, fades in on tab hover |
| Active | `AccentWash`, radius 8 | 1.0 | visible, max 160px | 14×14 at the pill end, always visible |
| Pinned | `SurfaceHoverBrush` | 0.4 | hidden | hidden |
| Hibernated | per state | 0.15 | per state | per state |
| Hovered | `#0DFFFFFF` | 1.0 | per state | per state |

Opacity animates 0.7 to 1.0 over 100ms on hover and over 200ms on selection. Pinned tabs never show a title or close button. When `Settings.ShowTabNames` is on, every tab shows its title.

### 8.3 Command bar

540px × auto, radius 8, `#2C2C32` surface, `#444449` border, 40px blur shadow, 50% black backdrop, 80ms open/close, 10px vertical slide.

### 8.4 Suggestion rows

3-column grid: 3px accent bar, 10px clock icon, text. The accent bar sits at opacity 0 until selected. Hover `#0EFFFFFF`; selected is `AccentWash` with the accent bar and an accent icon. 4px radius, padding `10,7,12,7`.

### 8.5 Context menus & tooltips

- Menu: surface fill, 1px border, radius 8, padding 4, shadow 12px/25%; items radius 4, padding `8,6`, gesture hints right-aligned in `TextSecondary`; separators are 1px `BorderBrush` with `4,4` margins.
- Tooltip: radius 4, padding `8,5`, 12px.

### 8.6 Scrollbars

6px, transparent track, pill thumb in `#26FFFFFF`, hover `#40FFFFFF`, drag `#59FFFFFF`. HTML pages: 6px, `--border` thumb.

### 8.7 Toggles

36×20 pill, `--border` off, accent on, 14px knob, 200ms slide. Always paired with a label and an optional description.

### 8.8 Loading bar

1.5px, transparent track, 100px accent comet sweeping a 1.2s loop with a sine ease-in-out, pinned to content top. See `LoadingAnimationController`.

### 8.9 Dialogs

450px, radius 8, surface fill, 24px shadow at 50% black. Title 16px semibold, message 14px muted in a scroll area capped at 80px, optional input at 14px. Cancel and OK sit right-aligned. OK is accent fill with dark text and is the default button. Both buttons are square.

### 8.10 Internal page CSS variables

Each page carries an inline `<style>` block defining ten tokens for dark, light, and system themes, plus `--accent` and `--accent-rgb`. System follows `prefers-color-scheme`. The block is static HTML, not injected by C#. The `{{THEME}}` substitution sets `data-theme` on the `<html>` element, and the app live-updates it on theme change.

| Token | Dark | Light |
| --- | --- | --- |
| `--bg-base` | `#121212` | `#F5F5F3` |
| `--bg-dark` | `#0C0C0C` | `#EFEFEE` |
| `--bg-surface` | `#1A1A1A` | `#FFFFFF` |
| `--bg-hover` | `#242424` | `#E6E6E5` |
| `--border` | `#333333` | `#D4D4D2` |
| `--text-main` | `#E0E0E0` | `#111113` |
| `--text-bright` | `#D4D4D4` | `#000000` |
| `--text-muted` | `#A3A3A3` | `#666461` |
| `--text-dim` | `#808080` | `#A5A3A0` |
| `--white` | `#FFFFFF` | `#000000` |

The page tokens are a separate palette from the chrome tokens in §3; the dark page values deliberately differ from the dark chrome hexes.

Known defect: every page except Downloads defines the dark `--border` as a cyclic `var(--border)333`, so borders silently vanish in dark mode. Five pages also carry a dead duplicate token block whose values reference themselves. Fix both when touching a page's token block.

---

## 9. Motion

| Duration | Easing | Use |
| --- | --- | --- |
| 80ms | cubic ease-out | Command bar slide and fade, tab close reveal |
| 100ms | quadratic ease-out | Tab hover opacity |
| 200ms | quadratic ease-in-out | Tab selection, page dialogs |
| 250ms | none | New Tab tile hover |
| 300ms | none | New Tab search focus |
| 400ms | quadratic ease-in-out | Toolbar tint |
| 600ms | none | New Tab entrance |
| 900ms | linear | Security badge spin |
| 1s | none | Focus page fade-in |
| 1.2s loop | sine ease-in-out | Loading sweep |
| 1.5s loop | none | Recording pulse |

Rules:

- Hover reveals fade; selection expands; arrival slides up.
- Never animate layout-affecting properties on the main thread beyond 400ms.
- `prefers-reduced-motion` collapses durations to near zero on HTML pages. Only New Tab and the Onboarding prototype honor it today.

---

## 10. Theming rules

1. Dark is default; Light is warm paper; System follows `prefers-color-scheme`. The app accent is user-chosen, stored in settings, injected into pages.
2. Chrome must use `DynamicResource` so runtime theme swap works. Pages use the CSS variable block from §8.10.
3. The **toolbar tint** is the only chrome element that adapts to content. `ToolbarTintAdapter` animates it over 400ms with a quadratic ease. It rejects near-white and strong-green backgrounds by falling back to `#111111`, and swaps the local text tokens when the computed luminance passes 0.5. Never extend content adaptation beyond the toolbar.
4. `ThemeManager` swaps the active theme at runtime by replacing the first merged dictionary in `App.xaml`, and pushes the theme to web views as `data-theme`. Do not introduce a third theme file; extend the token pairs.

---

## 11. Rules of the road for writing UI code

1. **New chrome controls:** XAML with `DynamicResource` tokens and shared styles from `App.xaml`. Reuse `IconBtn`, `TitleBtn`, `CloseTitleBtn`, `CommandBarInputStyle`, `FaviconTabListStyle`, and the global `ContextMenu`, `MenuItem`, `Separator`, `ToolTip`, and `ScrollBar` styles. `TabCloseBtn`, `SidebarActionBtn`, `AddressBarStyle`, and `NewTabBtn` still exist but nothing uses them today. Don't inline new chrome styles in windows unless truly local. The suggestion rows are currently duplicated between `Window.Resources` and the command bar; one shared style is the target.
2. **New internal pages:** HTML under `Resources/Pages/`. Only History and Downloads render through `TemplateRenderer`. New Tab, Settings, OneTab, and Error load templates directly with `ResourceLoader.LoadTemplate`, and the Focus page is loaded raw with no theme substitution. All interactive pages post IPC over `window.chrome.webview.postMessage` with the `{{IPC_TOKEN}}` prefix. Themed via the CSS variable block, accent via `--accent` and `--accent-rgb`.
3. **No emoji as UI icons in new work.** The Downloads folder, Error warning triangle, OneTab stars, and dark-mode banner moon are legacy. Replace them with SVGs over time.
4. **Every clickable thing** gets `Cursor="Hand"` and a tooltip naming its shortcut where one exists. The window control buttons currently have no tooltips.
5. **Never hard-code hex in XAML or C# chrome.** Sanctioned exceptions: transient hover washes `#0EFFFFFF` and `#14FFFFFF`, toolbar-tint overrides, page-local styles, and the documented legacy colors in §3.3 and §5.
6. **Keep state out of the visual layer.** Controllers live in `Services/UI/`; windows stay thin. A UI change that requires a C# rewrite of a controller method is a red flag.
7. **Dark-first authoring:** write for the dark theme, then verify the light theme. Watch muted-text contrast, accent wash strength, and shadow visibility. History, Error, and Focus still hard-code dark colors and break in light mode.
8. **Add the setting, then the control:** new toggles go into `Settings.html` with a description that names the trade-off, like requires restart or a privacy trade-off, where true.

---

## 12. Accessibility requirements

- Text tokens must hold WCAG AA on their surface, per the token pairs in §3. `TextMuted` and `--text-dim` are for captions, placeholders, and metadata, never primary content.
- All interactive elements keyboard-reachable. Pages use `focus-visible` rings. Chrome removes `FocusVisualStyle` on every button and list item; tabs, the command bar, and dialogs are supposed to get keyboard-focus styles instead, and today they do not.
- `prefers-reduced-motion` respected on pages. Only New Tab and the Onboarding prototype implement it today.
- Hit targets at least 26px, except the tab close buttons. Titles ellipsized. Favicons render with `BitmapScalingMode=HighQuality`.
- Destructive actions always confirm; permanent actions like Focus lock double-confirm.

---

## 13. Design review checklist

Before submitting UI work, verify:

- [ ] Uses tokens, not literals. Sanctioned exceptions only.
- [ ] Icons from the stroke set. No new emoji. Consistent stroke weight.
- [ ] Type from the chrome scale in §4 or the page's stack.
- [ ] Motion inside the duration and easing table. Reduced motion handled.
- [ ] Works in dark and light. Toolbar tint sanitizers respected.
- [ ] Empty state exists. Destructive actions confirm. Tooltips carry shortcuts.
- [ ] Hit targets and focus behavior pass §12.
- [ ] No new chrome feature is visible and noisy at once. If it can be quieter, make it quieter.
