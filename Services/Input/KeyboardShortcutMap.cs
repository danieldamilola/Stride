using System.Windows.Input;
using StrideBrowser.Engine;

namespace StrideBrowser.Services.Input;

/// <summary>
/// Declarative keyboard shortcut table. Maps (modifiers, key) pairs
/// to named actions, replacing the ~175-line OnKeyDown cascade.
/// Supports runtime rebinding from user settings.
/// </summary>
public sealed class KeyboardShortcutMap
{
    /// <summary>Describes a single keyboard shortcut binding.</summary>
    public record ShortcutEntry(ModifierKeys Modifiers, Key Key, string Name, Func<Task> Action);

    private readonly Dictionary<string, Func<Task>> _actions = new();
    private List<ShortcutEntry> _entries = [];

    public KeyboardShortcutMap(TabEngine engine, ShortcutActions actions)
    {
        // Register all actions by name (these never change, only the key bindings do)
        _actions["TCLens"] = actions.LaunchTCLens;
        _actions["RestoreClosedTab"] = async () =>
        {
            var tab = engine.RestoreClosedTab();
            if (tab is not null) { engine.SwitchTo(tab); await engine.ActivateAsync(tab); }
        };
        _actions["NewTab"] = async () =>
        {
            var tab = engine.CreateTab();
            engine.SwitchTo(tab); await engine.ActivateAsync(tab);
            await actions.FocusAddressBar();
        };
        _actions["CloseTab"] = () =>
        {
            if (engine.ActiveTab is not null && !engine.ActiveTab.IsPinned)
                engine.CloseTab(engine.ActiveTab);
            return Task.CompletedTask;
        };
        _actions["FocusAddressBar"] = actions.FocusAddressBar;
        _actions["SendAllToOneTab"] = () =>
        {
            var entries = actions.SendAllToOneTab();
            if (entries.Count > 0) actions.SaveOneTabGroup(entries);
            actions.SyncTabsBinding();
            return Task.CompletedTask;
        };
        _actions["SaveAllTabs"] = actions.SaveAllTabs;
        _actions["Reload"] = () => { engine.Reload(); return Task.CompletedTask; };
        _actions["GoBack"] = () => { engine.GoBack(); return Task.CompletedTask; };
        _actions["GoForward"] = () => { engine.GoForward(); return Task.CompletedTask; };
        _actions["ToggleFullscreen"] = actions.ToggleFullscreen;
        _actions["ExitFullscreen"] = async () =>
        {
            if (actions.IsFullscreen()) await actions.ToggleFullscreen();
        };
        _actions["CycleTabForward"] = () => actions.CycleTab(false);
        _actions["CycleTabBackward"] = () => actions.CycleTab(true);
        _actions["FindInPage"] = async () => await engine.FindInPageAsync();
        _actions["Print"] = () => { engine.Print(); return Task.CompletedTask; };
        _actions["ZoomIn"] = async () => { engine.Zoom(0.1); await actions.UpdateZoomIndicator(); };
        _actions["ZoomOut"] = async () => { engine.Zoom(-0.1); await actions.UpdateZoomIndicator(); };
        _actions["ResetZoom"] = async () => { engine.ResetZoom(); await actions.UpdateZoomIndicator(); };
        _actions["CopyUrl"] = () =>
        {
            var url = engine.ActiveTab?.Url;
            if (!string.IsNullOrEmpty(url)) actions.CopyUrl(url);
            return Task.CompletedTask;
        };
        _actions["DevTools"] = () =>
        {
            engine.GetCoreWebView2()?.OpenDevToolsWindow();
            return Task.CompletedTask;
        };
        _actions["History"] = actions.OpenHistory;
        _actions["Downloads"] = actions.OpenDownloads;
        _actions["OpenOneTab"] = actions.OpenOneTab;
        _actions["OpenSettings"] = actions.OpenSettings;

        // Build default bindings
        RebuildBindings(null);
    }

    public IReadOnlyList<ShortcutEntry> Entries => _entries;

