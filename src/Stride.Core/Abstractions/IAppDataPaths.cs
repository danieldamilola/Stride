namespace StrideBrowser.Abstractions;

public interface IAppDataPaths
{
    string DataDirectory { get; }
    string SettingsFile { get; }
    string HistoryFile { get; }
    string OneTabFile { get; }
    string SessionFile { get; }
    string FaviconCacheDir { get; }
    string ExtensionsDir { get; }
    string WebViewDataDir { get; }
    string LogFile { get; }
    string CrashLogFile { get; }
    string UBlockHashFile { get; }
    string FocusCacheDir { get; }
    void EnsureDirectories();
}
