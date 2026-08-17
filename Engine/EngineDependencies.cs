using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine;

/// <summary>
/// Groups the non-UI dependencies that TabEngine needs,
/// reducing the constructor signature from 8 params to 4.
/// </summary>
public sealed record EngineDependencies
{
    public required ExtensionManager ExtensionManager { get; init; }
    public required YouTubeUnhook YouTubeUnhook { get; init; }
    public required BrowserSettings Settings { get; init; }
    public required FaviconLoader FaviconLoader { get; init; }
    public required InternalPages Pages { get; init; }
    public required IHistoryStore HistoryStore { get; init; }
    public required IOneTabStore OneTabStore { get; init; }
    public required IDownloadStore DownloadStore { get; init; }
    public required FocusBlocklistService FocusBlocklistService { get; init; }
    public required ContentScriptInjector ContentScriptInjector { get; init; }
    public required CustomDownloadManager CustomDownloadManager { get; init; }
    public required TabHibernationManager HibernationManager { get; init; }
    public required NavigationPolicyEngine NavigationPolicyEngine { get; init; }
    public required ThemeManager ThemeManager { get; init; }
}
