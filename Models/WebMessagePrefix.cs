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
    public const string DownloadOpen = "download-open:";
    public const string DownloadFolder = "download-folder:";
    public const string DownloadCancel = "download-cancel:";
    public const string DownloadClear = "download-clear";
    public const string DownloadClearAll = "download-clear-all";
    public const string DownloadRequestSync = "download-sync";
    public const string DownloadRequest = "download-request:";
    public const string DownloadPause = "download-pause:";
    public const string DownloadResume = "download-resume:";
    public const string FocusSettings = "focus-settings";
    public const string TCLensGetText = "tclens-get-text:";
    public const string OpenBackgroundsFolder = "open-backgrounds-folder";
    public const string CheckForUpdate = "check-for-update";
    public const string InstallUpdate = "install-update";
    public const string ResetSettings = "reset-settings";
}
