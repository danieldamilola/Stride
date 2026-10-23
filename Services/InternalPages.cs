using UmberBrowser.Models;
using UmberBrowser.Services.Pages;

namespace UmberBrowser.Services;

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
    private readonly OnboardingPage _onboarding = new();
    private readonly ReleaseNotesPage _releaseNotes = new();
    private readonly HealthPage _health = new();
    private readonly ErrorPage _error = new();
    private readonly ThemeManager _themeManager;

    public InternalPages(ThemeManager themeManager)
    {
        _themeManager = themeManager;
    }

    private string ApplyTheme(string html) => html.Replace("{{THEME}}", _themeManager.GetThemeString());

    /// <summary>Returns the new tab page HTML.</summary>
    public string NewTabPage(List<ShortcutItem> shortcuts, string ipcToken, int zoom, string backgroundPath = "", bool faviconProxyEnabled = true) =>
        ApplyTheme(_newTab.Render(shortcuts, ipcToken, zoom, backgroundPath, faviconProxyEnabled));

    /// <summary>Returns a minimal blank dark page.</summary>
    public string BlankPage() =>
        ApplyTheme("""<html data-theme="{{THEME}}"><body style="background:var(--bg-base)"></body></html>""");

    /// <summary>Returns the OneTab page HTML.</summary>
    public string OneTabPage(List<OneTabGroup> groups, string ipcToken) =>
        ApplyTheme(_oneTab.Render(groups, ipcToken));

    /// <summary>Returns the settings page HTML.</summary>
    public string SettingsPage(BrowserSettings settings, string ipcToken) => ApplyTheme(_settings.Render(settings, ipcToken));

    /// <summary>Returns the history page HTML.</summary>
    public string HistoryPage(List<HistoryEntry> entries, string ipcToken, bool faviconProxyEnabled = true) =>
        ApplyTheme(_history.Render(entries, ipcToken, faviconProxyEnabled));

    /// <summary>Returns the downloads page HTML.</summary>
    public string DownloadsPage(List<DownloadItem> items, string ipcToken) =>
        ApplyTheme(_downloads.Render(items, ipcToken));

    /// <summary>Returns the onboarding page HTML.</summary>
    public string OnboardingPage(BrowserSettings settings, string ipcToken) =>
        ApplyTheme(_onboarding.Render(settings, ipcToken));

    /// <summary>Returns the release notes page HTML.</summary>
    public string ReleaseNotesPage(string currentVersion, IReadOnlyList<Engine.ReleaseNotesProvider.ReleaseVersion> releases, string ipcToken) =>
        ApplyTheme(_releaseNotes.Render(currentVersion, releases, ipcToken));

    /// <summary>Returns the error page HTML.</summary>
    public string ErrorPage(string url, string errorMessage) =>
        ApplyTheme(_error.Render(url, errorMessage));

    /// <summary>Returns the focus block page HTML.</summary>
    public string FocusPage(string blockedHost = "", string ipcToken = "") => ApplyTheme(Helpers.ResourceLoader.LoadTemplate("Resources.Pages.Focus.html", new Dictionary<string, string> { ["BLOCKED_HOST"] = System.Net.WebUtility.HtmlEncode(blockedHost), ["IPC_TOKEN"] = ipcToken }));

    /// <summary>Returns the health page HTML.</summary>
    public string HealthPage(string currentVersion, ExtensionManager.ExtensionHealth health, string ipcToken) =>
        ApplyTheme(_health.Render(currentVersion, health, ipcToken));

}
