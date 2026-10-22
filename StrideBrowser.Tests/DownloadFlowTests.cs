using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using StrideBrowser.Engine.Handlers;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Downloads;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class DownloadFlowTests
{
    [Fact]
    public void RestoredPausedItem_OffersRestartInsteadOfPretendingToResume()
    {
        var before = new DownloadItem { Url = "https://example.com/file.zip", State = DownloadState.Paused, CanResumeNative = true };
        var restored = JsonSerializer.Deserialize<DownloadItem>(JsonSerializer.Serialize(before, DownloadJson.Options), DownloadJson.Options)!;
        Assert.False(restored.CanResume);
        Assert.True(restored.CanRestart);
        Assert.Equal("Restart", restored.RetryLabel);
        Assert.Contains("Restart", restored.StatusText);
    }
    [Theory]
    [InlineData("blob:https://example.com/id")]
    [InlineData("file:///C:/example.zip")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public void ExpiredOrNonNetworkLinks_DoNotOfferADeadRestart(string url)
    {
        var item = new DownloadItem { Url = url, State = DownloadState.Failed };
        Assert.False(item.CanRestart);
    }
    [Fact]
    public void CompletedDetails_KeepTheSizeAndSource()
    {
        var item = new DownloadItem { Url = "https://example.com/file.zip", State = DownloadState.Completed, ReceivedBytes = 1024, TotalBytes = 1024 };
        Assert.Contains("1.0 KB", item.DetailText);
        Assert.Contains("example.com", item.DetailText);
        Assert.Equal("Completed", item.StatusText);
        Assert.True(item.CanOpen);
    }
    [Fact]
    public void ProgressTracker_UsesElapsedTimeAndHandlesUnknownLength()
    {
        var tracker = new DownloadProgressTracker();
        tracker.Reset(0, TimeSpan.Zero);
        var known = tracker.Sample(1000, 5000, TimeSpan.FromSeconds(1));
        Assert.Equal(1000, known.Speed);
        Assert.Equal(TimeSpan.FromSeconds(4), known.Remaining);
        var unknown = tracker.Sample(2000, 0, TimeSpan.FromSeconds(2));
        Assert.True(unknown.Speed > 0);
        Assert.Null(unknown.Remaining);
    }
    [Fact]
    public void ProgressTracker_ResetDoesNotCountPausedTimeOrInventNegativeSpeed()
    {
        var tracker = new DownloadProgressTracker();
        tracker.Reset(1000, TimeSpan.FromSeconds(50));
        Assert.Equal(0, tracker.Sample(1000, 5000, TimeSpan.FromSeconds(50.1)).Speed);
        var restarted = tracker.Sample(0, 5000, TimeSpan.FromSeconds(51));
        Assert.Equal(0, restarted.Speed);
        Assert.Null(restarted.Remaining);
    }
    [Fact]
    public void NativeResume_OnlyRunsWhenThereIsAResumableTransfer()
    {
        var store = new MemoryStore(); var actions = new DownloadCoordinator(store);
        var item = new DownloadItem { Url = "https://example.com/file", State = DownloadState.Paused };
        store.Add(item);
        actions.Resume(item.Id);
        Assert.Equal(DownloadState.Paused, item.State);
        var transfer = new FakeTransfer();
        actions.Attach(item, transfer, Guid.NewGuid());
        item.State = DownloadState.Paused;
        actions.Resume(item.Id);
        Assert.Equal(1, transfer.Resumes);
    }
    [Fact]
    public async Task RepeatedRetry_UsesOneOperationAndKeepsTheSameHistoryRow()
    {
        var store = new MemoryStore(); var actions = new DownloadCoordinator(store);
        var item = new DownloadItem { Url = "https://example.com/file", State = DownloadState.Failed };
        store.Add(item);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        async Task Start(DownloadItem value, CancellationToken cancellation)
        {
            count++; await release.Task.WaitAsync(cancellation);
            actions.Attach(value, new FakeTransfer(), Guid.NewGuid());
        }
        var first = actions.RetryAsync(item.Id, Start);
        await actions.RetryAsync(item.Id, Start);
        Assert.Equal(1, count);
        Assert.True(item.IsStarting);
        Assert.False(item.CanRestart);
        release.SetResult(); await first;
        Assert.Same(item, Assert.Single(store.Items));
        Assert.Equal(DownloadState.InProgress, item.State);
        Assert.False(item.IsStarting);
    }
    [Fact]
    public async Task ClearingDuringPendingRetry_CancelsItWithoutRecreatingARecord()
    {
        var store = new MemoryStore(); var actions = new DownloadCoordinator(store);
        var item = new DownloadItem { Url = "https://example.com/file", State = DownloadState.Paused };
        store.Add(item);
        var retry = actions.RetryAsync(item.Id, async (_, cancellation) => await Task.Delay(Timeout.Infinite, cancellation));
        actions.ClearAll(); await retry;
        Assert.Empty(store.Items);
        Assert.False(item.IsStarting);
        Assert.Equal(DownloadState.Cancelled, item.State);
    }
    [Fact]
    public async Task FailedRestart_HasAnErrorAndDoesNotClaimToBeDownloading()
    {
        var store = new MemoryStore(); var actions = new DownloadCoordinator(store);
        var item = new DownloadItem { Url = "https://example.com/file", State = DownloadState.Paused };
        store.Add(item);
        await actions.RetryAsync(item.Id, (_, _) => throw new InvalidOperationException());
        Assert.Equal(DownloadState.Failed, item.State);
        Assert.NotEmpty(item.ErrorMessage);
        Assert.True(item.CanRestart);
        Assert.False(item.IsStarting);
    }
    [Fact]
    public void CancellingACompletedDownload_DoesNotChangeItsStatus()
    {
        var store = new MemoryStore(); var actions = new DownloadCoordinator(store);
        var item = new DownloadItem { State = DownloadState.Completed };
        store.Add(item); actions.Cancel(item.Id);
        Assert.Equal(DownloadState.Completed, item.State);
    }
    [Theory]
    [InlineData("FileNoSpace", "space")]
    [InlineData("FileAccessDenied", "folder")]
    [InlineData("NetworkTimeout", "timed out")]
    [InlineData("ServerUnauthorized", "Sign in")]
    [InlineData("FileMalicious", "harmful")]
    public void NativeFailures_HaveActionableDescriptions(string reason, string expected)
        => Assert.Contains(expected, TabDownloadHandler.DescribeFailure(reason));
    [Fact]
    public void ABlockedFile_DoesNotOfferRetry()
    {
        var item = new DownloadItem { Url = "https://example.com/file", State = DownloadState.Failed, RetryAllowed = !TabDownloadHandler.IsSecurityBlock("FileMalicious") };
        Assert.False(item.CanRestart);
    }
    [Theory]
    [InlineData(900, 1100, 548)]
    [InlineData(1200, 1100, 712)]
    [InlineData(5, 1100, 8)]
    [InlineData(390, 400, 12)]
    public void PopupPlacement_StaysInsideItsWindow(double targetLeft, double width, double expectedLeft)
    {
        var placements = DownloadsPopupController.GetPlacements(new Size(380, 300), new Size(28, 28), targetLeft, width);
        Assert.Equal(expectedLeft, placements[0].Point.X + targetLeft);
        Assert.Equal(34, placements[0].Point.Y);
    }
    [Fact]
    public void DuplicateNames_UseAnAvailablePathWithoutChangingTheFolder()
    {
        var reserved = new HashSet<string> { @"C:\Downloads\setup.exe", @"C:\Downloads\setup (1).exe" };
        Assert.Equal(@"C:\Downloads\setup (2).exe", DownloadDestination.Choose(@"C:\Downloads\setup.exe", reserved.Contains));
    }
    [Fact]
    public async Task DisposedNativeTransfer_DoesNotCrashOrPreventARealRestart()
    {
        var store = new MemoryStore(); var actions = new DownloadCoordinator(store);
        var item = new DownloadItem { Url = "https://example.com/file" };
        actions.Attach(item, new DisposedTransfer(), Guid.NewGuid());
        item.State = DownloadState.Paused; item.CanResumeNative = true;
        actions.Resume(item.Id);
        Assert.Equal(DownloadState.Failed, item.State);
        Assert.False(item.CanResume);
        await actions.RetryAsync(item.Id, (value, _) => { actions.Attach(value, new FakeTransfer(), Guid.NewGuid()); return Task.CompletedTask; });
        Assert.Equal(DownloadState.InProgress, item.State);
        Assert.Empty(item.ErrorMessage);
    }
    private sealed class DisposedTransfer : IDownloadTransfer
    {
        public bool CanResume => throw new ObjectDisposedException("transfer");
        public void Pause() => throw new ObjectDisposedException("transfer");
        public void Resume() => throw new ObjectDisposedException("transfer");
        public void Cancel() => throw new ObjectDisposedException("transfer");
    }
    private sealed class FakeTransfer : IDownloadTransfer
    {
        public int Resumes;
        public bool CanResume => true;
        public void Pause() { }
        public void Resume() => Resumes++;
        public void Cancel() { }
    }
    private sealed class MemoryStore : IDownloadStore
    {
        public ObservableCollection<DownloadItem> Items { get; } = new();
        public void Add(DownloadItem item) => Items.Insert(0, item);
        public DownloadItem? Get(string id) => Items.FirstOrDefault(item => item.Id == id);
        public void Remove(string id) { if (Get(id) is { } item) Items.Remove(item); }
        public void ClearCompleted() { foreach (var item in Items.Where(i => i.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled).ToArray()) Items.Remove(item); }
        public void ClearAll() => Items.Clear();
    }
}
