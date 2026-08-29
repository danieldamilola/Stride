# Stride Design Document

> The design system behind Stride: brand intent, tokens, typography, iconography, components, motion, and the rules for writing UI code that stays Stride.

**Last updated:** 2026-08-29
**Audience:** contributors touching WPF chrome, internal HTML pages, injected scripts.

---

## 1. Product and brand intent

Stride is a fast, minimalist, privacy focused browser for Windows. It is a calm workspace that replaces noisy, bloated browsers.

- **Users:** power users, developers, and casual users who want focus and minimalism.
- **Personality:** calm, minimalist, focused.
- **Positioning:** the web is the content. The chrome should be barely there. The less the browser draws attention to itself, the better it does its job.

### 1.1 Anti references

Stride must never look like:

- Generic AI template slop with default gradients and purple blue SaaS cream
- Warm tinted SaaS cream generic templates
- Over saturated palettes or emoji as icons
- Glassmorphism overload, excessive blur, or shadow chaining as decoration

When in doubt, remove it.

---

## 2. Design principles

1. **Calmness over flash.** Interfaces reduce load, never add to it. One primary action per surface. Quiet is the default. Emphasis is earned.
2. **Authentic materials.** Real SVGs from one set, precise type, intentional spacing. No cheap tricks. No emoji icons, no gratuitous blur, no decorative gradients. System fonts mean zero download and native rendering.
3. **Show, do not tell.** Onboarding configures real settings. Import data, pick a search engine. Empty states are honest. Destructive actions are explicit.
4. **Content first.** The toolbar is 40px because the page deserves the rest of the window. Indeterminate progress is a 1.5px line, not a big spinner.
5. **Quiet automation.** Hibernation, ad blocking, and HTTPS upgrades act silently. The user sees subtle state, like favicon opacity and a ring around the downloads icon. Never an interruption.

---

## 3. Color tokens

All chrome colors come from two dictionaries, `Themes/DarkTheme.xaml` and `Themes/LightTheme.xaml`. Same keys, different values. Use `DynamicResource` keys in XAML and C# chrome code. Sanctioned exceptions are listed in section 11.5.

### 3.1 Dark theme, default

| Token | Value | Usage |
|---|---|---|
| `BaseColor` / `BaseBrush` | #111113 | Window background |
| `SidebarColor` / `SidebarBrush` | #151517 | Toolbar and tab strip |
| `ContentBrush` | #151517 | Defined, unused |
| `SurfaceColor` / `SurfaceBrush` | #1A1A1E | Cards, popups, dialogs, menus |
| `SurfaceHoverColor` / `SurfaceHoverBrush` | #252528 | Hover fills, pinned tab wash |
| `BorderColor` / `BorderBrush` | #2A2A2E | Hairlines, outlines |
| `AccentColor` / `Accent` | #7fb89a | Active tab wash, toggles, highlights, primary buttons |
| `AccentWashColor` / `AccentWash` | #187fb89a | Active tab pill, selected suggestion |
| `TextPrimaryColor` / `TextPrimary` | #E8E4DF | Body and headings |
| `TextSecondaryColor` / `TextSecondary` | #8A8680 | Secondary info, icons |
| `TextMutedColor` / `TextMuted` | #4A4844 | Placeholders, captions, disabled |
| `DangerColor` / `DangerBrush` | #C4453A | Close hover, destructive emphasis |
| `LoadingBarFadeColor` / `LoadingBarGlowColor` | #007fb89a / #7fb89a | Loading comet gradient |

### 3.2 Light theme, warm paper

| Token | Value |
|---|---|
| `BaseColor` / `BaseBrush` | #F5F5F3 |
| `SidebarColor` / `SidebarBrush` | #EFEFEE |
| `SurfaceColor` / `SurfaceBrush` | #FFFFFF |
| `SurfaceHoverColor` / `SurfaceHoverBrush` | #E6E6E5 |
| `BorderColor` / `BorderBrush` | #D4D4D2 |
| `AccentColor` / `Accent` | #7fb89a, unchanged, theme independent |
| `AccentWashColor` / `AccentWash` | #207fb89a, slightly stronger for light |
| `TextPrimaryColor` / `TextPrimary` | #111113 |
| `TextSecondaryColor` / `TextSecondary` | #666461 |
| `TextMutedColor` / `TextMuted` | #A5A3A0 |
| `DangerColor` / `DangerBrush` | #C4453A |

### 3.3 Semantic colors

- **Green #10B981:** success and progress. Download ring, download status dot, Focus locked confirmation.
- **Red #EF4444 and #E55050:** failure and destructive confirms. Separate from `DangerBrush`, which is only for hover.
- **Amber #F59E0B:** paused states and starred OneTab entries.
- **Accent:** single and constant across themes. The user picks it in Settings under Appearance. It is injected into pages as `--accent` and `--accent-rgb`.
- **Update badge #F85149:** one element only, hard coded in `MainWindow.xaml`.
- **HTTPS lock #6B8F71:** hard coded stroke in `SecurityBadgeHelper`, outside the token system.
- **Settings lock:** #e53935 for the lock button, #4caf50 for its confirmation. Both hard coded.

