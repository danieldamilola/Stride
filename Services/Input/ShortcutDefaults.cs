namespace StrideBrowser.Services.Input;

/// <summary>
/// Defines all rebindable keyboard shortcuts with their default key combos,
/// display names, and category groupings for the Settings UI.
/// </summary>
public static class ShortcutDefaults
{
    public record ShortcutDef(string Name, string Category, string Label, string Description, string DefaultCombo);

    public static readonly ShortcutDef[] All =
    [
        // ── Tabs ──
        new("NewTab",            "Tabs",       "New tab",              "Open a new tab",                    "Ctrl+T"),
        new("CloseTab",          "Tabs",       "Close tab",            "Close the current tab",             "Ctrl+W"),
        new("RestoreClosedTab",  "Tabs",       "Restore closed tab",   "Reopen last closed tab",            "Ctrl+Shift+T"),
        new("CycleTabForward",   "Tabs",       "Next tab",             "Switch to next tab",                "Ctrl+Tab"),
        new("CycleTabBackward",  "Tabs",       "Previous tab",         "Switch to previous tab",            "Ctrl+Shift+Tab"),

        // ── Navigation ──
        new("FocusAddressBar",   "Navigation", "Address bar",          "Focus the command bar",             "Ctrl+L"),
        new("GoBack",            "Navigation", "Go back",              "Navigate back",                     "Alt+Left"),
        new("GoForward",         "Navigation", "Go forward",           "Navigate forward",                  "Alt+Right"),
        new("Reload",            "Navigation", "Reload",               "Reload current page",               "F5"),

        // ── View ──
        new("ZoomIn",            "View",       "Zoom in",              "Increase page zoom",                "Ctrl+="),
        new("ZoomOut",           "View",       "Zoom out",             "Decrease page zoom",                "Ctrl+-"),
        new("ResetZoom",         "View",       "Reset zoom",           "Reset to default zoom",             "Ctrl+0"),
        new("ToggleFullscreen",  "View",       "Fullscreen",           "Toggle fullscreen mode",            "F11"),

        // ── Tools ──
        new("FindInPage",        "Tools",      "Find in page",         "Search within the page",            "Ctrl+F"),
        new("Print",             "Tools",      "Print",                "Print the page",                    "Ctrl+P"),
        new("CopyUrl",           "Tools",      "Copy URL",             "Copy current page URL",             "Ctrl+Shift+C"),
        new("History",           "Tools",      "History",              "Open browsing history",              "Ctrl+H"),
        new("Downloads",         "Tools",      "Downloads",            "Open downloads",                    "Ctrl+J"),
        new("OpenOneTab",        "Tools",      "OneTab",               "Open saved tabs",                   "Ctrl+Shift+O"),
        new("SendAllToOneTab",   "Tools",      "Send all to OneTab",   "Save and close all tabs",           "Ctrl+Shift+1"),
        new("SaveAllTabs",       "Tools",      "Save all tabs",        "Save session",                      "Ctrl+Shift+S"),
        new("OpenSettings",      "Tools",      "Settings",             "Open settings page",                "Ctrl+,"),
        new("DevTools",          "Tools",      "Developer tools",      "Open DevTools",                     "F12"),
    ];

    /// <summary>Gets the effective combo for an action, using custom override if set.</summary>
    public static string GetCombo(string actionName, Dictionary<string, string>? customShortcuts)
    {
        if (customShortcuts is not null && customShortcuts.TryGetValue(actionName, out var custom))
            return custom;
        var def = Array.Find(All, d => d.Name == actionName);
        return def?.DefaultCombo ?? "";
    }

    /// <summary>Gets the default combo for an action.</summary>
    public static string GetDefault(string actionName)
    {
        var def = Array.Find(All, d => d.Name == actionName);
        return def?.DefaultCombo ?? "";
    }
}
