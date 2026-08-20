# Stride Architecture Audit & Proposed Changes

**Date:** 2026-08-13 · **Scope:** all non-test source in the Stride assembly
**Method:** structure walk + dependency map + scoring against the architecture skill guardrails
**Context:** single-assembly WPF app, ~30 source files, ~7,300 LOC, 1-2 developers. Findings are scoped to *this* scale — no distributed anything is being suggested.

---

## 1. Current shape (one paragraph)

Stride is a modular monolith with a proper DI composition root (`Composition.cs`), a clean layering at the top (ViewModels → Services/Engine → Models/Helpers), an interface-based seam around the stores (justified: tests use it), and a real typed web-message pipeline (internal pages → IPC token → `WebMessageRouter` → 7 handlers). The problems are all *concentration* problems: two god classes (`TabEngine` 1,134 lines, `MainWindow` 1,324 lines), one dead-code leftover of an abandoned refactor (`TabDragDropHandler`), one global-state bridge that crosses the layer boundary backwards (`TCLensMessageHandler` → `MainWindow` static fields), a dependency cycle between Engine and Services, and three coexisting service styles (DI instance, static-with-state, static-functional) that make "where does X live" a guess.

---

## 2. Scorecard

| Dimension | Score | Notes |
|---|---|---|
| Coupling | 3/5 | Engine ↔ Services cycle; Services → UI static fields; mostly acyclic elsewhere |
| Cohesion | 3/5 | Engine and MainWindow are junk drawers of webview plumbing, policies, and UI behaviors |
| Abstraction level | 4/5 | Deep modules (stores, router, internal pages); a few shallow/leaky spots (router contract, `EngineDependencies`) |
| Testability | 3/5 | Service layer testable (29 tests pass); all engine policy + all view logic untestable without WebView2/UI |
| Pattern consistency | 2/5 | Three service styles + static mutable fields; store interfaces justified, others ad-hoc |

---

## 3. Findings (ranked by value/risk)

### F1 — `TabEngine` is still a god class (1,134 lines, 16 injected deps, ~60 methods)
The slop pass made its responsibilities *visible* as named methods, but they all still live in one class. It owns: WebView2 factory + lifecycle, navigation policy (custom protocols, focus-lock, HTTPS upgrade), tab hibernation/eviction, downloads, extensions, IPC plumbing, theming, favicon loading, process-failure handling.

- **Cost today:** any change to navigation policy requires loading ~1,100 lines of context; the class can't be unit-tested; two features touching unrelated parts collide on one file.
- **Fix (in rounds, build+test between each):**
  1. `WebViewFactory` — everything from `CreateWebViewForTab` through `ConfigureCoreWebView` (creation args, environment init).
  2. `TabHibernationManager` — `HibernateInactiveTabs`/`HibernateTab`/`EvictExcessWebViews`/`IsTabSafeToHibernate` + the timer (already near-identical shapes; move together with the timer field).
  3. `NavigationPolicies` — `TryHandleCustomProtocol`, `TryHandleFocusLock`, `TryUpgradeToHttps` (+ tests, pure decision methods).
  4. `MessageBridge` — `WireWebMessageReceived`, `WireNewWindowRequested`, token verification, `WebMessageReceived` event.
- **Effort:** L (multi-day, done in verified rounds). Highest-value change in this list.

### F2 — `MainWindow` is a god UI class (1,324 lines, ~75 members)
Owns window chrome, command bar, security spinner, toolbar tint, loading animation, drag-drop, keyboard shortcuts, TCLens launch, settings propagation, session restore, clipboard copy, update badge.

- **Cost today:** every UI behavior change touches this file; the TCLens flow (below) lives here partly because this class is the only place things "fit."
- **Fix (in rounds):**
  1. `CommandBarController` — address bar focus/suggestion/show/hide + the 8 duplicate handlers already consolidated once.
  2. `TabStripController` — the inline drag-drop (see F3), tab switching by index, mouse-wheel, right-click menu.
  3. `BrowserChromeBehaviors` — security spinner, loading animation, toolbar tint, zoom stubs.
  4. Move `PendingTCLens*` + launch logic into a service (see F4).
- **Effort:** L. Do *after* F1 (engine is the bigger risk), or interleave one extraction per week.

