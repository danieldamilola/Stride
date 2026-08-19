# Stride — UI/UX Specification

> A detailed walkthrough of the Stride browser interface: its anatomy, components, states, interactions, motion, and internal pages. Companion to [`DESIGN.md`](DESIGN.md) (design system/tokens) and [`PRODUCT.md`](PRODUCT.md) (product intent).

**Last updated:** 2026-08-18

---

## 1. Overview

Stride is a borderless, single-toolbar Windows browser built on WPF + WebView2. The core UX premise: **the tab strip is the toolbar**. There is no permanent, bulky address bar eating vertical space by default — navigation lives in a 40px strip at the top that merges the title bar, tabs, nav controls, and window controls into one row. The URL is summoned on demand (`Ctrl+L`) as a floating command bar.

Everything downstream (theme, accent, toolbar tint, internal pages) is driven by a small set of tokens defined in `Themes/DarkTheme.xaml` and `Themes/LightTheme.xaml`.

### 1.1 Window anatomy

```
┌─────────────────────────────────────────────────────────────────────────┐
│ ←  →  ↻   [favicon] [favicon] [◎ title ×]   host/path  +   ⇩  ⚙   — □ × │  ← 40px toolbar (tab strip)
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│                       WebView2 content (full bleed)                     │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

| Element | Region | Notes |
|---|---|---|
| Window | 1280×800 default, min 600×400, centered on start, `WindowStyle=None` | Custom chrome: `WindowChrome CaptionHeight=0`, 6px resize border, no glass frame. Dragging anywhere on the toolbar moves the window. |
| Toolbar | 40px high, `SidebarBrush` background | Doubles as title bar and tab strip; double-click maximizes (standard chrome behavior). |
| Content area | Remaining space, `BaseBrush` | WebView2 host + 1.5px loading bar overlay at top edge. |
| Command bar | Floating overlay (Z=100) over a 50% black dim layer | Only visible in Floating Command Bar mode. |

---

## 2. The Tab Strip (toolbar) — anatomy

Four regions in one 40px row (`MainWindow.xaml` `ToolbarRow`):

1. **Navigation cluster** (left) — Back / Forward / Refresh. 26×26 icon buttons, tooltips `Back (Alt+Left)`, `Forward (Alt+Right)`, `Refresh (F5)`. Each is independently toggleable in Settings.
2. **Tabs + address** (center, flexible) — the horizontal favicon-pill tab list, plus a right-anchored group containing the URL indicator and the **+** new-tab button (26×26, tooltip `New Tab (Ctrl+T)`). The whole group can be moved to the left of the tabs via `Address Bar Position`.
3. **Action cluster** — Downloads button with live progress ring, Settings gear with update badge. Independently toggleable.
4. **Window controls** — Minimize (42×40), Maximize, Close (42×40). Close button hovers to `DangerBrush` (#C4453A) red.

The toolbar also receives an **adaptive tint** from the active site's theme color (see §12).

### 2.1 Favicon pill tabs — the "Stride Signature"

Tabs are 28px-tall horizontal pills. The state model is deliberately minimal:

| State | Visual |
|---|---|
| **Inactive** | 16×16 favicon only, 40% opacity, 0.7 container opacity; no title, no close button |
| **Active** | `AccentWash` (amber-tinted) pill, favicon at full opacity, page title (max 160px, ellipsized, 12px), close button at the pill's trailing edge |
| **Hover** | Item opacity animates 0.7 → 1.0 over 100ms (ease-out); faint `#0DFFFFFF` pill wash; inactive tabs reveal a tiny 12×12 close button overlaying the favicon's top-right corner (80ms fade, 5px stroke) |
| **Hibernated** | Favicon at 15% opacity — the only hint the tab's memory was reclaimed (see §13) |
| **Pinned** | Favicon circle on a persistent subtle `SurfaceHoverBrush` wash; **never** shows title or close button; right-click → Unpin; pinned tabs are moved to the front of the strip |

Default placeholder: the Stride mark at 30% opacity, hidden once a real favicon loads.

**Scroll behavior:** the strip is a horizontal `ListBox` with no scrollbars. Mouse wheel and trackpad scroll horizontally (`delta × 0.5` for smooth precision). Tabs cycle with `Ctrl+Tab` / `Ctrl+Shift+Tab`.

**Context menu (right-click on a tab):**

