using StrideBrowser.Models;

namespace StrideBrowser.Abstractions;

public interface IInternalPageHost
{
    string NewTabPage(List<ShortcutItem> shortcuts, string accentColor, string accentRgb, string ipcToken, int zoom, string backgroundPath = "");
    string BlankPage();
    string OneTabPage(List<OneTabGroup> groups, string accentColor, string accentRgb, string ipcToken);
    string SettingsPage(BrowserSettings settings, string ipcToken);
    string HistoryPage(List<HistoryEntry> entries, string accentColor, string accentRgb, string ipcToken);
    string DownloadsPage(List<DownloadItem> items, string accentColor, string accentRgb, string ipcToken);
    string ErrorPage(string url, string errorMessage, string accentColor, string accentRgb);
    string FocusPage();
}
