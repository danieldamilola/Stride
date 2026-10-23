using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UmberBrowser.Models;
using UmberBrowser.Services;
using UmberBrowser.Services.UI;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class ClearDataOnExitHistoryTests
{
    private sealed class FakeHistoryStore : IHistoryStore
    {
        public List<HistoryEntry> Entries { get; } = [];
        public bool ThrowOnClear { get; set; }
        public bool FailOnClear { get; set; }
        public int ClearCallCount { get; private set; }

        public void Add(string url, string title) =>
            Entries.Add(new HistoryEntry { Url = url, Title = title, VisitedAt = DateTime.UtcNow });

        public List<HistoryEntry> Load() => [.. Entries];

        public List<HistoryEntry> Search(string query) =>
            Entries.FindAll(e => (e.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                 (e.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        public bool Clear()
        {
            ClearCallCount++;
            if (ThrowOnClear) throw new IOException("Disk failure during history clear");
            if (FailOnClear) return false;
            Entries.Clear();
            return true;
        }

        public List<HistoryEntry> GetFrequentSites(int count = 8) => Entries.GetRange(0, Math.Min(count, Entries.Count));
    }

    [Fact]
    public async Task ClearBrowsingData_WhenTrue_ClearsHistoryStore()
    {
        var historyStore = new FakeHistoryStore();
        historyStore.Add("https://example.com/visited", "Visited Page");
        Assert.Single(historyStore.Entries);

        var writes = new WindowShutdownWrites
        {
            WriteSnapshot = false,
            ClearBrowsingData = true,
            WriteSnapshotRows = () => true,
            ClearSnapshot = () => true,
            ClearClosedTabHistory = () => true,
            ClearHistory = historyStore.Clear,
            SaveSettings = () => true,
            ClearBrowsingDataAsync = () => Task.CompletedTask
        };

        var result = await writes.RunAsync();

        Assert.True(result.Complete);
        Assert.True(result.BrowsingHistoryCleared);
        Assert.Equal(1, historyStore.ClearCallCount);
        Assert.Empty(historyStore.Entries);
    }

    [Fact]
    public async Task ClearBrowsingData_WhenFalse_PreservesHistoryStore()
    {
        var historyStore = new FakeHistoryStore();
        historyStore.Add("https://example.com/visited", "Visited Page");

        var writes = new WindowShutdownWrites
        {
            WriteSnapshot = true,
            ClearBrowsingData = false,
            WriteSnapshotRows = () => true,
            ClearSnapshot = () => true,
            ClearClosedTabHistory = () => true,
            ClearHistory = historyStore.Clear,
            SaveSettings = () => true,
            ClearBrowsingDataAsync = () => Task.CompletedTask
        };

        var result = await writes.RunAsync();

        Assert.True(result.Complete);
        Assert.True(result.BrowsingHistoryCleared);
        Assert.Equal(0, historyStore.ClearCallCount);
        Assert.Single(historyStore.Entries);
    }

    [Fact]
    public async Task ClearBrowsingData_WhenHistoryClearFails_ReportsIncompleteShutdown()
    {
        var historyStore = new FakeHistoryStore { FailOnClear = true };
        historyStore.Add("https://example.com/visited", "Visited Page");

        var writes = new WindowShutdownWrites
        {
            WriteSnapshot = false,
            ClearBrowsingData = true,
            WriteSnapshotRows = () => true,
            ClearSnapshot = () => true,
            ClearClosedTabHistory = () => true,
            ClearHistory = historyStore.Clear,
            SaveSettings = () => true,
            ClearBrowsingDataAsync = () => Task.CompletedTask
        };

        var result = await writes.RunAsync();

        Assert.False(result.BrowsingHistoryCleared);
        Assert.False(result.Complete);
        Assert.True(result.RecoveryStateIntact);
    }

    [Fact]
    public async Task ClearBrowsingData_WhenHistoryClearThrows_ReportsIncompleteShutdownWithoutCrashing()
    {
        var historyStore = new FakeHistoryStore { ThrowOnClear = true };
        historyStore.Add("https://example.com/visited", "Visited Page");

        var writes = new WindowShutdownWrites
        {
            WriteSnapshot = false,
            ClearBrowsingData = true,
            WriteSnapshotRows = () => true,
            ClearSnapshot = () => true,
            ClearClosedTabHistory = () => true,
            ClearHistory = historyStore.Clear,
            SaveSettings = () => true,
            ClearBrowsingDataAsync = () => Task.CompletedTask
        };

        var result = await writes.RunAsync();

        Assert.False(result.BrowsingHistoryCleared);
        Assert.False(result.Complete);
    }
}
