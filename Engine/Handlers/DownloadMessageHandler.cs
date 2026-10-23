using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Pages;

namespace StrideBrowser.Engine.Handlers;

public sealed class DownloadMessageHandler(TabEngine engine, IDownloadStore downloadStore) : IWebMessageHandler
{
    private bool _syncing;
    private bool _observing;
    private readonly DownloadPageQuery _query = new();
    private readonly DownloadPageChanges _changes = new();
    private readonly HashSet<DownloadItem> _observed = [];
    public IEnumerable<MessageRoute> GetRoutes()
    {
        if (!_observing)
        {
            _observing = true;
            // Weak subscriptions do not retain a closed window through the shared store.
            CollectionChangedEventManager.AddHandler(downloadStore.Items, CollectionChanged);
        }
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
        yield return MessageRoute.Exact(WebMessagePrefix.DownloadRequestSync, () => Sync("{}"));
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadRequestSync + ":", Sync);
    }
    private static Task Run(Action action) { action(); return Task.CompletedTask; }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        _query.Invalidate();
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in _observed) PropertyChangedEventManager.RemoveHandler(item, ItemChanged, nameof(DownloadItem.FileName));
            _observed.Clear();
        }
        if (args.OldItems is not null)
            foreach (DownloadItem item in args.OldItems)
                if (_observed.Remove(item)) PropertyChangedEventManager.RemoveHandler(item, ItemChanged, nameof(DownloadItem.FileName));
        if (args.NewItems is not null)
            foreach (DownloadItem item in args.NewItems)
                if (_observed.Add(item)) PropertyChangedEventManager.AddHandler(item, ItemChanged, nameof(DownloadItem.FileName));
    }
    private void ItemChanged(object? sender, PropertyChangedEventArgs args) => _query.Invalidate();
    private void ObserveItems()
    {
        foreach (var item in downloadStore.Items)
            if (_observed.Add(item)) PropertyChangedEventManager.AddHandler(item, ItemChanged, nameof(DownloadItem.FileName));
    }
    private async Task Sync(string json)
    {
        if (_syncing || engine.ActiveTab?.Url != InternalUrls.Downloads) return;
        _syncing = true;
        var tab = engine.ActiveTab;
        var core = engine.GetCoreWebView2();
        try
        {
            var request = JsonSerializer.Deserialize<PageRequest>(json, DownloadJson.Options);
            if (request is null || request.Query is null || request.Request is null || request.Query.Length > 2048 || request.Request.Length > 128) return;
            if (_observed.Count != downloadStore.Items.Count) ObserveItems();
            var selection = _query.Select(downloadStore.Items, request.Query, request.Page);
            // File existence and shell icon checks are limited to the visible page.
            await engine.Downloads.FileDetails.RefreshAsync(selection.Items);
            if (engine.ActiveTab == tab && engine.GetCoreWebView2() == core)
            {
                var update = _changes.Create(request.Request, selection);
                if (update is not null)
                    core?.PostWebMessageAsString(engine.IpcToken + ":downloads:" + JsonSerializer.Serialize(update, DownloadJson.Options));
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        finally { _syncing = false; }
    }
    private sealed record PageRequest(string Query = "", int Page = 0, string Request = "");
}