- **Pin Tab / Unpin Tab** — pins move the tab to index 0.
- **Duplicate Tab** — clones the URL into a new tab.
- Separator, then **Close Tab** (only for unpinned tabs).

**Drag & drop** of tabs is supported within the strip (`TabStripDragDropBehavior`).

### 2.2 URL indicator (Floating Command Bar mode)

Instead of an input, the default mode shows a **bare-text read-only URL**: a globe (or lock, on HTTPS — see §4) icon + the trimmed host and path (`www.` stripped, `/` dropped) in muted 11.5px type, 80px ellipsized. Clicking it (or `Ctrl+L`) opens the command bar. Internal pages (`internal://…`) display their title instead of a URL. It is *text, not a box* — the toolbar stays flat and quiet.

---

## 3. The Command Bar (Ctrl+L)

The signature navigation surface. Invoked from the URL label, `Ctrl+L`, or the New Tab search field.

**Open animation (80ms, cubic ease-out):** backdrop + panel fade in; panel slides down 10px into place.

| Property | Value |
|---|---|
| Panel size | 540px wide, centered, 80px from top of window |
| Surface | `#2C2C32`, 1px `#444449` border, 8px corner radius, 4px padding |
| Elevation | 40px blur drop shadow at 60% black, 8px depth |
| Backdrop | 50% black, click-to-dismiss |

**Behavior:**
- On open, the active tab's URL is pre-filled and **select-all** is applied, so typing replaces it immediately.
- Typing streams suggestions (history matches) with an 80ms live update.
- `Enter` navigates: input is resolved via `ResolveInput` (URL vs search-engine query per the configured search engine).
- `Esc` or clicking the backdrop closes; the WebView regains focus.
- Closing is a 80ms fade + 10px slide-up.

### 3.1 Suggestions list

Each row: clock icon (10px, muted) + text (13px, `#C8C4BF`, ellipsized). Row height ≈ 34px with 4px corners.

| State | Visual |
|---|---|
| Default | transparent |
| Hover | `#0EFFFFFF` wash, icon brightens |
| Selected | `AccentWash` background + 3px amber accent bar on the left edge + accent-colored clock icon |

Keyboard: ↑/↓ cycle through suggestions, `Enter` commits, `Esc` dismisses. Directional navigation cycles (wrap-around).

### 3.2 Standard address bar mode (optional)

When "Floating Command Bar" is off, a conventional 240px pill (8px radius, `#0DFFFFFF` fill, `#1AFFFFFF` border) with an inline lock/globe icon and "Search or enter URL" placeholder appears in the toolbar. Suggestions attach as a flush dropdown (`8,8,0,0` corner, `#111111` fill, `#2A2A2E` border) that stays width-locked to the bar.

---

## 4. Security indicators

- **Globe** — HTTP or non-web pages (muted, 10px stroke icon).
- **Lock** — HTTPS.
- Badge logic lives in `Services/UI/SecurityBadgeHelper.cs` and switches both the toolbar URL icon and the standard bar's inline icon.

Note: **no padlock ceremony** — no colored address-bar states, no green background washes. The indicator is a quiet glyph.

---

## 5. Loading feedback

A **1.5px indeterminate sweep** pinned to the top edge of the content area: a 100px amber-gradient comet (`#00D4A574 → #D4A574 → #00D4A574`) slides left→right on a transparent track, looping every 1.2s (sine ease-in-out). Owned by `LoadingAnimationController`. It starts on navigation and stops when the page finishes; it is invisible the rest of the time.

---

## 6. Downloads

### 6.1 Toolbar indicator

