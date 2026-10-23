using UmberBrowser.Models;
using UmberBrowser.Services.Downloads;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class RecentDownloadListTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PopupExcludesOldHistoryWithoutRemovingItFromTheStore()
    {
        var old = Item(Now.AddDays(-2));
        var recent = Item(Now.AddMinutes(-10));
        var history = new[] { old, recent };
        Assert.Equal(new[] { recent }, RecentDownloadList.Select(history, Now));
        Assert.Equal(2, history.Length);
    }

    [Fact]
    public void PopupKeepsOnlyFiveRecentItemsInNewestFirstOrder()
    {
        var history = Enumerable.Range(0, 8).Select(index => Item(Now.AddMinutes(-index))).Reverse().ToArray();
        var result = RecentDownloadList.Select(history, Now);
        Assert.Equal(5, result.Count);
        Assert.Equal(history.Reverse().Take(5), result);
    }

    [Fact]
    public void OlderLiveTransfersRemainReachableBeyondTheRecentLimit()
    {
        var recent = Enumerable.Range(0, 5).Select(index => Item(Now.AddMinutes(-index))).ToArray();
        var active = Item(Now.AddDays(-3), DownloadState.InProgress);
        var paused = Item(Now.AddDays(-2), DownloadState.Paused);
        paused.CanResumeNative = true;
        var restored = Item(Now.AddDays(-2), DownloadState.Paused);
        var starting = Item(Now.AddDays(-4), DownloadState.Failed);
        starting.IsStarting = true;
        var result = RecentDownloadList.Select(recent.Concat(new[] { active, paused, restored, starting }), Now);
        Assert.Equal(8, result.Count);
        Assert.Contains(active, result);
        Assert.Contains(paused, result);
        Assert.Contains(starting, result);
        Assert.DoesNotContain(restored, result);
    }

    [Fact]
    public void ATransferCompletedTodayIsRecentEvenIfItStartedEarlier()
    {
        var completed = Item(Now.AddDays(-2));
        completed.CompletedAt = Now.AddMinutes(-1);
        Assert.Contains(completed, RecentDownloadList.Select(new[] { completed }, Now));
        Assert.Empty(RecentDownloadList.Select(new[] { completed }, Now.AddDays(2)));
    }

    [Fact]
    public void PopupSummaryKeepsCompletedRowsShortAndPausedRowsHonest()
    {
        var item = Item(Now);
        item.TotalBytes = 1024;
        Assert.Equal("1.0 KB", item.PopupDetailText);
        item.State = DownloadState.Paused;
        Assert.StartsWith("Interrupted", item.PopupDetailText);
        item.CanResumeNative = true;
        Assert.StartsWith("Paused", item.PopupDetailText);
    }

    private static DownloadItem Item(DateTime startedAt, DownloadState state = DownloadState.Completed) =>
        new() { StartedAt = startedAt, State = state, Url = "https://example.com/file.zip" };
}
