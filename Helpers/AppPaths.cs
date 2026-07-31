using System.IO;

namespace StrideBrowser.Helpers;

/// <summary>
/// Provides standard paths for Stride's data files.
/// All files are stored in %LocalAppData%\StrideBrowser.
/// </summary>
public static class AppPaths
{
    private static readonly string Base = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StrideBrowser");

    public static string DataDirectory => Base;

    public static string SettingsFile => Path.Combine(Base, "settings.json");
    public static string HistoryFile => Path.Combine(Base, "history.json");
    public static string OneTabFile => Path.Combine(Base, "onetab.json");
    public static string SessionFile => Path.Combine(Base, "session.json");
    public static string DownloadsFile => Path.Combine(Base, "downloads.json");
    public static string FaviconCacheDir => Path.Combine(Base, "favicons");
    public static string ExtensionsDir => Path.Combine(Base, "extensions");
    public static string WebViewDataDir => Path.Combine(Base, "WebView2");
    public static string WebView2Dir => WebViewDataDir;
    public static string LogFile => Path.Combine(Base, "stride.log");
    public static string CrashLogFile => Path.Combine(Base, "crash.log");
    public static string UBlockHashFile => Path.Combine(Base, "ublock.sha256");
    public static string FocusCacheDir => Path.Combine(Base, "focus_cache");

    /// <summary>Ensures the data directory exists.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(FaviconCacheDir);
        if (!Directory.Exists(ExtensionsDir)) Directory.CreateDirectory(ExtensionsDir);
        if (!Directory.Exists(WebViewDataDir)) Directory.CreateDirectory(WebViewDataDir);
        if (!Directory.Exists(FocusCacheDir)) Directory.CreateDirectory(FocusCacheDir);
    }
}
