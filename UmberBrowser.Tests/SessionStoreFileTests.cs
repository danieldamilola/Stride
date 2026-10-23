using System;
using System.IO;
using System.Linq;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// File level behaviour of the startup snapshot. Uses a real temp file so the
/// atomic write path is exercised, not just the in memory helpers.
/// </summary>
public sealed class SessionStoreFileTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("umber-session");
    private readonly string _path;

    public SessionStoreFileTests()
    {
        _path = Path.Combine(_dir.FullName, "session.json");
    }

    public void Dispose() => _dir.Delete(true);

    private SessionStore NewStore() => new(_path);

    [Fact]
    public void NoFile_LoadsEmpty()
    {
        Assert.Empty(NewStore().Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsUrlTitlePinAndActive()
    {
        NewStore().Save([
            ("https://a.example/", "A", false, false),
            ("https://b.example/", "B", true, true),
            ("https://c.example/", "C", false, false),
        ]);

        var loaded = NewStore().Load();

        Assert.Equal(3, loaded.Count);
        Assert.Equal("https://a.example/", loaded[0].Url);
        Assert.True(loaded[1].IsPinned);
        Assert.True(loaded[1].IsActive);
        Assert.Equal(1, SessionStore.ResolveActiveIndex(loaded));
    }

    [Fact]
    public void Save_RoundTripsEveryTabWithNoCap()
    {
        // No artificial limit on how many tabs a session may hold. 250 is well
        // past anything a person would open, and all of them must survive.
        var tabs = Enumerable.Range(0, 250)
            .Select(i => ($"https://site{i}.example/", $"Site {i}", i % 7 == 0, i == 137))
            .ToList();

        NewStore().Save(tabs);

        var loaded = NewStore().Load();
        Assert.Equal(250, loaded.Count);
        Assert.Equal("https://site0.example/", loaded[0].Url);
        Assert.Equal("https://site249.example/", loaded[249].Url);
        Assert.Equal(137, SessionStore.ResolveActiveIndex(loaded));
    }

    [Fact]
    public void Save_KeepsDuplicateUrlsInOrder()
    {
        NewStore().Save([
            ("https://same.example/", "First", false, false),
            ("https://same.example/", "Second", true, true),
            ("https://same.example/", "Third", false, false),
        ]);

        var loaded = NewStore().Load();
        Assert.Equal(new[] { "First", "Second", "Third" }, loaded.Select(e => e.Title));
        Assert.Equal(1, SessionStore.ResolveActiveIndex(loaded));
    }

    [Fact]
    public void Save_ReplacesThePreviousSnapshot()
    {
        var store = NewStore();
        store.Save([("https://one.example/", "One", false, true)]);
        store.Save([("https://two.example/", "Two", false, true)]);

        var loaded = store.Load();
        Assert.Equal("https://two.example/", Assert.Single(loaded).Url);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        NewStore().Save([("https://a.example/", "A", false, true)]);

        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
        Assert.Single(Directory.GetFiles(_dir.FullName));
    }

    [Fact]
    public void Clear_EmptiesTheSnapshotAndLeavesNoTemporaryFile()
    {
        var store = NewStore();
        store.Save([("https://a.example/", "A", false, true)]);

        store.Clear();

        Assert.Empty(NewStore().Load());
        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void Clear_DoesNotRemoveTheFile()
    {
        // A deleted file and an empty snapshot behave the same to Load, but
        // keeping the file means the state of the profile is visible on disk.
        NewStore().Clear();
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void CorruptSnapshot_LoadsEmptyWithoutThrowing()
    {
        File.WriteAllText(_path, "not json at all");
        Assert.Empty(NewStore().Load());
    }

    [Fact]
    public void SnapshotAndClosedHistoryUseDifferentFiles()
    {
        var closedPath = Path.Combine(_dir.FullName, "closed-tabs.json");
        var closed = new ClosedTabStore(closedPath, ClosedTabStore.DefaultCapacity);

        NewStore().Save([("https://open.example/", "Open", false, true)]);
        closed.Record("https://closed.example/", "Closed", false);
        NewStore().Clear();

        Assert.True(File.Exists(closedPath));
        Assert.Equal("https://closed.example/", new ClosedTabStore(closedPath, 50).TakeNewest()!.Url);
    }
}