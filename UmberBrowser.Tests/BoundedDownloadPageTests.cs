using System.Text.Json;
using UmberBrowser.Models;
using UmberBrowser.Services;
using UmberBrowser.Services.Pages;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class BoundedDownloadPageTests
{
    private static DownloadItem[] History(int count) => Enumerable.Range(0, count).Select(index => new DownloadItem
    {
        Id = "item-" + index,
        FileName = "record-" + index + ".zip",
        Url = "https://example.com/" + index,
        StartedAt = new DateTime(2026, 1, 1).AddMinutes(index),
        State = DownloadState.Completed
    }).ToArray();

    [Fact]
    public void ShellSizeDoesNotGrowWithRetainedHistory()
    {
        var page = new DownloadPage();
        var empty = page.Render([], "token");
        var large = page.Render(History(10000).ToList(), "token");
        Assert.Equal(empty, large);
        Assert.DoesNotContain("record-9999.zip", large);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(large) < 100000);
    }

    [Fact]
    public void EveryRecordIsReachableInBoundedPagesInNewestFirstOrder()
    {
        var items = History(10000);
        var query = new DownloadPageQuery();
        var seen = new List<string>();
        for (var page = 0; page < 200; page++)
        {
            var selected = query.Select(items, "", page);
            Assert.Equal(10000, selected.Total);
            Assert.Equal(50, selected.Items.Length);
            seen.AddRange(selected.Items.Select(item => item.Id));
        }
        Assert.Equal(items.Reverse().Select(item => item.Id), seen);
    }

    [Fact]
    public void SearchIncludesOldRecordsOutsideFirstPageAndClampsPage()
    {
        var items = History(10000);
        var query = new DownloadPageQuery();
        query.Select(items, "", 0);
        var selected = query.Select(items, "  RECORD-0.ZIP  ", int.MaxValue);
        Assert.Equal(0, selected.Page);
        Assert.Equal(1, selected.Total);
        Assert.Same(items[0], Assert.Single(selected.Items));
        Assert.Empty(query.Select(items, "does not exist", -20).Items);
    }

    [Fact]
    public void CachedIndexDoesNotEnumerateSourceOnProgressPolls()
    {
        var items = History(10000);
        var query = new DownloadPageQuery();
        query.Select(items, "", 0);
        IEnumerable<DownloadItem> UnexpectedEnumeration() => Enumerable.Range(0, 1)
            .Select<int, DownloadItem>(_ => throw new InvalidOperationException("Full history enumerated"));
        Assert.Equal(50, query.Select(UnexpectedEnumeration(), "", 0).Items.Length);
    }

    [Fact]
    public void InvalidationRefreshesRenamedAndRemovedRecords()
    {
        var items = History(100).ToList();
        var query = new DownloadPageQuery();
        Assert.Empty(query.Select(items, "renamed", 0).Items);
        items[0].FileName = "renamed.zip";
        query.Invalidate();
        Assert.Same(items[0], Assert.Single(query.Select(items, "renamed", 0).Items));
        items.RemoveAt(0);
        query.Invalidate();
        Assert.Empty(query.Select(items, "renamed", 0).Items);
    }

    [Fact]
    public void UnchangedPageProducesNoPayloadAndProgressSendsOnlyChangedRow()
    {
        var items = History(10000);
        items[^1].State = DownloadState.InProgress;
        var selection = new DownloadPageQuery().Select(items, "", 0);
        var changes = new DownloadPageChanges();
        var first = changes.Create("document:0", selection)!;
        Assert.Equal(50, first.Changed.Length);
        Assert.True(first.Active);
        Assert.Null(changes.Create("document:0", selection));
        items[^1].ReceivedBytes = 12345;
        var progress = changes.Create("document:0", selection)!;
        Assert.Equal(items[^1].Id, Assert.Single(progress.Changed).Id);
        Assert.Equal(12345, progress.Changed[0].ReceivedBytes);
        Assert.Null(changes.Create("document:0", selection));
        // A fresh document needs every visible row even when the host cache is unchanged.
        Assert.Equal(50, changes.Create("other-document:0", selection)!.Changed.Length);
    }

    [Fact]
    public void MissingFileAndActionChangesReachThePage()
    {
        var item = History(1)[0];
        var selection = new DownloadPageQuery().Select([item], "", 0);
        var changes = new DownloadPageChanges();
        changes.Create("page", selection);
        item.IsMissing = true;
        var update = changes.Create("page", selection)!;
        var row = Assert.Single(update.Changed);
        Assert.False(row.CanOpen);
        Assert.True(row.CanRestart);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(update, DownloadJson.Options));
        Assert.True(json.RootElement.GetProperty("changed")[0].GetProperty("isMissing").GetBoolean());
    }

    [Fact]
    public void RemovingTheLastPageUpdatesMembershipWithoutRetainingOldRows()
    {
        var query = new DownloadPageQuery();
        var items = History(51).ToList();
        var changes = new DownloadPageChanges();
        changes.Create("page", query.Select(items, "", 1));
        items.RemoveAt(0);
        query.Invalidate();
        var update = changes.Create("page", query.Select(items, "", 1))!;
        Assert.Equal(0, update.Page);
        Assert.Equal(50, update.Total);
        Assert.DoesNotContain("item-0", update.Ids);
        Assert.Equal(50, update.Ids.Length);
    }
}

