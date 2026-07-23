using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine;

/// <summary>
/// Groups the non-UI dependencies that TabEngine needs,
/// reducing the constructor signature from 8 params to 4.
/// </summary>
public sealed record EngineDependencies(
    ExtensionManager ExtensionManager,
    YouTubeUnhook YouTubeUnhook,
    BrowserSettings Settings,
    FaviconLoader FaviconLoader,
    InternalPages Pages,
    IHistoryStore HistoryStore,
    IOneTabStore OneTabStore,
    IDownloadStore DownloadStore,
    FocusBlocklistService FocusBlocklistService,
    ContentScriptInjector ContentScriptInjector);
