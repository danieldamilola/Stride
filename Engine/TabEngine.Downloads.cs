using Microsoft.Web.WebView2.Core;
using StrideBrowser.Models;
using StrideBrowser.Services.Downloads;

namespace StrideBrowser.Engine;

public sealed partial class TabEngine
{
    public DownloadCoordinator Downloads { get; }
    private readonly NativeDownloadRequest _downloadRequests;

    public Task RetryDownloadAsync(string id) => Downloads.RetryAsync(id, (item, cancellation) =>
        _downloadRequests.StartAsync(WebViewEnvironment ?? throw new InvalidOperationException("Browser is not ready."),
            item, ActiveTab?.Id ?? Guid.Empty, cancellation));

    public async Task RequestDownloadAsync(string url)
    {
        if (!DownloadItem.IsRestartableUrl(url)) return;
        var item = new DownloadItem
        {
            Url = url,
            FileName = Handlers.TabDownloadHandler.ResolveDownloadFileName(null, url),
            State = DownloadState.Paused
        };
        _downloadStore.Add(item);
        await RetryDownloadAsync(item.Id);
    }

    public async Task OpenDownloadsAsync()
    {
        var tab = Tabs.FirstOrDefault(t => t.Url == InternalUrls.Downloads) ?? CreateTab(InternalUrls.Downloads);
        SwitchTo(tab);
        await ActivateAsync(tab);
    }
}
