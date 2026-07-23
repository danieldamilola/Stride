# Handoff: SpurBrowser Linux Port — Phase 4 UI Polish Complete

**Date:** 2026-07-16  
**Session type:** Coding  
**Status:** Ready to Resume  
**Project root:** `C:\dev\SpurBrowser`

---

## 1. Goal

Build a functionally complete Linux port of the Stride browser (WPF/WebView2 → Avalonia/WebKitGTK) with feature parity to the Windows version. The Linux project lives at `src/Stride.Linux/`, the shared portable library at `src/Stride.Core/`.

---

## 2. Current State

**Fully complete:**
- Core compilation: Linux project (`Stride.Linux.csproj`) builds 0 errors, 1 pre-existing AVLN3001 XAML runtime-loader warning; Windows project (`SpurBrowser.csproj`) builds 0 errors, 0 warnings
- Tab lifecycle: create, close, cycle, index-activate, hibernate, restore closed (Ctrl+Shift+T)
- Navigation: go back/forward, reload, internal page routing
- Zoom controls (Ctrl+=, Ctrl+-, Ctrl+0) via CSS `zoom` + `InvokeScript`
- Keyboard shortcuts: Ctrl+T/W/Tab/Shift+Tab/D1-9/L/F/H/J/D/B/P/, /Shift+O/Shift+C/Shift+1/Shift+S, Alt+Left/Right/D/1-9, F5/F6/F11/F3/Shift+F3, Escape (fullscreen exit)
- IPC bridge: `window.chrome.webview.postMessage(msg)` → `invokeCSharpAction(msg)` → `WebMessageReceived` → `WebMessageRouter.RouteAsync`
- All internal page generators ported (Settings, History, Downloads, NewTab, OneTab, Error, Focus)
- All internal pages receive real data from registered services (HistoryStore, DownloadStore, OneTabStore, BrowserSettings)
- Focus Mode blocklist + domain matching
- YouTube Enhancer + Unhook content script injection
- ContentScriptInjector (InvokeScript on NavigationCompleted)
- Dark Reader injection toggle
- FaviconLoader (HTTP + DuckDuckGo fallback + LRU cache)
- LinuxDefaultBrowserRegistrar (XDG .desktop + xdg-mime)
- Session restore on startup (ISessionStore.Load) + save on tab add/close/window-close
- History auto-tracking on NavigationCompleted
- Find in page (Ctrl+F, find bar UI, find-in-page.js injected, match count via IPC)
- Find results display ("N matches") via `OnFindResults` event
- Security icon (🔒 green HTTPS / 🌐 gray HTTP)
- Loading bar animation (ProgressBar shown/hidden with delay)
- Title bar drag + double-click maximize
- Tab close button
- Tab right-click context menu (Duplicate, Pin/Unpin, Close)
- Tab drag & drop reorder (PointerPressed/Moved/Released)
- Tab tooltip
- Pin tab protection (CloseTab blocks pinned tabs, amber visual indicator)
- Pin visual update via `TabPinStateChanged` event (background + close button toggle)
- Duplicate tab prevention (BlockDuplicateTubes setting)
- Keyboard shortcut recording IPC (set-shortcut/reset-shortcut handlers in WebMessageRouter)
- ShortcutDefaults ported to Core (all 23 actions, GetCombo/GetDefault)
- Settings page renders keyboard shortcuts table
- Find bar position (Grid.Row 1, between toolbar and content)
- Copy URL (Ctrl+Shift+C) with clipboard + "URL copied!" title feedback
- Send all to OneTab (Ctrl+Shift+1)
- Save all tabs / session (Ctrl+Shift+S)
- Print via window.print() (Ctrl+P)
- Settings page (Ctrl+,)
- Focus address bar on new tab
- URL label host display (strips www, shows scheme for non-HTTPS)
- TabList selection guard (_isUpdatingTabSelection)
- Proper shutdown (save settings, save session, engine shutdown)

**Partially complete:**
- Pin tab close button visibility (starts hidden, code-behind toggles via `TabPinStateChanged`)
- URL label security icon uses unicode emoji (🔒/🌐) instead of proper lock icon SVG

**Not started:**
- Address bar autocomplete/suggestions dropdown
- Ctrl+Tab tab switcher overlay (thumbnail grid)
- Favicon-based toolbar adaptive tinting
- Tab close button hover behavior (always hidden currently)
- Keyboard shortcut rebinding UI in Settings page (needs Settings.html JS recording panel + IPC roundtrip)

---

## 3. What We're Currently Working On

