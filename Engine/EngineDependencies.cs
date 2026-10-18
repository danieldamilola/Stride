using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.LinkPreview;

namespace StrideBrowser.Engine;

/// <summary>
/// Non UI dependencies for TabEngine, grouped so the constructor stays small.
/// </summary>
public sealed record EngineDependencies
{
    /// <summary>
    /// Process wide state. Each window builds its own engine, so the per session IPC
    /// token and the native download table have to come from here or a tab moved
    /// between windows would lose the token its internal pages check.
    /// </summary>
    public required BrowserSession Session { get; init; }
    public required ExtensionManager ExtensionManager { get; init; }
    public required YouTubeUnhook YouTubeUnhook { get; init; }
    public required YouTubeEnhancer YouTubeEnhancer { get; init; }
    public required BrowserSettings Settings { get; init; }
    public required FaviconLoader FaviconLoader { get; init; }
    public required InternalPages Pages { get; init; }
    public required IHistoryStore HistoryStore { get; init; }
    public required IOneTabStore OneTabStore { get; init; }
    public required IDownloadStore DownloadStore { get; init; }
    public required IClosedTabStore ClosedTabStore { get; init; }
    public required FocusBlocklistService FocusBlocklistService { get; init; }
    public required ContentScriptInjector ContentScriptInjector { get; init; }
    public required CustomDownloadManager CustomDownloadManager { get; init; }
    public required TabHibernationManager HibernationManager { get; init; }
    public required NavigationPolicyEngine NavigationPolicyEngine { get; init; }
    public required ThemeManager ThemeManager { get; init; }
    public required ILinkPreviewDownloadSuppressor DownloadSuppressor { get; init; }
}
