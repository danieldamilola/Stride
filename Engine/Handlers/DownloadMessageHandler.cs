using System.Text.Json;
using UmberBrowser.Models;
using UmberBrowser.Services;

namespace UmberBrowser.Engine.Handlers;

public sealed class DownloadMessageHandler(TabEngine engine, IDownloadStore downloadStore) : IWebMessageHandler
{
    private bool _syncing;
    public IEnumerable<MessageRoute> GetRoutes()
    {
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadCopy, id => Run(() => engine.Downloads.CopyLink(id)));
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadRemove, id => Run(() => engine.Downloads.Remove(id)));
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadOpen, id => Run(() => engine.Downloads.Open(id)));
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadFolder, id => Run(() => engine.Downloads.ShowInFolder(id)));
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadCancel, id => Run(() => engine.Downloads.Cancel(id)));
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadPause, id => Run(() => engine.Downloads.Pause(id)));
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadResume, id => Run(() => engine.Downloads.Resume(id)));
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadRetry, engine.RetryDownloadAsync);
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadRequest, engine.RequestDownloadAsync);
        yield return MessageRoute.Exact(WebMessagePrefix.DownloadClear, () => Run(downloadStore.ClearCompleted));
        yield return MessageRoute.Exact(WebMessagePrefix.DownloadClearAll, () => Run(engine.Downloads.ClearAll));
        yield return MessageRoute.Exact(WebMessagePrefix.DownloadRequestSync, Sync);
    }
    private static Task Run(Action action) { action(); return Task.CompletedTask; }
    private async Task Sync()
    {
        if (_syncing || engine.ActiveTab?.Url != InternalUrls.Downloads) return;
        _syncing = true;
        var tab = engine.ActiveTab;
        var core = engine.GetCoreWebView2();
        try
        {
            var items = downloadStore.Items.ToArray();
            await engine.Downloads.FileDetails.RefreshAsync(items);
            if (engine.ActiveTab == tab && engine.GetCoreWebView2() == core)
                core?.PostWebMessageAsString(engine.IpcToken + ":downloads:" + DownloadJson.SerializeForPage(downloadStore.Items));
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        finally { _syncing = false; }
    }
}
