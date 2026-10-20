using Microsoft.Web.WebView2.Core;

namespace StrideBrowser.Services;

/// <summary>State shared by browser windows for the lifetime of the process.</summary>
public sealed class BrowserSession
{
    public string WebViewDataDirectory { get; init; } = Helpers.AppPaths.WebView2Dir;

    /// <summary>
    /// One token for the whole process, not one per window. Internal pages refuse
    /// host messages that do not carry it, so a tab torn into another window has to
    /// keep a token the new window also knows.
    /// </summary>
    public string IpcToken { get; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Dedicated token for external page telemetry (theme color and link preview).
    /// External scripts receive this token and cannot use it to authenticate privileged commands.
    /// </summary>
    public string TelemetryToken { get; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Native download operations keyed by id. Shared so a download started in one
    /// window can still be driven from the downloads list of another.
    /// </summary>
    public Dictionary<string, CoreWebView2DownloadOperation> NativeDownloads { get; } = new();

    /// <summary>
    /// The uBlock registration work, shared by every window in the process because
    /// every window talks to the one WebView2 user data folder in
    /// <see cref="WebViewDataDirectory"/>. It keys its own state on that folder, so a
    /// run against a different data directory stays separate instead of sharing this
    /// one process instance's idea of what is registered.
    /// </summary>
    public ExtensionStartupCoordinator ExtensionStartup { get; } = new();
}