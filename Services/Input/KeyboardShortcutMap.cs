using System.Windows.Input;
using SpurBrowser.Engine;

namespace SpurBrowser.Services.Input;

/// <summary>
/// Declarative keyboard shortcut table. Maps (modifiers, key) pairs
/// to named actions, replacing the ~175-line OnKeyDown cascade.
/// </summary>
public sealed class KeyboardShortcutMap
{
    /// <summary>Describes a single keyboard shortcut binding.</summary>
    public record ShortcutEntry(ModifierKeys Modifiers, Key Key, string Name, Func<Task> Action);

    private readonly List<ShortcutEntry> _entries;

    public KeyboardShortcutMap(
        TabEngine engine,
        Func<Task> focusAddressBar,
        Func<Task> saveAllTabs,
        Func<bool, Task> cycleTab,
        Func<Task> toggleFullscreen,
        Func<bool> isFullscreen,
        Func<Task> updateZoomIndicator,
        Func<Task> openHistory,
        Func<int, Task> switchToTabIndex,
        Action<string> copyUrl,
        Func<List<(string url, string title)>> sendAllToOneTab,
        Action<List<(string url, string title)>> saveOneTabGroup,
        Action syncTabsBinding)
    {
        var ctrl = ModifierKeys.Control;
        var ctrlShift = ModifierKeys.Control | ModifierKeys.Shift;
        var alt = ModifierKeys.Alt;
        var none = ModifierKeys.None;

        _entries =
        [
            new(ctrlShift, Key.T, "RestoreClosedTab", async () =>
            {
                var tab = engine.RestoreClosedTab();
                if (tab is not null) { engine.SwitchTo(tab); await engine.ActivateAsync(tab); }
            }),
            new(ctrl, Key.T, "NewTab", async () =>
            {
                var tab = engine.CreateTab();
                engine.SwitchTo(tab); await engine.ActivateAsync(tab);
                await focusAddressBar();
            }),
            new(ctrl, Key.W, "CloseTab", () =>
            {
                if (engine.ActiveTab is not null && !engine.ActiveTab.IsPinned)
                    engine.CloseTab(engine.ActiveTab);
                return Task.CompletedTask;
            }),
            new(ctrl, Key.L, "FocusAddressBar", focusAddressBar),
            new(ctrlShift, Key.D1, "SendAllToOneTab", () =>
            {
                var entries = sendAllToOneTab();
                if (entries.Count > 0) saveOneTabGroup(entries);
                syncTabsBinding();
                return Task.CompletedTask;
            }),
            new(ctrlShift, Key.S, "SaveAllTabs", saveAllTabs),
            new(none, Key.F5, "Reload", () => { engine.Reload(); return Task.CompletedTask; }),
            new(alt, Key.Left, "GoBack", () => { engine.GoBack(); return Task.CompletedTask; }),
            new(alt, Key.Right, "GoForward", () => { engine.GoForward(); return Task.CompletedTask; }),
            new(none, Key.F11, "ToggleFullscreen", toggleFullscreen),
            new(none, Key.Escape, "ExitFullscreen", async () =>
            {
                if (isFullscreen()) await toggleFullscreen();
            }),
            new(ctrl, Key.Tab, "CycleTabForward", () => cycleTab(false)),
            new(ctrlShift, Key.Tab, "CycleTabBackward", () => cycleTab(true)),
            new(ctrl, Key.F, "FindInPage", async () => await engine.FindInPageAsync()),
            new(ctrl, Key.P, "Print", () => { engine.Print(); return Task.CompletedTask; }),
            new(ctrl, Key.OemPlus, "ZoomIn", async () => { engine.Zoom(0.1); await updateZoomIndicator(); }),
            new(ctrl, Key.Add, "ZoomInNumpad", async () => { engine.Zoom(0.1); await updateZoomIndicator(); }),
            new(ctrl, Key.OemMinus, "ZoomOut", async () => { engine.Zoom(-0.1); await updateZoomIndicator(); }),
            new(ctrl, Key.Subtract, "ZoomOutNumpad", async () => { engine.Zoom(-0.1); await updateZoomIndicator(); }),
            new(ctrl, Key.D0, "ResetZoom", async () => { engine.ResetZoom(); await updateZoomIndicator(); }),
            new(ctrl, Key.NumPad0, "ResetZoomNumpad", async () => { engine.ResetZoom(); await updateZoomIndicator(); }),
            new(ctrlShift, Key.C, "CopyUrl", () =>
            {
                var url = engine.ActiveTab?.Url;
                if (!string.IsNullOrEmpty(url)) copyUrl(url);
                return Task.CompletedTask;
            }),
            new(ctrlShift, Key.F12, "StressTest", async () => await new StressTestRunner(engine).RunAsync()),
            new(none, Key.F12, "DevTools", () =>
            {
                engine.GetCoreWebView2()?.OpenDevToolsWindow();
                return Task.CompletedTask;
            }),
            new(ctrl, Key.H, "History", openHistory),
        ];
    }

    public IReadOnlyList<ShortcutEntry> Entries => _entries;

    public async Task<bool> TryExecuteAsync(ModifierKeys modifiers, Key key)
    {
        if (modifiers == ModifierKeys.Control && key >= Key.D1 && key <= Key.D9)
            return false;

        foreach (var entry in _entries)
        {
            if (entry.Key == key && MatchModifiers(entry.Modifiers, modifiers))
            {
                await entry.Action();
                return true;
            }
        }
        return false;
    }

    private static bool MatchModifiers(ModifierKeys expected, ModifierKeys actual)
    {
        if (expected == ModifierKeys.None)
            return actual == ModifierKeys.None;
        return actual == expected;
    }
}