- Idle: a plain 12px download glyph.
- **Active downloads:** the button is ringed by a 24px circle — a neutral `BorderBrush` track plus a **green (#10B981) progress arc** (dashed stroke whose dash pattern encodes percent, rotated to start at 12 o'clock). Progress updates live.
- `Ctrl+J` or clicking opens the Downloads page.

### 6.2 Downloads page (`internal://downloads`)

Layout: 240px dark sidebar (brand header + "All Downloads" nav item) + content column (max-width 800px).

- **Rows:** 42×42 rounded file-type tile (accent-tinted) → filename (15px, ellipsized) → metadata line of status dot · state label · size · speed · ETA · start time.
- **Status dots:** green=completed, amber=in-progress (with soft glow), orange=paused, red=failed, gray=cancelled.
- **In-progress rows** show a 4px progress bar (accent fill, animated width transition 300ms).
- **Actions** (36×36 ghost buttons): Open file + Show in folder (completed); Pause/Resume (active); Cancel (active).
- Live updates polled from the host every 500ms; rows re-render in place.
- "Clear completed" is guarded by a confirm dialog that explicitly reassures: *files stay on disk, only the list entry is removed* (danger-styled confirm button).

---

## 7. Settings page (`internal://settings`)

Two-column layout: icon sidebar (240px) + scrollable content (max-width 720px, `40px` gutters).

**Sidebar sections:** General · Appearance · Privacy & Browsing · Keyboard Shortcuts · YouTube Enhancer · YouTube Unhook · Irreversible Focus · System.

Active section: 4px accent bar on the left edge, accent text, 15% accent wash. Hover: surface fill. The active tab persists across visits (localStorage).

**Content anatomy:** each section is a stack of **cards** (10px radius, surface fill, hairline border). Rows are label + optional description (12px dim) on the left, control on the right, separated by hairlines; rows show a subtle hover fill. Subsections get uppercase 11px letter-spaced headers.

**Controls:**
- **Toggle** — 36×20 pill, accent when on, knob slides 16px (200ms).
- **Select** — bordered, 13px, custom chevron, accent focus ring.
- **Number input** — 72px centered.
- **Action button** — 1px accent outline; hover inverts to accent fill.
- **Accent color** — native color picker plus swatch strip (28px circles, active = white ring + dot).
- **Shortcut badges** — key-cap-styled chips showing the current combo; click → "Press keys..." recording state (accent border + pulse animation); `Esc` cancels; per-row reset link restores the default.

**Notable UX:**
- Every change applies immediately (no Save button) via the `s(key, el)` IPC bridge.
- "Floating Command Bar" and SmartScreen/HW-accel rows carry explicit "requires restart" caveats in their descriptions.
- **Irreversible Focus:** a textarea for domains (one per line), "Save Domains", and a permanent-red **"LOCK PERMANENTLY"** button. Locking is guarded by a `confirm()` dialog stating the action cannot be undone. Once locked, the UI swaps to a green "Focus Mode is Permanently Locked" confirmation state and the inputs disappear.
- **System:** Set-as-default-browser button, hardware-accel toggle, auto-update toggle, "Check for Updates" button that transitions to "Download & Install" when a newer version is found and to "Downloading update… Stride will restart shortly" while installing.

---

## 8. History page (`internal://history`)

Header: "History" (22px) + search box (260px, 8px radius, inline magnifier) + "Clear all".

- Entries sort newest-first and group **by day** (Today / Yesterday / weekday / full date), then **by domain**.
- Single-entry domains render inline: favicon (16px, DuckDuckGo icon service) + title (accent on hover) + muted URL + time.
- Multi-entry domains collapse under a header row (favicon · prettified domain label · count · chevron) — clicking toggles the group; collapsed groups rotate the chevron −90°.
- Sticky date headers.
- Empty states: "No browsing history yet." / "No results found."
- "Clear all" requires a confirm dialog styled in danger red (`#E55050`), warning the action is permanent.

---

## 9. New Tab page (`internal://newtab`)

A full-bleed glassmorphic page over a custom background image (shipped set in `Resources/Pages/Backgrounds/`, extensible via "Custom Wallpapers" folder button).

- **Search field** — 540px, 16px radius, 24px backdrop blur over 40% black tile, 16px text; focus: accent border, glow ring (`0 0 0 4px` accent at 15%), 300ms transitions. Enter = search/navigate. **Type-any-key focuses the search field** (unless a dialog is open or modifiers are held).
- **Shortcut tiles** — 90×90 rounded squares, blurred glass fill, favicon (32px) + 12px name; hover: lift −4px, shadow, icon scales 1.08; `focus-visible` gets an accent ring. A dashed "+" tile adds shortcuts (max 10). Hovering a tile reveals a circular × badge (accent on hover) to remove; Delete/Backspace removes from the keyboard.
- **Add dialog** — blurred overlay, 360px rounded card, Name + URL fields (URL auto-prefixes `https://`), accent-focused inputs; Enter saves, Esc cancels, overlay click cancels.
- **Keyboard hints** — a quiet bottom-center legend of kbd chips: `Ctrl L` URL · `Ctrl T` Tab · `Ctrl H` History.
- **Wallpapers button** — bottom-right glass chip that opens the backgrounds folder in Explorer.
- **Motion:** content fades + slides up 12px over 600ms (custom cubic-bezier `0.2,0.8,0.2,1`); `prefers-reduced-motion` zeroes all durations.

---

## 10. OneTab page (`internal://onetab`)

List of **groups** (cards, 8px radius): editable group name, saved time, tab count, and per-group actions. Each row: star toggle (amber `#F59E0B` when starred) · title (accent on hover) · muted URL · hover-revealed × delete (danger on hover). Search box at top. Empty state is centered muted text.

---

## 11. Focus mode, error page, dialogs

### 11.1 Focus mode
- **Locked target pages** are redirected to `internal://focus`: a pure-black screen with "FOCUS" in 120px, 700-weight, 20px letter-spaced type that fades in over 1s. Deliberately stark — the page *is* the statement. Scales down at narrow widths.
- **Blocked navigation** shows a "Focus Locked" interstitial. A locked browser also enforces community adult-content + Telegram blocklists which cannot be disabled.

### 11.2 Error page (`internal://error`)
Centered composition: large muted glyph (64px, 40% opacity) · title (22px) · message (14px, `#7A7A7A`, max 500px) · failed URL (12px, break-all) · accent **Retry** button (10×24 padding, 8px radius; hover darkens). Retry self-navigates rather than using IPC (the page runs on `about:blank`, which the privileged-IPC gate rejects).

### 11.3 Dialogs (`BaseBrowserDialogWindow`)
Shared frameless host for JS alerts/confirms/prompts and permission prompts: 450px, 8px radius, surface fill, 24px shadow (50% black), 10px outer margin, centered on owner, `Topmost`.
- Title: 16px semibold. Message: 14px muted, wrapped, max 80px height scroll.
- Optional single-line input (prompts) auto-focuses.
- Buttons: neutral **Cancel** (bordered, surface) + accent **OK** (accent fill, dark `#111113` text, default/Enter).

### 11.4 Web content context menu
An inlined WebView2 menu (graphite surface, 8px radius, 4px padding, 12px shadow): **Back · Forward · Reload · Save as… · Print… · View page source · Inspect**, each with its gesture hint right-aligned (`Alt+Left Arrow`, `Ctrl+R`, `Ctrl+S`, `Ctrl+P`, `Ctrl+U`, `Ctrl+Shift+I`), disabled states where unsupported.

---

## 12. Adaptive toolbar tint

The toolbar gently recolors to the active site's `theme-color`/favicon color:
- **400ms color animation**, quadratic ease-in-out (`ToolbarTintAdapter`).
- **Sanitizers:** near-white values (all channels > 245) fall back to base `#111113` — this prevents blinding-white toolbars when Dark Reader is active but the site's meta tag still claims white. Strong pure greens are rejected as clashing with the warm dark theme.
- **Dynamic contrast:** if the tinted background's luminance > 0.5, toolbar text/icon brushes locally swap to dark grays; otherwise local overrides are removed so the global theme applies.

The rest of the UI is unaffected — only the toolbar adapts, keeping the chrome calm and the content the star.

---

## 13. Memory & hibernation UX

Inactive tabs are **hibernated** after a base of 5 minutes (sooner under memory pressure, LRU order; never the active tab; downloads and pinned tabs are excluded via `IsTabSafeToHibernate`). UX contract: the favicon fades to 15% opacity — a silent, non-blocking hint. Selecting the tab restores it instantly and the favicon returns to full strength. No dialogs, no toasts, no countdowns: the browser manages memory *quietly*.

---

## 14. Motion system

| Token | Duration | Easing | Used for |
|---|---|---|---|
| Instant | — | — | State swaps that must not lag (tab switch, icon states) |
| Fast | 80ms | quadratic/cubic ease-out | Command bar open/close, close-button fades, suggestion hover |
| Standard | 100–200ms | quadratic ease-in-out | Tab hover opacity (100ms), pill selection expand (200ms), toggles (200ms), buttons (150ms) |
| Slow | 300–400ms | cubic-bezier `0.2,0.8,0.2,1` / quadratic ease-in-out | Search field focus, toolbar tint (400ms), progress bars (300ms) |
| Arrival | 600ms | cubic-bezier `0.2,0.8,0.2,1` | New Tab page entrance, keyboard hints (300ms delay) |
| Loop | 1.2s | sine ease-in-out | Loading sweep |

Rules: hover-reveal UI fades, never pops; selection state expands; everything respects `prefers-reduced-motion` where feasible.

---

## 15. Confirmations & destructive actions

Destructive actions always confirm, with danger styling (`#E55050` text/borders, red fill on confirm):
- Clear history ("permanently delete… cannot be undone")
- Clear completed downloads (with the files-are-safe reassurance)
- Remove OneTab entries, remove shortcuts (implicit — hover badge)
- **Lock Focus Mode permanently** (additional `confirm()` before the UI swap)

---

## 16. Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+L` | Open command bar (or focus standard bar, select-all) |
| `Ctrl+T` | New tab |
| `Ctrl+W` | Close tab |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Cycle tabs (wrap-around) |
| `Ctrl+1…9` | Switch to tab by index |
| `Ctrl+H` | History |
| `Ctrl+J` | Downloads |
| `F5` / `Ctrl+R` | Reload |
| `Alt+Left` / `Alt+Right` | Back / Forward |
| `Ctrl+S` / `Ctrl+P` | Save as… / Print… |
| `Ctrl+U` / `Ctrl+Shift+I` | View source / Inspect |
| `Esc` | Dismiss command bar, suggestions, dialogs |
| `F11` | Fullscreen (window chrome toggles) |
| *New Tab page* | Any printable key focuses search; Delete removes hovered shortcut |
| *Shortcut badges (Settings)* | Click → press combo → `Esc` to cancel |

All shortcuts are **rebindable** in Settings → Keyboard Shortcuts (badge recording UI; per-action reset).

---

## 17. Empty states

| Surface | Empty state |
|---|---|
| Downloads | 📂 glyph (48px, 20% opacity) + "No downloads yet." |
| History | "No browsing history yet." (filtered: "No results found.") |
| OneTab | Muted centered text |
| Suggestions | No list shown (bar closes on navigation) |

---

## 18. Accessibility baseline

- **Contrast:** text tokens tuned for WCAG AA on their surfaces (e.g., `#E8E4DF` on `#111113`, `#111113` on `#F5F5F3`); muted/dim tokens are reserved for captions and non-essential metadata, never body text.
- **Focus:** custom `focus-visible` accent rings on web pages; WPF focuses via keyboard-navigation direction (no default dotted rects — `FocusVisualStyle={x:Null}` on decorative controls, but every interactive element remains keyboard-reachable).
- **Hit targets:** icon buttons ≥ 26×26; context-menu and dialog rows ≥ 34px.
- **Reduced motion:** `prefers-reduced-motion` honored on internal pages.
- **Legibility:** `TextTrimming="CharacterEllipsis"` everywhere titles can overflow; high-DPI-aware WPF rendering with `RenderOptions.BitmapScalingMode=HighQuality` on favicons.
- **Keyboard-first surfaces:** command bar (full key support), tab strip (cycling, context menu via keyboard), history (type-to-filter), New Tab (type-to-search).

---

## 19. Implementation surfaces

| Surface | Technology | Location |
|---|---|---|
| Chrome (toolbar, tabs, command bar, dialogs) | WPF/XAML | `MainWindow.xaml`, `App.xaml`, `BaseBrowserDialogWindow.xaml`, `Themes/*` |
| Internal pages | HTML/CSS/JS over WebView2 with token-templated IPC | `Resources/Pages/*.html`, `Services/Pages/TemplateRenderer.cs` |
| UI behaviors | Controllers | `Services/UI/*` (CommandBar, TabStrip, ToolbarTint, Loading, SecurityBadge, DragDrop) |
| Content scripting | JS injected per-site | `Resources/Scripts/*` (dark mode, YouTube enhancer/unhook, theme-color) |

---

## 20. Open UX debt / roadmap

- **Vertical tabs mode** (roadmap) — power-user layout for ultrawide monitors; the pill model should carry over unchanged.
- Extension options pages (TCLens) render in-browser today; marketplace integration will need a canonical options-page chrome.
- The standard address bar and floating command bar are two parallel code paths — visual parity is maintained by shared tokens, but a future consolidation pass should unify them.