The session completed Phase 4 (UI Polish & Edge Cases). No active work in progress.

---

## 4. What We Tried That Failed

- **Avalonia XAML DataTrigger for pin tab visual**: Attempted to use `<DataTrigger>` inside `<Border.Styles>` to toggle close button/pin icon visibility via `IsPinned` binding. Failed because Avalonia 12's `DataTrigger` inside `Styles` collection only supports `Setter` with `Property` on the styled element, not `TargetName` targeting nested children. Replaced with code-behind `OnTabPinStateChanged` event that walks `GetRealizedContainers()`, sets `Background` on the `ListBoxItem`, and uses `FindControl<Button>("TabCloseBtn")` to toggle `IsVisible`.

- **Avalonia `ListBox.GetContainerFromEventSource()`**: Protected method, inaccessible from external code. Replaced with visual tree walk via `StyledElement.DataContext` chain.

- **Avalonia `IClipboard.SetTextAsync()`**: Initially failed with CS1061 until `using Avalonia.Input.Platform;` was added — the `SetTextAsync` extension method lives in that namespace and wasn't resolved by default.

---

## 5. Next Steps

1. Wire keyboard shortcut recording UI in Settings.html (the JS `startRecording`/`resetShortcut` callbacks that send `set-shortcut`/`reset-shortcut` IPC messages — infrastructure is in place in WebMessageRouter)
2. Add address bar autocomplete dropdown (popup overlay with history search results as user types in command bar)
3. Add Ctrl+Tab tab switcher overlay (optional — cosmetic enhancement)
4. Implement favicon toolbar tinting (ExtractDominantColor from favicon byte[], blend 8% into toolbar)
5. Add tab close button hover behavior (PointerEntered/Exited)

---

## 6. Key Decisions & Constraints

- Linux project uses `Avalonia 12.0.4` + `Avalonia.Controls.WebView 12.0.1` (WebKitGTK wrapper) — no Chromium bundling
- IPC bridge: `window.chrome.webview.postMessage` polyfilled to `invokeCSharpAction` → `NativeWebView.WebMessageReceived` event
- All internal pages rendered via `NavigateToString()` with IPC polyfill prepended
- Session store uses JSON + atomic writes under `IAppDataPaths.SessionFile`
- History, OneTab, Downloads, Settings all use JSON file persistence
- TabEngine exposes settable service properties (Router, InternalPages, ContentInjector, FocusBlocklist, Settings, HistoryStore, SessionStore, OneTabStore, DownloadStore) wired via DI in MainWindow constructor
- Keyboard shortcuts are hardcoded in MainWindow_KeyDown (not rebindable at runtime — the `CustomShortcuts` dictionary exists in BrowserSettings and IPC handlers are ported, but no UI to record them)
- Focus mode blocks navigation via NavigationStarted event callback
- Content scripts injected on NavigationCompleted via InvokeScript
- Find bar occupies Grid.Row 1 (between toolbar row 0 and content row 2)
- Blocked features (no NativeWebView/WebKitGTK equivalent): ExtensionManager (AddBrowserExtensionAsync), network-level ad blocking (WebResourceRequested), download progress tracking (DownloadStarting), DevTools (OpenDevToolsWindow), page screenshot (CapturePreviewAsync)

---

## 7. Open Questions & Blockers

None. All remaining items are purely additive features, not blockers.

---

## 8. Files & Artifacts