When the user picks a custom accent, the app rebuilds `Accent`, `AccentWash`, and both loading bar tokens in `Application.Current.Resources`. The rebuilt `AccentWash` always uses `0x1A` alpha, ignoring the theme values `0x18` dark and `0x20` light.

---

## 4. Typography

- **UI font:** `Segoe UI Variable, Segoe UI`. System, invisible, fast, zero download.
- **Internal pages:** system stack by default, `-apple-system, "Segoe UI", Roboto` and fallbacks. Two pages load a display face from Google Fonts:
  - New Tab: **Outfit** 300, 400, 500, 600. The page is the personalized canvas, so it earns a display face.
  - History: **Inter** 300, 400, 500, 600, for dense list legibility.
- **Chrome scale:** 11 captions, 13 body, 14 brand and command input. Three off scale sizes exist: 12px tab titles, 12px standard address bar, 12px tooltips. Dialogs use 16px semibold title with 14px message and input.
- **Weights:** Regular everywhere. `SemiBold` for the Stride wordmark and dialog titles. `600` for page headings and active nav. Focus page heading is `700`.
- **Stack gap:** OneTab and Error name Inter in their font stack but never load it. They render in Segoe UI.
- **Measure:** Text stays under about 75 characters per line in content columns. Settings content max width is 720px. History and OneTab full width lists are exempt.

---

## 5. Iconography

- **Source:** Lucide inspired, 24 by 24 viewBox, stroke based.
- **Stroke:** width 1 to 1.4. Toolbar glyphs use 1.4. Tiny glyphs use 1 to 1.2. Caps and joins are round everywhere. Only the refresh glyph is filled.
- **Sizes:** 10 to 14px in the toolbar. Settings gear is 14px, the largest. Page icons run 14 to 18px. Nothing exceeds 24px inside a button.
- **Consistency:** every chrome icon is a geometry in `App.xaml`. Pages use inline SVGs. No raster icons. No third party icon fonts. Favicons are the one sanctioned raster and they belong to the site, not to Stride.
- **Legacy emoji:** three glyphs still ship. Downloads empty state uses a folder, Error uses a warning triangle, OneTab uses Unicode stars. New code must use SVG.

---

## 6. Spacing and geometry

| Token | Value | Used for |
|---|---|---|
| Micro | 2 to 4px | Icon padding, pill gaps, hairlines |
| Tight | 8px | Button padding, card gutters, icon gaps |
| Base | 12 to 16px | Row padding, dialog padding, list gutters |
| Loose | 20 to 24px | Page gutters, dialog outer margins. 40px in Settings column and Downloads page |
| Section | 28 to 40px | Between sections and cards, page headers |

**Radii:** 3px scrollbar thumb and inactive tab close, 4px buttons and chips, 6px inputs and history entries and sidebar nav, 8px tabs and cards and dialogs and popups and command bar and context menus and download rows, 10px settings cards, 16px New Tab search field and shortcut tiles, 12px History confirm dialog. Dialog buttons and window controls are square.

**Hit targets:** icon buttons 26 by 26 in the toolbar and 30 by 30 by style default, window controls 42 by 40, menu and dialog rows at least 34px, toggles 36 by 20. Tab close buttons are the exception at 12 by 12 inactive and 14 by 14 active.

---

## 7. Elevation

Real shadows, restrained.

- Menus: blur 12px, depth 4px, 25% black
- Dialogs: blur 24px, depth 8px, 50% black
- Command bar: blur 40px, depth 8px, 60% black
- New Tab tiles: hover lifts 4px with a `0 12px 24px` shadow at 30% black
- No persistent shadows on static UI

---

## 8. Component specs

### 8.1 Buttons

- **Icon button:** transparent, 4px radius, 30 by 30 by style with 26 by 26 toolbar instances. Hover #0AFFFFFF, pressed #14FFFFFF.
- **Window controls:** square, 42 by 40, no radius. Close hover is `DangerBrush`.
- **Primary:** accent fill with dark #111113 text. Radius 8px on pages, square in dialogs because dialogs use the default WPF template.
- **Ghost and action:** transparent with 1px accent outline, radius 8px. Hover inverts to accent fill. Lives on Settings.
- **Danger:** red #EF4444 for destructive confirms only. Settings lock uses #e53935.

### 8.2 Favicon pill tabs

The pill is the signature element. `FaviconTabListStyle` in `App.xaml` owns the states. The item template in `MainWindow.xaml` renders them. Inactive is icon only. Active is a pill with title. Pinned is icon plus a persistent wash. Max title 160px, 12px, `CharacterEllipsis`.

