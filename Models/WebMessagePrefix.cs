namespace StrideBrowser.Models;

/// <summary>Constants for web message prefixes sent from internal pages via postMessage.</summary>
public static class WebMessagePrefix
{
    public const string Open = "open:";
    public const string Search = "search:";
    public const string Setting = "setting:";
    public const string OneTabRestore = "onetab-restore:";
    public const string OneTabDelete = "onetab-delete:";
    public const string OneTabRename = "onetab-rename:";
    public const string OneTabOpen = "onetab-open:";
    public const string OneTabDeleteTab = "onetab-delete-tab:";
    public const string OneTabStar = "onetab-star:";
    public const string OneTabReorderTab = "onetab-reorder-tab:";
    public const string OneTabReorderGroup = "onetab-reorder-group:";
    public const string HistoryOpen = "history-open:";
    public const string HistoryClear = "history-clear";
    public const string SetDefaultBrowser = "set-default-browser";
    public const string ShortcutAdd = "shortcut-add:";
    public const string ShortcutRemove = "shortcut-remove:";
    public const string ShortcutClick = "shortcut-click:";
}
