# Fullscreen Fix

How we fixed video fullscreen in Stride: the taskbar stayed visible, a thin
light border showed around the media, and the window state got stuck after
exiting fullscreen. This document covers the symptoms, the root causes, every
file touched, and how the new fullscreen path works.

Date: 2026-08-27
Branch: feature/v1.2.1-update-ui

---

## Symptoms

1. Clicking the fullscreen button on any video maximized the window above the
   taskbar instead of covering it. The taskbar stayed on screen.
2. A thin white line framed the media in fullscreen. It looked like a border
   showing what sits behind the window.
3. Exiting video fullscreen left the window maximized even if it was windowed
   before the video started.
4. Pressing F11 while a video was fullscreen desynced the toolbar and window
   state, because F11 and video fullscreen tracked state in two independent
   places.

## Root causes

### 1. Maximize is bounded by the work area

Entering video fullscreen collapsed the toolbar and set
`WindowState = Maximized`. With `WindowStyle="None"`, maximize bounds are
decided by the `WM_GETMINMAXINFO` handler in `WindowChromeManager`. That
handler sizes maximized bounds to `rcWork` (the monitor minus the taskbar),
which is correct for the maximize button but wrong for fullscreen. Fullscreen
needs `rcMonitor`, the full monitor rectangle. The first fix attempt flipped a
flag in the `WM_GETMINMAXINFO` handler to swap in `rcMonitor` during maximize.
That did not work reliably, so the final fix stopped using maximize for
fullscreen entirely.

### 2. DWM chrome draws edges around the window

The app turns on rounded corners with `DwmSetWindowAttribute`
(`DWMWA_WINDOW_CORNER_PREFERENCE = ROUND`) and uses `WindowChrome` with
`GlassFrameThickness="0"`. During fullscreen the DWM border and the rounded
corner treatment produced the thin light line around the video, with the window
background visible at the edges.

### 3. No restore path on exit

The video fullscreen exit handler showed the toolbar and re-enabled resizing
but never restored the previous window bounds or state. A windowed browser
stayed maximized after the video ended.

### 4. Two independent state machines

`MainWindow` kept a private `_isFullscreen` flag touched only by the F11 toggle.
WebView2's `ContainsFullScreenElementChanged` event drove a separate inline
handler that never updated that flag. The two paths fought each other. A dead
duplicate `ToggleFullscreen` also lived in `WindowLifecycleController`.

---

## The fix

Fullscreen no longer maximizes the window. The window is sized natively to the
exact physical monitor rectangle with `SetWindowPos`, which covers the taskbar
by definition. DWM rounded corners and the border color are suppressed while
fullscreen is active. One controller owns the fullscreen state for both F11 and
video events.

### Fullscreen flow

Enter, triggered by the video's fullscreen button or F11:

1. WebView2 fires `ContainsFullScreenElementChanged`, or the user presses F11.
2. `FullscreenController` runs the transition and captures
   `Window.RestoreBounds`, the bounds to restore on exit.
3. `WindowChromeManager.EnterMonitorFullscreen` sets `WindowState = Normal`,
   calls `SetWindowPos` with the monitor rectangle from `GetMonitorInfo`
   (`rcMonitor`), and applies fullscreen chrome: corners `DONOTROUND`, border
   color `NONE`.
4. Toolbar collapses, `ResizeMode` goes to `NoResize`.

If the native sizing fails, the controller falls back to `WindowState =
Maximized` so fullscreen still engages.

Exit, triggered by Esc, the video's exit button, or F11:

1. `FullscreenController` sees the state change.
2. `WindowChromeManager.ExitMonitorFullscreen` restores rounded corners and the
   default border, then calls `SetWindowPos` with the saved bounds, converted
   from WPF DIPs to physical pixels using the window's DPI transform.
3. If the window was maximized before the video, it re-maximizes. Otherwise it
   sits exactly where it was.
4. Toolbar returns, `ResizeMode` goes back to `CanResize`.

---

## Files changed

### New: Services/UI/FullscreenController.cs

Owns all fullscreen state. Three types live here:

- `FullscreenState`: immutable record holding `IsActive` and the
  `SavedWindowState` to restore.
- `FullscreenTransitions`: pure static transitions (`Enter`, `Exit`, `Toggle`)
  with no WPF window dependency, so the state machine is unit-testable.
  Repeated transitions are no-ops, which protects against WebView2 firing the
  event multiple times.