| State | Pill background | Favicon opacity | Title | Close |
|---|---|---|---|---|
| Inactive | transparent | 0.4 | hidden unless `ShowTabNames` is on | 12 by 12 overlay on favicon, fades in on tab hover |
| Active | `AccentWash`, radius 8 | 1.0 | visible, max 160px | 14 by 14 at pill end, always visible |
| Pinned | `SurfaceHoverBrush` | 0.4 | hidden | hidden |
| Hibernated | per state | 0.15 | per state | per state |
| Hovered | #0DFFFFFF | 1.0 | per state | per state |

Opacity animates 0.7 to 1.0 over 100ms on hover and over 200ms on selection. Pinned tabs never show a title or close button. When `Settings.ShowTabNames` is on, every tab shows its title.

### 8.3 Command bar

540px by auto, radius 8, #2C2C32 surface, #444449 border, 40px blur shadow, 50% black backdrop, 80ms open and close, 10px vertical slide. Input style is `CommandBarInputStyle` at 14px with radius 6 and padding 12,10.

### 8.4 Suggestion rows

Three column grid: 3px accent bar, 10px clock icon, text. The accent bar sits at opacity 0 until selected. Hover #0EFFFFFF. Selected is `AccentWash` with the accent bar and an accent icon. Radius 4px, padding `10,7,12,7`.

### 8.5 Context menus and tooltips

- Menu: surface fill, 1px border, radius 8, padding 4, shadow blur 12 depth 4 at 25%. Standard items radius 4, padding `8,6`, gesture hints right aligned in `TextSecondary`, separators 1px `BorderBrush` with `4,4` margins.
- Navigation row: the top row contains Back, Forward, and Reload buttons. It spans the full menu width using `ContextMenuNavRowTemplate` to bypass the standard 3-column WPF grid.
- Tooltip: radius 4, padding `8,5`, 12px type.

### 8.6 Scrollbars

6px, transparent track, pill thumb. Thumb #26FFFFFF, hover #40FFFFFF, drag #59FFFFFF. HTML pages use 6px with `--border` thumb.

### 8.7 Toggles

36 by 20 pill, `--border` when off, accent when on, 14px knob, 200ms slide. Always paired with a label and optional description.

### 8.8 Loading bar

1.5px, transparent track, 100px accent comet sweeping a 1.2s loop with sine ease in out, pinned to content top. See `LoadingAnimationController`.

### 8.9 Dialogs

450px, radius 8, surface fill, 24px shadow at 50% black. Title 16px semibold, message 14px muted in a scroll area capped at 80px, optional input at 14px. Cancel and OK sit right aligned. OK is accent fill with dark text and is the default button. Both buttons are square with padding 16,8. Input is focused and selected on open.

### 8.10 Internal page CSS variables

Each page carries an inline `style` block that defines ten tokens for dark, light, and system themes, plus `--accent` and `--accent-rgb`. System follows `prefers-color-scheme`. The block is static HTML, not injected by C#. The `{{THEME}}` substitution sets `data-theme` on the `html` element and the app live updates it on theme change.

| Token | Dark | Light |
|---|---|---|
| `--bg-base` | #121212 | #F5F5F3 |
| `--bg-dark` | #0C0C0C | #EFEFEE |
| `--bg-surface` | #1A1A1A | #FFFFFF |
| `--bg-hover` | #242424 | #E6E6E5 |
| `--border` | #333333 | #D4D4D2 |
| `--text-main` | #E0E0E0 | #111113 |
| `--text-bright` | #D4D4D4 | #000000 |
| `--text-muted` | #A3A3A3 | #666461 |
| `--text-dim` | #808080 | #A5A3A0 |
| `--white` | #FFFFFF | #000000 |

Page tokens are a separate palette from chrome tokens in section 3. Dark page values differ from dark chrome hexes on purpose.

Known defect: some pages (like Focus and Error) define the dark `--border` as a cyclic `var` reference with `333` appended, breaking borders in dark mode, and carry a dead duplicate token block. Settings, ReleaseNotes, and Onboarding have been fixed. Fix the others when touching their token blocks.

---

## 9. Motion

| Duration | Easing | Use |
|---|---|---|
| 80ms | cubic ease out | Command bar slide and fade, tab close reveal |
| 100ms | quadratic ease out | Tab hover opacity |
| 200ms | quadratic ease in out | Tab selection, page dialogs |
| 250ms | none | New Tab tile hover |
| 300ms | none | New Tab search focus |
| 400ms | quadratic ease in out | Toolbar tint |
| 600ms | none | New Tab entrance |
| 900ms | linear | Security badge spin |
| 1s | none | Focus page fade in |
| 1.2s loop | sine ease in out | Loading sweep |
| 1.5s loop | none | Recording pulse |