### F3 — Dead code: `Helpers/TabDragDropHandler.cs` (72 lines)
Grep shows it is **never instantiated**. `MainWindow` has its own inline drag-drop (lines 1287-1341) doing the same job. This is an abandoned half-refactor.

- **Fix:** delete the file; keep the working inline version. (Or, if you prefer the helper's shape, adopt it and delete the inline copy — but do not keep both.)
- **Effort:** S. Pure deletion, zero risk.

### F4 — TCLens bridge: service layer reads UI static mutable fields
`TCLensMessageHandler` reads `MainWindow.PendingTCLensText/Url/Title` (public static fields, set in `MainWindow.HandleNativeTCLensShortcutAsync`). Direction of dependency: **Services → UI class**, via global mutable state, bypassing DI. Also `Services.MessageHandlers → StrideBrowser.ViewModels` (4 of 7 handlers take `BrowserViewModel`).

- **Cost today:** cross-window data flow is invisible (no wiring, no lifetime); breaks the moment a second window or a restart path appears; the fields persist stale values after the TCLens window closes.
- **Fix:** introduce a tiny `TCLensTransfer` singleton (service) that holds the pending payload and clears it on read; MainWindow writes to it, handler reads from it. Then move the message handlers into the Engine layer or behind an interface (see F5).
- **Effort:** S-M. Safe, contained.

### F5 — Dependency cycle: Engine ↔ Services, and Services → ViewModels
`TabEngine` (Engine) depends on `Services` (stores, injector, `CustomDownloadManager`…). 6 of 7 `Services.MessageHandlers` depend on `TabEngine` (Engine). 4 also depend on `BrowserViewModel`. Net effect: the "browser core" and the "web-message control plane" are one tangle, and the IPC layer reaches straight into the UI view model.

- **Cost today:** understanding "what happens when the page sends a message" requires crossing both layers twice; the IPC layer can't be tested without the whole app; handlers can't be reused by any future surface (e.g. a second window, extensions).
- **Fix:** move the handlers from `Services\MessageHandlers` into the Engine layer (they are the core's IPC controllers, not app services), and replace their `BrowserViewModel` dependency with narrow command interfaces or events (`ISettingsNotifier`, per-handler events — see F6). Handlers should depend on stores + engine commands, never on `MainWindow`/`ViewModel`.
- **Effort:** M (move + interface sweep; mostly mechanical, verify with build + the 29 tests).

### F6 — `WebMessageRouter` contract is leaky and uses downcasting
`IWebMessageHandler.Register(prefixDict, exactDict)` exposes the router's internal dictionaries as the handler contract, and the router's constructor type-tests (`if (handler is SettingsMessageHandler) … else if (handler is ShortcutMessageHandler)`) to forward `SettingChanged`.

- **Cost today:** every handler must know the router's data structures; adding a settings-emitting handler requires editing the router's if/else chain (Open/Closed violation).
- **Fix:** replace with `IWebMessageHandler { string Prefix; string? Exact; Task HandleAsync(string payload) }` (or similar sealed model); let handlers expose a `SettingsChanged` event through a small interface (`ISettingBroadcaster`) that the router subscribes to generically. Wire the handlers through `EngineDependencies`-style records instead of 6-8 ctor params.
- **Effort:** M. Do together with F5 (same files).

### F7 — Three coexisting service styles
1. DI instance services (stores, `NavigationService`, `ExtensionManager`…) — good.
2. Static-with-state (`ThemeManager` — static class holding theme state + `ThemeChanged` event, initialized in `Composition`; `SingleInstanceManager` static) — works, but stateful statics are invisible to tests and can't be swapped.
3. Static-functional handlers (`TabDialogHandler`, `TabContextMenuHandler`, `TabDownloadHandler` — static methods taking `TabEngine`) — fine, pure-ish, keep.

- **Fix:** convert `ThemeManager` to a DI singleton instance service (4 call sites: `Composition`, `TabEngine` ×2, `MainWindow` ×2, `InternalPages`). Keep stateless statics as-is.
- **Effort:** S. Mechanical.

### F8 — `KeyboardShortcutMap` 17-parameter constructor
Hidden coupling wearing a parameter list. Every new shortcut touches the ctor signature and all tests.

- **Fix:** introduce a `ShortcutActions` aggregate (navigation, tab ops, zoom, TCLens…) — a record the map consumes once. `Services.Input → Engine` dependency also disappears from the handler-facing surface.
- **Effort:** S-M.

### F9 — Service locator in `MainWindow` constructor
`MainWindow` pulls 6 services out of `IServiceProvider` instead of ctor injection (`new MainWindow(_serviceProvider, vm)` in `App.xaml.cs`).

- **Fix:** build the window from DI (`services.GetRequiredService<MainWindow>()`) with `BrowserViewModel` + services in the ctor; `App.xaml.cs` shrinks.
- **Effort:** S. Low priority but removes the locator pattern from the app's only window.

### F10 — Testability gap: navigation policy is buried in event wiring
`TryHandleCustomProtocol`, `TryUpgradeToHttps`, `TryHandleFocusLock`, `NavigationService` URL logic are the browser's actual business rules, but only `NavigationService` is unit-tested. The rest is only exercisable by clicking in a real WebView2.

- **Fix:** as part of F1/F5, extract the policy methods into pure classes with `WebView2`-free signatures and add tests (the test project already has the infrastructure; `IWebView2` seam exists).
- **Effort:** M (opportunistic — do while splitting).

### F11 — Cosmetic leftovers
- Test project renamed `SpurBrowser.Tests` → `StrideBrowser.Tests` (folder + csproj; namespaces were already `StrideBrowser.Tests`). ✅ done 2026-08-17.
- `UpdateZoomIndicator()` was an empty stub — deleted method + delegate + call sites. ✅ done 2026-08-17.
- `App.xaml.cs` had a duplicated `using Microsoft.Extensions.DependencyInjection;` — already gone (stale finding).
- The two empty `LostFocus` handlers are required by XAML bindings — leave them.

- **Effort:** S total.

---

## 4. What's already right (don't touch)

- **Composition root** — single place, eager settings load, interface+concrete registration pairs are deliberate and testable.
- **Store interfaces** — justified by the test project (real seam, two consumers).
- **Web-message pipeline** — IPC token per engine instance verified in `WireWebMessageReceived` before routing; router catches and logs.
- **`IWebView2` seam** — the app already abstracts the webview; keep extending it, don't let raw `CoreWebView2` leak further.
- **Resource-based scripts, signed updates, crash logging, single-instance** — all sound, keep as-is.

---

## 5. Proposed roadmap

| # | Change | Effort | Risk | Do first? |
|---|---|---|---|---|
| 1 | Delete `TabDragDropHandler` (F3) | S | none | yes |
| 2 | TCLens transfer service + clear-on-read (F4) | S-M | low | yes |
| 3 | `ThemeManager` → DI instance (F7) | S | low | yes |
| 4 | Handlers → Engine layer + sealed router contract (F5+F6) | M | medium (build+test between rounds) | after 1-3 |
| 5 | `TabEngine` decomposition in 4 rounds (F1) | L | medium | after 4 |
| 6 | `MainWindow` decomposition (F2) | L | medium | after 5 |
| 7 | `KeyboardShortcutMap` aggregate (F8) | S-M | low | anytime |
| 8 | MainWindow ctor injection (F9) | S | low | anytime |
| 9 | Cosmetic cleanup (F11) | S | none | anytime |

Suggested order: **1 → 2 → 3 → 9 → 4 → 5 → 6** (7/8 slot in whenever), verifying `dotnet build` + `dotnet test` (29 tests) after every step. Rules: never delete the original file until the extraction compiles and tests pass; do the big splits in rounds, not one giant rewrite.

---

## 6. Suggested ADRs to record once decisions are made

- **ADR: message-handler layer placement** (Engine vs Services) — the F5/F6 decision, so it isn't re-litigated.
- **ADR: god-class decomposition strategy** — "splitting TabEngine into named collaborators, not microservices" and the round-based order, so the next person knows why the file list changed.
- **ADR: static-vs-DI policy** — stateless statics allowed; anything with mutable state must be a DI singleton (F7 rule), so future code doesn't reintroduce the third style.

---

*Prepared for review — no changes in this document were applied to the codebase.*