    /// <summary>
    /// Rebuilds the key binding table using custom overrides from settings.
    /// Call this when the user changes a shortcut binding.
    /// </summary>
    public void RebuildBindings(Dictionary<string, string>? customShortcuts)
    {
        var entries = new List<ShortcutEntry>();

        foreach (var def in ShortcutDefaults.All)
        {
            if (!_actions.TryGetValue(def.Name, out var action)) continue;
            var combo = ShortcutDefaults.GetCombo(def.Name, customShortcuts);
            if (TryParseCombo(combo, out var mods, out var key))
            {
                entries.Add(new ShortcutEntry(mods, key, def.Name, action));

                // Add numpad duplicates for zoom shortcuts
                if (def.Name == "ZoomIn" && key == Key.OemPlus)
                    entries.Add(new ShortcutEntry(mods, Key.Add, "ZoomInNumpad", action));
                if (def.Name == "ZoomOut" && key == Key.OemMinus)
                    entries.Add(new ShortcutEntry(mods, Key.Subtract, "ZoomOutNumpad", action));
                if (def.Name == "ResetZoom" && key == Key.D0)
                    entries.Add(new ShortcutEntry(mods, Key.NumPad0, "ResetZoomNumpad", action));
            }
        }

        // Always add non-rebindable Escape handler
        if (_actions.TryGetValue("ExitFullscreen", out var escAction))
            entries.Add(new ShortcutEntry(ModifierKeys.None, Key.Escape, "ExitFullscreen", escAction));

        _entries = entries;
    }

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

    /// <summary>Parses a combo string like "Ctrl+Shift+T" into ModifierKeys and Key.</summary>
    public static bool TryParseCombo(string combo, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(combo)) return false;

        var parts = combo.Split('+');
        var keyPart = parts[^1].Trim();

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var mod = parts[i].Trim();
            if (mod.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Control;
            else if (mod.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Shift;
            else if (mod.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Alt;
        }

        // Map special key names
        key = keyPart switch
        {
            "Tab" => Key.Tab,
            "Left" => Key.Left,
            "Right" => Key.Right,
            "Up" => Key.Up,
            "Down" => Key.Down,
            "=" or "+" => Key.OemPlus,
            "-" => Key.OemMinus,
            "," => Key.OemComma,
            "." => Key.OemPeriod,
            "/" => Key.Oem2,
            "\\" => Key.Oem5,
            "[" => Key.OemOpenBrackets,
            "]" => Key.Oem6,
            ";" => Key.OemSemicolon,
            "'" => Key.OemQuotes,
            "`" => Key.OemTilde,
            "Space" => Key.Space,
            "Enter" => Key.Return,
            "Backspace" => Key.Back,
            "Delete" => Key.Delete,
            "Home" => Key.Home,
            "End" => Key.End,
            "PageUp" => Key.PageUp,
            "PageDown" => Key.PageDown,
            "F1" => Key.F1, "F2" => Key.F2, "F3" => Key.F3, "F4" => Key.F4,
            "F5" => Key.F5, "F6" => Key.F6, "F7" => Key.F7, "F8" => Key.F8,
            "F9" => Key.F9, "F10" => Key.F10, "F11" => Key.F11, "F12" => Key.F12,
            "1" => Key.D1, "2" => Key.D2, "3" => Key.D3, "4" => Key.D4,
            "5" => Key.D5, "6" => Key.D6, "7" => Key.D7, "8" => Key.D8,
            "9" => Key.D9, "0" => Key.D0,
            _ => ParseLetterKey(keyPart)
        };

        return key != Key.None;
    }

    /// <summary>Converts a (ModifierKeys, Key) pair back to a display string like "Ctrl+Shift+T".</summary>
    public static string ToComboString(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(KeyToString(key));
        return string.Join("+", parts);
    }

    private static Key ParseLetterKey(string s)
    {
        if (s.Length == 1 && char.IsLetter(s[0]))
            return (Key)(Key.A + (char.ToUpper(s[0]) - 'A'));
        return Key.None;
    }

    private static string KeyToString(Key key) => key switch
    {
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Oem2 => "/",
        Key.Oem5 => "\\",
        Key.OemOpenBrackets => "[",
        Key.Oem6 => "]",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemTilde => "`",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.Space => "Space",
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.A and <= Key.Z => key.ToString(),
        _ => key.ToString()
    };

    private static bool MatchModifiers(ModifierKeys expected, ModifierKeys actual)
    {
        if (expected == ModifierKeys.None)
            return actual == ModifierKeys.None;
        return actual == expected;
    }
}
