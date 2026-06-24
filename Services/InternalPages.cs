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
    private readonly ErrorPage _error = new();

    /// <summary>Returns the new tab page HTML.</summary>
    public string NewTabPage(string backgroundPath = "") => _newTab.Render(backgroundPath);

    /// <summary>Returns a minimal blank dark page.</summary>
    public string BlankPage() =>
        """<html><body style="background:#101012"></body></html>""";

    /// <summary>Returns the OneTab page HTML.</summary>
    public string OneTabPage(List<OneTabGroup> groups) => _oneTab.Render(groups);

    /// <summary>Returns the settings page HTML.</summary>
    public string SettingsPage(BrowserSettings settings) => _settings.Render(settings);

    /// <summary>Returns the history page HTML.</summary>
    public string HistoryPage(List<HistoryEntry> entries) => _history.Render(entries);

    /// <summary>Returns the error page HTML.</summary>
    public string ErrorPage(string url, string errorMessage) => _error.Render(url, errorMessage);
}