| Item | Type | Location |
|------|------|----------|
| SpurBrowser.csproj | Windows WPF project | `C:\dev\SpurBrowser\SpurBrowser.csproj` |
| Stride.Core.csproj | Shared portable library | `C:\dev\SpurBrowser\src\Stride.Core\Stride.Core.csproj` |
| Stride.Linux.csproj | Linux Avalonia project | `C:\dev\SpurBrowser\src\Stride.Linux\Stride.Linux.csproj` |
| MainWindow.axaml | Linux main window XAML | `C:\dev\SpurBrowser\src\Stride.Linux\Views\MainWindow.axaml` |
| MainWindow.axaml.cs | Linux main window code-behind | `C:\dev\SpurBrowser\src\Stride.Linux\Views\MainWindow.axaml.cs` |
| TabEngine.cs | Tab lifecycle engine | `C:\dev\SpurBrowser\src\Stride.Linux\Engine\TabEngine.cs` |
| WebMessageRouter.cs | IPC message router | `C:\dev\SpurBrowser\src\Stride.Linux\Services\WebMessageRouter.cs` |
| ContentScriptInjector.cs | Script injection on navigation | `C:\dev\SpurBrowser\src\Stride.Linux\Engine\ContentScriptInjector.cs` |
| InternalPages.cs | Internal page facade | `C:\dev\SpurBrowser\src\Stride.Linux\Services\InternalPages.cs` |
| SettingsPage.cs (Linux) | Settings HTML generator | `C:\dev\SpurBrowser\src\Stride.Linux\Services\Pages\SettingsPage.cs` |
| FocusBlocklistService.cs | Domain blocklist download/cache | `C:\dev\SpurBrowser\src\Stride.Linux\Services\FocusBlocklistService.cs` |
| FocusDomainMatcher.cs | Domain matching logic | `C:\dev\SpurBrowser\src\Stride.Linux\Services\FocusDomainMatcher.cs` |
| FaviconLoader.cs | HTTP favicon with cache | `C:\dev\SpurBrowser\src\Stride.Linux\Services\FaviconLoader.cs` |
| YouTubeEnhancer.cs | YouTube config/script builder | `C:\dev\SpurBrowser\src\Stride.Linux\Services\YouTubeEnhancer.cs` |
| YouTubeUnhook.cs | YouTube unhook config/script | `C:\dev\SpurBrowser\src\Stride.Linux\Services\YouTubeUnhook.cs` |
| LinuxDefaultBrowserRegistrar.cs | XDG desktop registration | `C:\dev\SpurBrowser\src\Stride.Linux\Services\LinuxDefaultBrowserRegistrar.cs` |
| Composition.cs | DI container | `C:\dev\SpurBrowser\src\Stride.Linux\Services\Composition.cs` |
| SessionStore.cs | Session persistence | `C:\dev\SpurBrowser\src\Stride.Linux\Services\SessionStore.cs` |
| HistoryStore.cs | History persistence | `C:\dev\SpurBrowser\src\Stride.Linux\Services\HistoryStore.cs` |
| DownloadStore.cs | Downloads persistence | `C:\dev\SpurBrowser\src\Stride.Linux\Services\DownloadStore.cs` |
| OneTabStore.cs | OneTab persistence | `C:\dev\SpurBrowser\src\Stride.Linux\Services\OneTabStore.cs` |
| BrowserSettings.cs | Settings model (Core) | `C:\dev\SpurBrowser\src\Stride.Core\Models\BrowserSettings.cs` |
| BrowserTab.cs | Tab model (Core) | `C:\dev\SpurBrowser\src\Stride.Core\Models\BrowserTab.cs` |
| ShortcutDefaults.cs | Keyboard shortcut definitions | `C:\dev\SpurBrowser\src\Stride.Core\Services\ShortcutDefaults.cs` |
| InternalUrls.cs | Internal URL constants | `C:\dev\SpurBrowser\src\Stride.Core\Models\InternalUrls.cs` |
| find-in-page.js | Find script resource | `C:\dev\SpurBrowser\src\Stride.Linux\Resources\Scripts\find-in-page.js` |

---

## 9. Context & Background

### Session history
- 2026-07-16 (earlier): Core compilation fix, project structure, address bar overlay, keyboard shortcuts, DefaultBrowserRegistrar, BrowserTab unification, resource file migration, InternalPages system, FaviconLoader, WebMessageRouter, ContentScriptInjector, FocusBlocklistService, YouTubeEnhancer/Unhook, TabEngine IPC wiring, DI composition
- 2026-07-16 (continued): Find bar UI + Ctrl+F/F3, wiring real data into internal pages, ShortcutDefaults ported to Core, find results IPC, zoom controls, session restore, history tracking, Alt+D/F6 focus address bar, Ctrl+H/J/B/D/, other nav shortcuts
- 2026-07-16 (current): set-shortcut/reset-shortcut IPC handlers, duplicate tab prevention, pin tab visual + protect, focus address bar on new tab, tab drag & drop reorder, URL label host display, TabList selection guard, tab close button, tab context menu, security icon, loading bar, title bar drag, copy URL, send all to OneTab, print, proper shutdown

---

## 10. Paste-In Opener

> SpurBrowser — a Linux port of the Stride WPF browser using Avalonia + WebKitGTK. All core features are ported and both the Linux and Windows projects build clean. The next step is to wire the keyboard shortcut recording UI in Settings.html (the JS `startRecording`/`resetShortcut` callbacks — the IPC handler infrastructure is already in place in `WebMessageRouter`, just need the HTML/JS side to call `chrome.webview.postMessage` with `set-shortcut:actionName:combo`). Want me to pick up there, or should I start on the address bar autocomplete dropdown?
