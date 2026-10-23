using System;
using System.IO;
using System.Linq;
using UmberBrowser.Helpers;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// What SessionStore reports when a write does or does not reach the disk.
///
/// The earlier shutdown tests injected an action that threw, which is not what the real
/// store does: it catches the write fault, logs it and returns. A caller that watched for
/// exceptions could not tell a saved snapshot from one that never landed. These cases run
/// the real store against real files, through the real atomic write, so the answer comes
/// from the write itself.
///
/// Every path here is inside a fresh temp directory that the constructor proves is
/// outside the installed browser's profile.
/// </summary>
public sealed class SessionStoreWriteResultTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SessionStoreWriteResultTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "UmberStoreFaults", Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_dir, "session.json");

        // Containment: no case in this class may read or write the real profile.
        Assert.False(AppPaths.IsSameOrAncestor(AppPaths.ProductionDataDirectory, _path),
            "the test profile must not sit inside the installed browser's data");
        Assert.False(AppPaths.IsSameOrAncestor(_dir, AppPaths.ProductionDataDirectory),
            "the temp profile must not contain the installed browser's data");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { /* a handle a failed case left open must not fail the run */ }
    }

    private static readonly (string Url, string Title, bool IsPinned, bool IsActive)[] Tabs =
    [
        ("https://one.example/", "One", false, true),
        ("https://one.example/", "One again", true, false)
    ];

    private SessionStore Store(Action<string, string>? write = null) => new(_path, write);

    private void SeedPreviousSnapshot()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, """[{"url":"https://old.example/","title":"Old","isPinned":false,"isActive":true}]""");
    }

    [Fact]
    public void AWriteThatLandsReportsTrueAndRoundTrips()
    {
        var store = Store();

        Assert.True(store.TrySave(Tabs));

        var loaded = store.Load();
        Assert.Equal(["https://one.example/", "https://one.example/"], loaded.Select(e => e.Url));
        Assert.Equal(["One", "One again"], loaded.Select(e => e.Title));
        Assert.Equal([false, true], loaded.Select(e => e.IsPinned));
        Assert.Equal([true, false], loaded.Select(e => e.IsActive));
        Assert.Equal(0, SessionStore.ResolveActiveIndex(loaded));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void AWriterFaultReportsFalseAndLeavesThePreviousSnapshotOnDisk()
    {
        SeedPreviousSnapshot();
        var before = File.ReadAllBytes(_path);
        var store = Store((_, _) => throw new IOException("injected writer fault"));

        Assert.False(store.TrySave(Tabs));

        Assert.Equal(before, File.ReadAllBytes(_path));
        Assert.Equal("https://old.example/", Assert.Single(store.Load()).Url);
    }

    [Fact]
    public void ALockedFileIsARealFaultAndReportsFalse()
    {
        // The real atomic write renames over the target, so holding the file open without
        // delete access stops the rename. This is the fault the store has to report.
        SeedPreviousSnapshot();
        var store = Store();
        using var held = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.False(store.TrySave(Tabs));

        held.Dispose();
        Assert.Equal("https://old.example/", Assert.Single(store.Load()).Url);
    }

    [Fact]
    public void AStoreThatAnsweredFalseWritesOnTheNextAttempt()
    {
        // False describes one attempt. The store is not poisoned by the fault it reported.
        SeedPreviousSnapshot();
        var store = Store();
        using (var held = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(store.TrySave(Tabs));
        }

        Assert.True(store.TrySave(Tabs));
        Assert.Equal(["https://one.example/", "https://one.example/"], store.Load().Select(e => e.Url));
    }

    [Fact]
    public void APathThatCannotBeCreatedReportsFalse()
    {
        // A parent that is a file, not a directory. The failure happens before any bytes
        // are written, and the store still has to answer false rather than throw.
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "not a directory");
        var store = new SessionStore(Path.Combine(blocker, "session.json"));

        Assert.False(store.TrySave(Tabs));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void ClearThatLandsReportsTrueAndEmptiesTheFile()
    {
        var store = Store();
        Assert.True(store.TrySave(Tabs));

        Assert.True(store.TryClear());

        Assert.Empty(store.Load());
        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void ClearThatCannotReachTheDiskReportsFalseAndKeepsTheRows()
    {
        var store = Store();
        Assert.True(store.TrySave(Tabs));
        using var held = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.False(store.TryClear());

        held.Dispose();
        Assert.Equal(2, store.Load().Count);
    }

    [Fact]
    public void SaveAndClearStillNeverThrow()
    {
        // The void calls other callers use keep their behaviour: a fault is logged and
        // swallowed, so a tab close or a settings tick is never taken down by a disk.
        var store = Store((_, _) => throw new IOException("injected writer fault"));

        store.Save(Tabs);
        store.Clear();

        Assert.Empty(store.Load());
    }

    [Fact]
    public void LoadReturnsTheSavedRowsWithoutValidatingOrDeduplicating()
    {
        // The read is a plain read. A row this build would never create still comes back,
        // because what is on disk is what the next launch restores.
        var store = Store();
        Assert.True(store.TrySave([
            ("", "", false, true),
            ("https://dup.example/", "Dup", true, false),
            ("https://dup.example/", "Dup", true, false)
        ]));

        var loaded = store.Load();

        Assert.Equal(3, loaded.Count);
        Assert.Equal("", loaded[0].Url);
        Assert.Equal(2, loaded.Count(e => e.Url == "https://dup.example/"));
        Assert.Equal(0, SessionStore.ResolveActiveIndex(loaded));
    }
}
