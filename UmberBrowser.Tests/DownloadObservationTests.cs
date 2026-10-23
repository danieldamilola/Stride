using System.Collections.ObjectModel;
using System.IO;
using UmberBrowser.Models;
using UmberBrowser.Services.Downloads;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class DownloadObservationTests
{
    [Fact]
    public async Task StoreLookupFollowsCollectionChangesAndReload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "StrideDownloadLookup", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "downloads.json");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new UmberBrowser.Services.DownloadStore(path);
            var item = new DownloadItem { State = DownloadState.Completed };
            store.Add(item);
            Assert.Same(item, store.Get(item.Id));
            Assert.True(await store.FlushAsync());
            var restored = new UmberBrowser.Services.DownloadStore(path);
            Assert.Equal(item.Id, restored.Get(item.Id)!.Id);
            store.ClearCompleted();
            Assert.Null(store.Get(item.Id));
            Assert.True(await store.FlushAsync());
            // The collection remains public for existing consumers.
            store.Items.Add(item);
            Assert.Same(item, store.Get(item.Id));
            store.Items.Clear();
            Assert.Null(store.Get(item.Id));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ObserverFollowsAddReplaceMoveRemoveAndResetWithoutDuplicateSubscriptions()
    {
        var first = new DownloadItem();
        var second = new DownloadItem();
        var replacement = new DownloadItem();
        var items = new ObservableCollection<DownloadItem> { first };
        var changed = new List<DownloadItem>();
        using var observer = new DownloadCollectionObserver(items, (sender, args) =>
        {
            if (args.PropertyName == nameof(DownloadItem.FileName)) changed.Add((DownloadItem)sender!);
        }, _ => { });
        items.Add(second);
        items.Move(1, 0);
        first.FileName = "first.zip";
        second.FileName = "second.zip";
        Assert.Equal(new[] { first, second }, changed);
        changed.Clear();
        items[1] = replacement;
        first.FileName = "removed.zip";
        replacement.FileName = "replacement.zip";
        Assert.Same(replacement, Assert.Single(changed));
        changed.Clear();
        items.Remove(second);
        items.Clear();
        second.FileName = "removed.zip";
        replacement.FileName = "removed.zip";
        Assert.Empty(changed);
    }

    [Fact]
    public void DisposingObserverStopsCollectionAndItemNotifications()
    {
        var first = new DownloadItem();
        var items = new ObservableCollection<DownloadItem> { first };
        var notifications = 0;
        var observer = new DownloadCollectionObserver(items, (_, _) => notifications++, _ => notifications++);
        observer.Dispose();
        observer.Dispose();
        first.FileName = "changed.zip";
        var later = new DownloadItem();
        items.Add(later);
        later.FileName = "changed.zip";
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void ToolbarActivityTracksTransitionsWithoutReportingCompletedProgress()
    {
        var history = new ObservableCollection<DownloadItem>(Enumerable.Range(0, 10_000)
            .Select(_ => new DownloadItem { State = DownloadState.Completed }));
        var live = new DownloadItem { State = DownloadState.InProgress };
        history.Add(live);
        using var tracker = new DownloadActivityTracker(history);
        var notifications = 0;
        tracker.Changed += () => notifications++;
        Assert.Same(live, Assert.Single(tracker.ActiveItems));
        history[0].ReceivedBytes = 100;
        Assert.Equal(0, notifications);
        live.ReceivedBytes = 100;
        Assert.Equal(1, notifications);
        live.State = DownloadState.Paused;
        Assert.Empty(tracker.ActiveItems);
        live.IsStarting = true;
        Assert.Same(live, Assert.Single(tracker.ActiveItems));
        live.IsStarting = false;
        Assert.Empty(tracker.ActiveItems);
        live.State = DownloadState.InProgress;
        history.Remove(live);
        Assert.Empty(tracker.ActiveItems);
        var before = notifications;
        live.ReceivedBytes = 200;
        Assert.Equal(before, notifications);
    }

    [Fact]
    public void ClearingHistoryRemovesAllActiveSubscriptions()
    {
        var item = new DownloadItem { State = DownloadState.InProgress };
        var source = new ObservableCollection<DownloadItem> { item };
        using var tracker = new DownloadActivityTracker(source);
        source.Clear();
        Assert.Empty(tracker.ActiveItems);
        var changes = 0;
        tracker.Changed += () => changes++;
        item.TotalBytes = 1_000;
        Assert.Equal(0, changes);
        var added = new DownloadItem { State = DownloadState.Completed, IsStarting = true };
        source.Add(added);
        Assert.Same(added, Assert.Single(tracker.ActiveItems));
    }

    [Fact]
    public void RecentSelectionKeepsStableTiesAndReadsHistoryOnce()
    {
        var now = DateTime.UtcNow;
        var records = Enumerable.Range(0, 10_000).Select(index => new DownloadItem
        {
            Id = index.ToString(), State = DownloadState.Completed, StartedAt = now.AddMinutes(-index)
        }).ToArray();
        var enumerations = 0;
        IEnumerable<DownloadItem> Once()
        {
            Assert.Equal(1, ++enumerations);
            foreach (var item in records) yield return item;
        }
        Assert.Equal(records.Take(5), RecentDownloadList.Select(Once(), now));
        var tied = records.Take(8).Select(item => new DownloadItem { Id = item.Id, State = DownloadState.Completed, StartedAt = now }).ToArray();
        tied[^1].State = DownloadState.InProgress;
        Assert.Equal(tied.Take(5).Append(tied[^1]), RecentDownloadList.Select(tied, now));
    }
}