- `FullscreenController`: applies transitions to the real window. Public
  surface is `Toggle()` for F11, `SetFullscreen(bool)` for video events, and
  `IsFullscreen` for the keyboard shortcut map. `SetFullscreen` is idempotent.
  On enter it captures `RestoreBounds`, calls into `WindowChromeManager`, and
  collapses the toolbar. On exit it restores bounds, re-maximizes when needed,
  and brings the toolbar back.

### Edited: Services/WindowChromeManager.cs

Added the native fullscreen methods:

- `EnterMonitorFullscreen(Rect dipRestoreBounds)`: resolves the nearest monitor
  with `MonitorFromWindow`, reads `rcMonitor` with `GetMonitorInfo`, forces
  `WindowState = Normal`, and calls `SetWindowPos` with `SWP_NOZORDER`,
  `SWP_NOACTIVATE`, and `SWP_FRAMECHANGED` to place the window exactly over the
  monitor. Returns false on failure so the caller can fall back to maximize.
- `ExitMonitorFullscreen()`: restores DWM chrome, converts the saved DIP bounds
  to pixels via `PresentationSource.CompositionTarget.TransformToDevice`, and
  `SetWindowPos` back.
- `ApplyFullscreenChrome(bool)`: flips `DWMWA_WINDOW_CORNER_PREFERENCE`
  between `DONOTROUND` and `ROUND`, and `DWMWA_BORDER_COLOR` between `NONE`
  and `DEFAULT`. This is what removes the light line around the video.

The `WM_GETMINMAXINFO` handler is unchanged: normal maximize still respects the
work area and auto-hide taskbar edges.

### Edited: MainWindow.xaml.cs

- Removed the private `_isFullscreen` and `_preFullscreenState` fields and the
  private `ToggleFullscreen` method.
- The constructor creates one `FullscreenController` bound to the window and
  the toolbar.
- `OnSourceInitialized` hands the controller the `WindowChromeManager` after
  the native handle exists.
- `_engine.FullScreenChanged` is now a one-liner calling
  `_fullscreenController.SetFullscreen(isFullScreen)`.
- The F11 entry in `BuildShortcutMap` calls `_fullscreenController.Toggle()`
  and reads `_fullscreenController.IsFullscreen`.

### Edited: Services/UI/WindowLifecycleController.cs

Removed the dead duplicate fullscreen state: `_isFullscreen`,
`_preFullscreenState`, the `IsFullscreen` property, and the `ToggleFullscreen`
method. Nothing referenced them. The class was already unused; the duplicates
are gone so they cannot drift back in.

### Edited: Interop/NativeMethods.cs

Added constants: `DWMWA_BORDER_COLOR` (34), `DWMWCP_DONOTROUND` (1),
`DWMWA_COLOR_DEFAULT` (0xFFFFFFFF), `DWMWA_COLOR_NONE` (0xFFFFFFFE), `SWP_NOZORDER` (0x0004),
`SWP_FRAMECHANGED` (0x0020). `DwmSetWindowAttribute`, `SetWindowPos`,
`MonitorFromWindow`, and `GetMonitorInfo` already existed.

### Edited: Stride.csproj

Added `<Compile Remove="dump.cs" />`. `dump.cs` is an untracked scratch file
with its own `Main` that broke the build with CS0017, duplicate entry point.
The file stays on disk; it is just excluded from compilation.

### Edited: ReleaseNotes.md

Added a Bug Fixes entry under v1.2.1 covering the fullscreen fix.

### New: StrideBrowser.Tests/FullscreenTransitionsTests.cs

Ten tests over the pure state machine:

- Enter from windowed and from maximized saves the right state
- Repeated enter is a no-op and keeps the first saved state
- Exit restores the saved state and deactivates
- Exit when not active is a no-op
- Toggle round trips in and out
- F11 while video fullscreen exits once and keeps the saved window state
- Duplicate video events cannot overwrite the saved window state

---

## Verification

- `dotnet build Stride.csproj -c Debug`: 0 errors.
- `dotnet test StrideBrowser.Tests/StrideBrowser.Tests.csproj`: 202 passed,
  0 failed, 8 skipped.
- App launched from `dotnet run` and stayed alive with its window up.
- Manual check performed by hand: video fullscreen covers the taskbar with no
  edge line, and Esc restores the previous window bounds.

## Known limits

- Multi-monitor: fullscreen targets the nearest monitor to the window, which
  matches Chrome behavior.
- DPI changes while fullscreen is active are not tracked. Exit converts the
  saved DIP bounds with the DPI at exit time.
- If the window is minimized when a video requests fullscreen, the saved bounds
  come from `RestoreBounds`, which still holds the pre-minimize geometry.