Rules:

- Hover reveals fade. Selection expands. Arrival slides up.
- Never animate layout affecting properties on the main thread beyond 400ms.
- `prefers-reduced-motion` collapses durations to near zero on HTML pages. Only New Tab and the Onboarding prototype honor it today. WPF keeps instant swaps.

---

## 10. Theming rules

1. Dark is default. Light is warm paper. System follows `prefers-color-scheme`. The app accent is user chosen, stored in settings, injected into pages.
2. Chrome must use `DynamicResource` so runtime swaps work. Pages use the CSS variable block from section 8.10.
3. The toolbar tint is the only chrome element that adapts to content. `ToolbarTintAdapter` animates it over 400ms with a quadratic ease. It rejects near white and strong green backgrounds by falling back to #111111, and swaps local text tokens when computed luminance passes 0.5. Never extend content adaptation beyond the toolbar.
4. `ThemeManager` swaps the active theme at runtime by replacing the first merged dictionary in `App.xaml` and pushes the theme to web views as `data-theme`. Do not add a third theme file. Extend the token pairs.

---

## 11. Rules of the road for writing UI code

1. **New chrome controls:** XAML with `DynamicResource` tokens and shared styles from `App.xaml`. Reuse `IconBtn`, `TitleBtn`, `CloseTitleBtn`, `CommandBarInputStyle`, `FaviconTabListStyle`, and the global `ContextMenu`, `MenuItem`, `Separator`, `ToolTip`, `ContextMenuNavRowTemplate`, and `ScrollBar` styles. `TabCloseBtn`, `SidebarActionBtn`, `AddressBarStyle`, and `NewTabBtn` still exist but nothing uses them today. Do not inline new chrome styles in windows unless truly local. Suggestion rows are currently duplicated between `Window.Resources` and the command bar. One shared style is the target.
2. **New internal pages:** HTML under `Resources/Pages`. New Tab, Settings, OneTab, ReleaseNotes, and Error load templates directly with `ResourceLoader.LoadTemplate`. History, Downloads, and Onboarding render through `TemplateRenderer`. The Focus page is loaded raw with no theme substitution. All interactive pages post IPC over `window.chrome.webview.postMessage` with the `{{IPC_TOKEN}}` prefix. Theme with the CSS variable block, accent with `--accent` and `--accent-rgb`.
3. **No emoji as UI icons in new code.** Downloads folder, Error warning triangle, and OneTab stars are legacy. Replace them with SVG over time.
4. **Every clickable thing** gets `Cursor="Hand"` and a tooltip naming its shortcut where one exists. Window control buttons currently have no tooltips.
5. **Never hard code hex in XAML or C# chrome.** Sanctioned exceptions: transient hover washes #0AFFFFFF and #14FFFFFF, toolbar tint overrides, page local styles, and the documented legacy colors in sections 3.3 and 5.
6. **Keep state out of the visual layer.** Controllers live in `Services/UI`. Windows stay thin. A UI change that needs a C# rewrite of a controller method is a red flag.
7. **Dark first authoring:** write for dark, then verify light. Watch muted text contrast, accent wash strength, shadow visibility. History, Error, and Focus still hard code dark colors and break in light mode.
8. **Add the setting, then the control:** new toggles go into `Settings.html` with a description that names the tradeoff, like requires restart or a privacy tradeoff, where true.

---

## 12. Accessibility requirements

- Text tokens must hold WCAG AA on their surface, per the pairs in section 3. `TextMuted` and `--text-dim` are for captions, placeholders, and metadata, never primary content.
- All interactive elements are keyboard reachable. Pages use `focus-visible` rings. Chrome removes `FocusVisualStyle` on every button and list item. Tabs, the command bar, and dialogs are supposed to get keyboard focus styles instead. Today they do not.
- `prefers-reduced-motion` is respected on pages. Only New Tab and the Onboarding prototype implement it today.
- Hit targets at least 26px, except tab close buttons. Titles are ellipsized. Favicons render with `BitmapScalingMode=HighQuality`.
- Destructive actions always confirm. Permanent actions like Focus lock double confirm.

---

## 13. Design review checklist

Before submitting UI work, verify:

- [ ] Uses tokens, not literals. Sanctioned exceptions only.
- [ ] Icons from the stroke set. No new emoji. Consistent stroke weight.
- [ ] Type from the chrome scale in section 4 or the page stack.
- [ ] Motion inside the duration and easing table. Reduced motion handled.
- [ ] Works in dark and light. Toolbar tint sanitizers respected.
- [ ] Empty state exists. Destructive actions confirm. Tooltips carry shortcuts.
- [ ] Hit targets and focus behavior pass section 12.
- [ ] No new chrome feature is visible and noisy at once. If it can be quieter, make it quieter.
