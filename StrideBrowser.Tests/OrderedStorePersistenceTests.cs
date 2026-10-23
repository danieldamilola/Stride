using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using StrideBrowser.Helpers;
using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class OrderedStorePersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrideOrderedStores", Guid.NewGuid().ToString("N"));
    private string StorePath(string name) => Path.Combine(_directory, name + ".json");

    public OrderedStorePersistenceTests()
    {
        Assert.False(AppPaths.IsSameOrAncestor(AppPaths.ProductionDataDirectory, _directory));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task HistoryMutationsProceedWhileWriterIsBlockedAndFlushRestoresLatestVersion()
    {
        var path = StorePath("history");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var store = new HistoryStore(path, (target, json) =>
        {
            if (Interlocked.Increment(ref writes) == 1)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            AtomicFileWriter.WriteAllText(target, json);
        });
        try
        {
            store.Add("https://first.example/", "First");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // This would deadlock until the writer timed out if Add still owned file I/O.
            store.Add("https://second.example/", "Second");
            Assert.Equal(2, store.Load().Count);
        }
        finally { release.Set(); }
        Assert.True(await store.FlushAsync());
        Assert.Equal(2, new HistoryStore(path).Load().Count);
        Assert.Equal(2, writes);
    }

    [Fact]
    public async Task HistoryClearCannotBeOvertakenByAnOlderWrite()
    {
        var path = StorePath("history-clear");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var store = new HistoryStore(path, (target, json) =>
        {
            if (Interlocked.Increment(ref writes) == 1)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            AtomicFileWriter.WriteAllText(target, json);
        });
        store.Add("https://forgotten.example/", "Forget");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var clear = Task.Run(store.Clear);
        release.Set();
        Assert.True(await clear.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await store.FlushAsync());
        Assert.False(File.Exists(path));
        Assert.Empty(new HistoryStore(path).Load());
        store.Add("https://new.example/", "New");
        Assert.True(await store.FlushAsync());
        Assert.Equal("https://new.example/", Assert.Single(new HistoryStore(path).Load()).Url);
    }

    [Fact]
    public async Task HistoryFlushReportsLockedFileAndCanRetryWithoutAnotherMutation()
    {
        var path = StorePath("history-locked");
        File.WriteAllText(path, "[]");
        var store = new HistoryStore(path);
        store.Load();
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.Add("https://kept.example/", "Kept");
            Assert.False(await store.FlushAsync());
            Assert.Equal("[]", File.ReadAllText(path));
        }
        Assert.True(await store.FlushAsync());
        Assert.Equal("https://kept.example/", Assert.Single(new HistoryStore(path).Load()).Url);
    }

    [Fact]
    public async Task HistoryClearFailureRemainsDirtyAndCannotRestoreForgottenEntriesOnRetry()
    {
        var path = StorePath("history-clear-locked");
        var store = new HistoryStore(path);
        store.Add("https://forgotten.example/", "Forget");
        Assert.True(await store.FlushAsync());
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(store.Clear());
            Assert.False(await store.FlushAsync());
            Assert.Empty(store.Load());
        }
        Assert.True(await store.FlushAsync());
        Assert.Empty(new HistoryStore(path).Load());
    }

    [Fact]
    public async Task ConcurrentHistoryMutationsCoalesceAndPreserveTheCap()
    {
        var path = StorePath("history-concurrent");
        var store = new HistoryStore(path);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var index = 0; index < 1500; index++) store.Add($"https://example.com/{worker}/{index}", "Visit");
        })));
        Assert.True(await store.FlushAsync());
        var restored = new HistoryStore(path).Load();
        Assert.Equal(10_000, restored.Count);
        Assert.Equal(10_000, restored.Select(entry => entry.Url).Distinct().Count());
    }

    [Fact]
    public async Task CancelledFlushWaitDoesNotCancelThePendingAtomicWrite()
    {
        var path = StorePath("history-cancel-wait");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new HistoryStore(path, (target, json) =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            AtomicFileWriter.WriteAllText(target, json);
        });
        try
        {
            store.Add("https://kept.example/", "Kept");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.FlushAsync(cancellation.Token));
        }
        finally { release.Set(); }
        Assert.True(await store.FlushAsync());
        Assert.Single(new HistoryStore(path).Load());
    }

    [Fact]
    public async Task DownloadClearCompletedUsesOneResetAndUnsubscribesRemovedRecords()
    {
        var path = StorePath("download-clear");
        var writes = 0;
        var store = new DownloadStore(path, (target, json) =>
        {
            Interlocked.Increment(ref writes);
            AtomicFileWriter.WriteAllText(target, json);
        });
        var completed = Enumerable.Range(0, 1000).Select(index => new DownloadItem { FileName = index + ".zip", State = DownloadState.Completed }).ToArray();
        using (store.BeginUpdate())
        {
            foreach (var item in completed) store.Add(item);
            store.Add(new DownloadItem { State = DownloadState.InProgress });
        }
        Assert.True(await store.FlushAsync());
        var events = new List<NotifyCollectionChangedAction>();
        store.Items.CollectionChanged += (_, args) => events.Add(args.Action);
        store.ClearCompleted();
        Assert.Equal([NotifyCollectionChangedAction.Reset], events);
        Assert.Single(store.Items);
        Assert.True(await store.FlushAsync());
        var writesAfterClear = writes;
        foreach (var item in completed) item.State = DownloadState.Failed;
        await Task.Delay(200);
        Assert.Equal(writesAfterClear, Volatile.Read(ref writes));
        // A second explicit flush deliberately writes a current snapshot. Removed models must add none.
        Assert.True(await store.FlushAsync());
        Assert.Equal(writesAfterClear + 1, writes);
        var restored = new DownloadStore(path);
        Assert.Single(restored.Items);
        Assert.Equal(DownloadState.Paused, restored.Items[0].State);
        events.Clear();
        store.ClearAll();
        Assert.Equal([NotifyCollectionChangedAction.Reset], events);
        Assert.True(await store.FlushAsync());
        Assert.Empty(new DownloadStore(path).Items);
    }

    [Fact]
    public async Task DownloadStateChangeQueuesImmutableRecordAndFlushPersistsFinalFields()
    {
        var path = StorePath("download-state");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshots = new List<string>();
        var store = new DownloadStore(path, (target, json) =>
        {
            lock (snapshots) snapshots.Add(json);
            if (snapshots.Count == 1)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            AtomicFileWriter.WriteAllText(target, json);
        });
        var item = new DownloadItem { Url = "https://example.com/file.zip", FileName = "file.zip" };
        try
        {
            store.Add(item);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            item.ReceivedBytes = 123;
            item.TotalBytes = 123;
            item.CompletedAt = DateTime.UtcNow;
            item.FilePath = @"C:\Downloads\file.zip";
            item.State = DownloadState.Completed;
        }
        finally { release.Set(); }
        Assert.True(await store.FlushAsync());
        using var first = JsonDocument.Parse(snapshots[0]);
        Assert.Equal((int)DownloadState.InProgress, first.RootElement[0].GetProperty("state").GetInt32());
        Assert.Equal(0, first.RootElement[0].GetProperty("receivedBytes").GetInt64());
        var restored = Assert.Single(new DownloadStore(path).Items);
        Assert.Equal(DownloadState.Completed, restored.State);
        Assert.Equal(123, restored.ReceivedBytes);
        Assert.NotNull(restored.CompletedAt);
        Assert.Equal(item.FilePath, restored.FilePath);
    }

    [Fact]
    public async Task SavedTabsUseMemoryAndRapidMutationsSurviveRestartAtTenThousandTabs()
    {
        var path = StorePath("saved-tabs");
        var group = new OneTabGroup { Id = "group", Tabs = Enumerable.Range(0, 10_000)
            .Select(index => new OneTabEntry("https://example.com/" + index, index.ToString(), null, DateTime.UtcNow)).ToList() };
        var store = new OneTabStore(path);
        store.AddGroup(group);
        Assert.True(await store.FlushAsync());
        // An out-of-process file edit must not become the source for an in-process mutation.
        File.WriteAllText(path, "[]");
        for (var index = 0; index < 101; index++) store.ToggleStar("group", 0);
        store.ReorderTab("group", 0, 9999);
        store.RenameGroup("group", "Renamed");
        Assert.True(await store.FlushAsync());
        var restored = Assert.Single(new OneTabStore(path).Load());
        Assert.Equal(10_000, restored.Tabs.Count);
        Assert.Equal("Renamed", restored.Name);
        Assert.Equal("https://example.com/0", restored.Tabs[9999].Url);
        Assert.True(restored.Tabs[9999].IsStarred);
        // Callers cannot mutate the authoritative in-memory collection through Load results.
        restored.Tabs.Clear();
        Assert.Equal(10_000, Assert.Single(store.Load()).Tabs.Count);
    }

    [Fact]
    public async Task SavedTabsKeepDuplicateUrlRemovalAndGroupOrdering()
    {
        var path = StorePath("saved-tab-duplicates");
        var store = new OneTabStore(path);
        store.AddGroup(new OneTabGroup { Id = "first", Tabs = [new("https://same.example/", "First", null, DateTime.UtcNow), new("https://same.example/", "Second", null, DateTime.UtcNow)] });
        store.AddGroup(new OneTabGroup { Id = "second", Tabs = [new("https://other.example/", "Other", null, DateTime.UtcNow)] });
        store.ToggleStar("first", 1);
        store.RemoveTab("first", "https://same.example/");
        store.ReorderGroup(0, 1);
        Assert.True(await store.FlushAsync());
        var restored = new OneTabStore(path).Load();
        Assert.Equal(["second", "first"], restored.Select(group => group.Id));
        Assert.Equal("Second", Assert.Single(restored[1].Tabs).Title);
        Assert.True(restored[1].Tabs[0].IsStarred);
    }

    [Fact]
    public async Task SavedTabsWriteFailureRetainsMemoryAndExplicitFlushRetries()
    {
        var path = StorePath("saved-tab-fault");
        var fail = true;
        var store = new OneTabStore(path, (target, json) =>
        {
            if (Volatile.Read(ref fail)) throw new IOException("Injected fault");
            AtomicFileWriter.WriteAllText(target, json);
        });
        store.AddGroup(new OneTabGroup { Id = "kept" });
        Assert.False(await store.FlushAsync());
        Assert.Single(store.Load());
        Assert.False(File.Exists(path));
        Volatile.Write(ref fail, false);
        Assert.True(await store.FlushAsync());
        Assert.Equal("kept", Assert.Single(new OneTabStore(path).Load()).Id);
    }

    [Fact]
    public async Task SaveAllReturnsOnlyAfterDurableWriteAndRollsBackAFailedGroup()
    {
        var path = StorePath("save-all");
        var fail = false;
        var store = new OneTabStore(path, (target, json) =>
        {
            if (fail) throw new IOException("Injected fault");
            AtomicFileWriter.WriteAllText(target, json);
        });
        var first = store.SaveAll([new BrowserTab { Url = "https://first.example/", Title = "First" }]);
        Assert.NotNull(first);
        Assert.Equal(first.Id, Assert.Single(new OneTabStore(path).Load()).Id);
        fail = true;
        Assert.Null(store.SaveAll([new BrowserTab { Url = "https://failed.example/" }]));
        Assert.Single(store.Load());
        fail = false;
        Assert.True(await store.FlushAsync());
        Assert.Equal(first.Id, Assert.Single(new OneTabStore(path).Load()).Id);
    }
}
