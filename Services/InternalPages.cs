using StrideBrowser.Models;
using StrideBrowser.Services.Pages;

namespace StrideBrowser.Services;

/// <summary>
/// Thin facade over the per-page generators.
/// Kept for backward compatibility with TabEngine's existing API.
/// </summary>
public sealed class InternalPages
{
    private readonly NewTabPage _newTab = new();
    private readonly OneTabPage _oneTab = new();
    private readonly SettingsPage _settings = new();
    private readonly HistoryPage _history = new();
    private readonly DownloadPage _downloads = new();
    private readonly ErrorPage _error = new();

    /// <summary>Returns the new tab page HTML.</summary>
    public string NewTabPage(List<ShortcutItem> shortcuts, string accentColor, string accentRgb, string ipcToken, string backgroundPath = "") =>
        _newTab.Render(shortcuts, accentColor, accentRgb, ipcToken, backgroundPath);

    /// <summary>Returns a minimal blank dark page.</summary>
    public string BlankPage() =>
        """<html><body style="background:#101012"></body></html>""";

    /// <summary>Returns the OneTab page HTML.</summary>
    public string OneTabPage(List<OneTabGroup> groups, string accentColor, string accentRgb, string ipcToken) =>
        _oneTab.Render(groups, accentColor, accentRgb, ipcToken);

    /// <summary>Returns the settings page HTML.</summary>
    public string SettingsPage(BrowserSettings settings, string ipcToken) => _settings.Render(settings, ipcToken);

    /// <summary>Returns the history page HTML.</summary>
    public string HistoryPage(List<HistoryEntry> entries, string accentColor, string accentRgb, string ipcToken) =>
        _history.Render(entries, accentColor, accentRgb, ipcToken);

    /// <summary>Returns the downloads page HTML.</summary>
    public string DownloadsPage(List<DownloadItem> items, string accentColor, string accentRgb, string ipcToken) =>
        _downloads.Render(items, accentColor, accentRgb, ipcToken);

    /// <summary>Returns the error page HTML.</summary>
    public string ErrorPage(string url, string errorMessage, string accentColor, string accentRgb) =>
        _error.Render(url, errorMessage, accentColor, accentRgb);

    /// <summary>Returns the focus block page HTML.</summary>
    public string FocusPage() => Helpers.ResourceLoader.Load("Resources.Pages.Focus.html");

    /// <summary>Converts a hex color like #D4A574 to an RGB triplet like 212,165,116.</summary>
    public static string HexToRgb(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return "212,165,116";
        var r = Convert.ToInt32(hex[..2], 16);
        var g = Convert.ToInt32(hex[2..4], 16);
        var b = Convert.ToInt32(hex[4..6], 16);
        return $"{r},{g},{b}";
    }
}
