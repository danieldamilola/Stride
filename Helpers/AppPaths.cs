using System.IO;

namespace SpurBrowser.Helpers;

/// <summary>
/// Provides standard paths for Spur's data files.
/// All files are stored in %LocalAppData%\SpurBrowser.
/// </summary>
public static class AppPaths
{
    private static readonly string Base = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpurBrowser");

    public static string DataDirectory => Base;

    public static string SettingsFile => Path.Combine(Base, "settings.json");
    public static string HistoryFile => Path.Combine(Base, "history.json");
    public static string OneTabFile => Path.Combine(Base, "onetab.json");
    public static string SessionFile => Path.Combine(Base, "session.json");
    public static string FaviconCacheDir => Path.Combine(Base, "favicons");
    public static string ExtensionsDir => Path.Combine(Base, "extensions");
    public static string WebViewDataDir => Path.Combine(Base, "WebView2");
    public static string WebView2Dir => WebViewDataDir;
    public static string LogFile => Path.Combine(Base, "spur.log");
    public static string CrashLogFile => Path.Combine(Base, "crash.log");
    public static string UBlockHashFile => Path.Combine(Base, "ublock.sha256");

    /// <summary>Ensures the data directory exists.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(FaviconCacheDir);
        Directory.CreateDirectory(ExtensionsDir);
    }
}
