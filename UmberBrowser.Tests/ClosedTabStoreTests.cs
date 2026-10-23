using System;
using System.IO;
using System.Text.Json;
using UmberBrowser.Models;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// The closed tab history is what Ctrl+Shift+T reads. These tests use a real
/// file in a temp directory and a real store, including a second store instance
/// standing in for the next process after a restart.
/// </summary>
public sealed class ClosedTabStoreTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("umber-closed-tabs");
    private readonly string _path;

    public ClosedTabStoreTests()
    {
        _path = Path.Combine(_dir.FullName, "closed-tabs.json");
    }

    public void Dispose() => _dir.Delete(true);

    private ClosedTabStore NewStore(int capacity = ClosedTabStore.DefaultCapacity) => new(_path, capacity);

    [Fact]
    public void NoFile_LoadsEmpty()
    {
        Assert.Empty(NewStore().Load());
        Assert.Equal(0, NewStore().Count);
    }

    [Fact]
    public void Record_AppendsNewestLast()
    {
        var store = NewStore();
        store.Record("https://a.example/", "A", false);
        store.Record("https://b.example/", "B", true);
        store.Record("https://c.example/", "C", false);

        Assert.Equal(
            new[] { "https://a.example/", "https://b.example/", "https://c.example/" },
            store.Load().Select(r => r.Url));
    }

    [Fact]
    public void Order_IsInTheStoredBytes_NotJustTheReader()
    {
        // A reader that never saw the writes still gets the right order.
        var store = NewStore();
        store.Record("https://one.example/", "One", false);
        store.Record("https://two.example/", "Two", false);
        store.Record("https://three.example/", "Three", false);

        var raw = JsonDocument.Parse(File.ReadAllText(_path)).RootElement;
        var urls = raw.EnumerateArray().Select(e => e.GetProperty("url").GetString()).ToArray();
        Assert.Equal(new[] { "https://one.example/", "https://two.example/", "https://three.example/" }, urls);
    }

    [Fact]
    public void TakeNewest_ReturnsNewestAndConsumesOnlyThatEntry()
    {
        var store = NewStore();
        store.Record("https://one.example/", "One", false);
        store.Record("https://two.example/", "Two", true);
        store.Record("https://three.example/", "Three", false);

        var newest = store.TakeNewest();

        Assert.NotNull(newest);
        Assert.Equal("https://three.example/", newest!.Url);
        Assert.Equal("Three", newest.Title);
        Assert.False(newest.WasPinned);
        Assert.Equal(
            new[] { "https://one.example/", "https://two.example/" },
            store.Load().Select(r => r.Url));
    }

    [Fact]
    public void TakeNewest_PinAndTitleSurviveTheRoundTrip()
    {
        var store = NewStore();
        store.Record("https://pinned.example/", "Pinned Tab", true);

        var reopened = NewStore().TakeNewest();

        Assert.NotNull(reopened);
        Assert.True(reopened!.WasPinned);
        Assert.Equal("Pinned Tab", reopened.Title);
    }

    [Fact]
    public void TakeNewest_OnEmptyHistory_ReturnsNullAndWritesNothing()
    {
        var store = NewStore();
        Assert.Null(store.TakeNewest());
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void RepeatedTakeNewest_ComesBackInClosureOrder()
    {
        var store = NewStore();
        store.Record("https://first.example/", "First", false);
        store.Record("https://second.example/", "Second", false);
        store.Record("https://third.example/", "Third", false);

        // Ctrl+Shift+T pressed three times: third, second, first.
        Assert.Equal("https://third.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://second.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://first.example/", store.TakeNewest()!.Url);
        Assert.Null(store.TakeNewest());
    }

    [Fact]
    public void HistorySurvivesRestart()
    {
        // First run closes two tabs, then the process ends.
        var firstRun = NewStore();
        firstRun.Record("https://alpha.example/", "Alpha", false);
        firstRun.Record("https://beta.example/", "Beta", true);

        // Second run, fresh store instance reading the same file.
        var secondRun = NewStore();
        Assert.Equal(2, secondRun.Count);
        Assert.Equal("https://beta.example/", secondRun.TakeNewest()!.Url);
        Assert.Equal("https://alpha.example/", secondRun.TakeNewest()!.Url);
    }

    [Fact]
    public void DuplicateUrls_AreKeptAsSeparateClosures()
    {
        // Two closes of the same URL must not collapse into one entry, and taking
        // the newest must not consume the older one.
        var store = NewStore();
        store.Record("https://same.example/", "Same", false);
        store.Record("https://same.example/", "Same", false);
        store.Record("https://other.example/", "Other", false);

        Assert.Equal(3, store.Count);
        Assert.Equal("https://other.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://same.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://same.example/", store.TakeNewest()!.Url);
    }

    [Fact]
    public void Capacity_TrimsOldestFirstAndKeepsNewest()
    {
        var store = NewStore(capacity: 3);
        for (var i = 1; i <= 6; i++)
            store.Record($"https://t{i}.example/", $"T{i}", false);

        Assert.Equal(
            new[] { "https://t4.example/", "https://t5.example/", "https://t6.example/" },
            store.Load().Select(r => r.Url));
    }

    [Fact]
    public void Record_KeepsAVeryLongUrlWhole()
    {
        // Clipping a URL would reopen the tab somewhere the user never asked for.
        // A query string can easily run past any fixed ceiling, so the whole
        // address has to survive.
        var store = NewStore();
        var longUrl = "https://search.example/results?q=" + new string('x', 20000);
        store.Record(longUrl, "Long", false);

        var record = Assert.Single(store.Load());
        Assert.Equal(longUrl, record.Url);
        Assert.Equal(20000 + "https://search.example/results?q=".Length, record.Url.Length);
    }

    [Fact]
    public void Record_KeepsVeryLongUrlThroughTakeNewest()
    {
        var store = NewStore();
        var longUrl = "https://api.example/item/" + new string('y', 12000) + "?detail=full";
        store.Record(longUrl, "Long", false);

        var reopened = NewStore().TakeNewest();
        Assert.NotNull(reopened);
        Assert.Equal(longUrl, reopened!.Url);
    }

    [Fact]
    public void Record_ClipsTitleBecauseTitleIsCosmetic()
    {
        // Only the label is clipped. Clipping a title changes how a reopened tab
        // reads, never where it goes.
        var store = NewStore();
        store.Record("https://title.example/", new string('t', 4000), false);

        var record = Assert.Single(store.Load());
        Assert.Equal(512, record.Title.Length);
        Assert.Equal("https://title.example/", record.Url);
    }

    [Fact]
    public void Record_IgnoresNonAbsoluteUrl()
    {
        // Reopening a relative or unparseable address cannot produce the tab the
        // user closed, so no record is created rather than a broken one.
        var store = NewStore();
        store.Record("not a url at all", "Bad", false);
        store.Record("/relative/path", "Bad", false);
        store.Record("javascript:void(0)", "Bad", false);
        store.Record("data:text/html,hi", "Bad", false);

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Record_AcceptsInternalAndFileUrls()
    {
        var store = NewStore();
        store.Record("internal://settings", "Settings", false);
        store.Record("file:///c:/temp/page.html", "Local", false);

        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void LegacyHistoryWithOnlyUrlAndTitleStillLoads()
    {
        // The schema already on disk from before the pin and timestamp fields
        // were added carries url and title only. Those entries must still load,
        // keep their order, and default to not pinned.
        File.WriteAllText(_path,
            "[{\"url\":\"https://one.example/\",\"title\":\"One\"}," +
            "{\"url\":\"https://two.example/\",\"title\":\"Two\"}," +
            "{\"url\":\"https://one.example/\",\"title\":\"One again\"}]");

        var store = NewStore();
        var loaded = store.Load();

        Assert.Equal(3, loaded.Count);
        Assert.Equal(new[] { "https://one.example/", "https://two.example/", "https://one.example/" },
            loaded.Select(r => r.Url));
        Assert.All(loaded, r => Assert.False(r.WasPinned));

        // Order is the file's order, so the newest closure is the last element
        // even when it repeats an earlier URL.
        var newest = store.TakeNewest();
        Assert.Equal("https://one.example/", newest!.Url);
        Assert.Equal("One again", newest.Title);
    }

    [Fact]
    public void Record_IgnoresEmptyUrl()
    {
        var store = NewStore();
        store.Record("", "Nothing", false);
        store.Record("   ", "Nothing", false);

        Assert.Equal(0, store.Count);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Record_UsesUtcCloseTime()
    {
        var store = NewStore();
        var before = DateTime.UtcNow.AddSeconds(-1);
        store.Record("https://a.example/", "A", false);
        var after = DateTime.UtcNow.AddSeconds(1);

        var record = Assert.Single(store.Load());
        Assert.InRange(record.ClosedAtUtc, before, after);
    }

    [Fact]
    public void CorruptFile_LoadsEmptyWithoutThrowing()
    {
        File.WriteAllText(_path, "{ this is not json");
        Assert.Empty(NewStore().Load());
    }

    [Fact]
    public void EntriesWithoutUrl_AreDroppedOnRead()
    {
        File.WriteAllText(_path, "[{\"url\":\"\",\"title\":\"ghost\"},{\"url\":\"https://real.example/\",\"title\":\"Real\"}]");

        var record = Assert.Single(NewStore().Load());
        Assert.Equal("https://real.example/", record.Url);
    }

    [Fact]
    public void Write_LeavesNoTemporaryFileBehind()
    {
        var store = NewStore();
        store.Record("https://a.example/", "A", false);

        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
        Assert.Single(Directory.GetFiles(_dir.FullName));
    }

    [Fact]
    public void Load_DoesNotWriteTheFile()
    {
        var store = NewStore();
        store.Record("https://a.example/", "A", false);
        var afterRecord = File.GetLastWriteTimeUtc(_path);

        File.SetLastWriteTimeUtc(_path, afterRecord.AddSeconds(-30));
        var marker = File.GetLastWriteTimeUtc(_path);

        store.Load();
        store.Load();
        _ = store.Count;

        // Reading the history must not touch it. A write here would rewrite the
        // file with an identical body and reset the user's mtime for no reason.
        Assert.Equal(marker, File.GetLastWriteTimeUtc(_path));
    }

    [Fact]
    public void Load_HandsBackACopyTheCallerCannotCorrupt()
    {
        var store = NewStore();
        store.Record("https://a.example/", "A", false);

        var loaded = store.Load();
        ((List<ClosedTabRecord>)loaded).Clear();

        Assert.Equal(1, store.Count);
        Assert.Single(store.Load());
    }

    [Fact]
    public void ClearAndDelete_RemovesTheFileAndTheTmpLeftover()
    {
        var store = NewStore();
        store.Record("https://a.example/", "A", false);
        File.WriteAllText(_path + ".tmp", "partial");

        store.ClearAndDelete();

        Assert.False(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
        Assert.Equal(0, store.Count);
        Assert.Null(NewStore().TakeNewest());
    }

    [Fact]
    public void ClearAndDelete_IsSafeWhenNothingWasWritten()
    {
        var store = NewStore();
        store.ClearAndDelete();
        Assert.False(File.Exists(_path));
    }

    // ─── durability and failure semantics ───

/// <summary>
/// A store whose durable write can be made to fail on demand, so the rollback
/// behaviour is exercised directly rather than inferred. The fault lives in the
/// writer, so nothing about the product path changes.
/// </summary>
private sealed class FaultyWriter
{
    private readonly Action<string, string>? _inner;
    public int Calls { get; private set; }
    public bool FailNow { get; set; }

    public FaultyWriter(Action<string, string>? inner = null) => _inner = inner;

    public void Write(string path, string content)
    {
        Calls++;
        if (FailNow) throw new IOException("injected write fault");
        _inner?.Invoke(path, content);
        UmberBrowser.Helpers.AtomicFileWriter.WriteAllText(path, content);
    }
}

[Fact]
public void Record_ReportsFailureAndLeavesTheFileUnchanged()
{
    File.WriteAllText(_path, "[]");
    var fault = new FaultyWriter();
    var store = new ClosedTabStore(_path, 50, fault.Write);

    fault.FailNow = true;
    var recorded = store.Record("https://a.example/", "A", false);

    Assert.False(recorded);
    Assert.True(store.LastWriteFailed);
    Assert.Equal("[]", File.ReadAllText(_path));

    // The file is untouched and the answer is false, so nothing here claims the
    // closure was saved. It is still remembered, because the user closed a real
    // tab and this process can reopen it.
    Assert.Equal("https://a.example/", Assert.Single(store.Load()).Url);
    Assert.Equal("https://a.example/", store.TakeNewest()!.Url);
}

[Fact]
public void Record_AfterAFailedWriteSucceedsAndIsPersisted()
{
    File.WriteAllText(_path, "[]");
    var fault = new FaultyWriter();
    var store = new ClosedTabStore(_path, 50, fault.Write);

    fault.FailNow = true;
    store.Record("https://lost.example/", "Lost", false);
    fault.FailNow = false;
    store.Record("https://kept.example/", "Kept", false);

    Assert.False(store.LastWriteFailed);

    // One write carries the whole list, so the closure the failed write could not
    // save goes out with this one, oldest first. Nothing is dropped on the way.
    var reloaded = NewStore().Load();
    Assert.Equal(
        new[] { "https://lost.example/", "https://kept.example/" },
        reloaded.Select(r => r.Url));
    Assert.Equal("https://kept.example/", NewStore().TakeNewest()!.Url);
}

[Fact]
public void TakeNewest_ReturnsNullAndKeepsTheEntryWhenTheWriteFails()
{
    var store = NewStore();
    store.Record("https://a.example/", "A", false);
    store.Record("https://b.example/", "B", false);

    var fault = new FaultyWriter((p, c) => File.WriteAllText(p, c));
    var failing = new ClosedTabStore(_path, 50, fault.Write);
    failing.Load();

    fault.FailNow = true;
    var taken = failing.TakeNewest();

    // Reporting the entry would be wrong: it is still on disk and would open twice.
    Assert.Null(taken);
    Assert.True(failing.LastWriteFailed);
    Assert.Equal(2, failing.Count);
}

[Fact]
public void TakeNewest_AfterAFailedRemovalStillReopensOnNextLaunch()
{
    var fault = new FaultyWriter((p, c) => File.WriteAllText(p, c));
    var store = new ClosedTabStore(_path, 50, fault.Write);
    store.Record("https://only.example/", "Only", false);

    fault.FailNow = true;
    Assert.Null(store.TakeNewest());

    fault.FailNow = false;
    // A fresh store reading the same file is what the next launch sees.
    Assert.Equal("https://only.example/", NewStore().TakeNewest()!.Url);
}

[Fact]
public void Clear_ReportsFailureAndKeepsTheEntries()
{
    var store = NewStore();
    store.Record("https://a.example/", "A", false);
    var before = File.ReadAllText(_path);

    var fault = new FaultyWriter();
    var failing = new ClosedTabStore(_path, 50, fault.Write);
    failing.Load();

    fault.FailNow = true;
    var cleared = failing.Clear();

    Assert.False(cleared);
    Assert.True(failing.LastWriteFailed);
    Assert.Equal(before, File.ReadAllText(_path));
    Assert.Equal(1, failing.Count);
}

[Fact]
public void ClearAndDelete_ReportsFailureAndKeepsTheFile()
{
    var store = NewStore();
    store.Record("https://a.example/", "A", false);
    var contents = File.ReadAllText(_path);

    // Holding the file open without sharing makes the delete fail on Windows.
    // That is the realistic shape of a persistence error here: something else is
    // holding the file, the removal does not happen, and the closures must stay
    // so the next launch can still replay them.
    using (var handle = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        var failing = NewStore();
        failing.Load();

        var deleted = failing.ClearAndDelete();

        Assert.False(deleted);
        Assert.True(failing.LastWriteFailed);
    }

    Assert.True(File.Exists(_path));
    Assert.Equal(contents, File.ReadAllText(_path));
}

[Fact]
public void ClearAndDelete_RemovesTheFileWhenItCan()
{
    var store = NewStore();
    store.Record("https://a.example/", "A", false);

    Assert.True(store.ClearAndDelete());
    Assert.False(File.Exists(_path));
    Assert.False(store.LastWriteFailed);
}

[Fact]
public void MalformedHistoryIsNotOverwrittenByAWrite()
{
    // A history this build cannot parse is the only surviving record of whatever
    // closures are in it. Recording more must not replace those bytes with a
    // shorter list that knows nothing about them.
    var original = "{ this is not json at all";
    File.WriteAllText(_path, original);

    var store = new ClosedTabStore(_path, 50, (p, c) => File.WriteAllText(p, c));
    store.Load();
    Assert.True(store.LastReadFailed);

    var recorded = store.Record("https://new.example/", "New", false);

    Assert.False(recorded);
    Assert.Equal(original, File.ReadAllText(_path));
}

[Fact]
public void MalformedHistoryIsNotOverwrittenByAClear()
{
    var original = "{ this is not json at all";
    File.WriteAllText(_path, original);

    var store = new ClosedTabStore(_path, 50, (p, c) => File.WriteAllText(p, c));
    store.Load();

    Assert.False(store.Clear());
    Assert.False(store.ClearAndDelete());
    Assert.Equal(original, File.ReadAllText(_path));
}

[Fact]
public void UnreadableHistoryIsPreservedUntilSomethingCanReadIt()
{
    var original = "not json";
    File.WriteAllText(_path, original);

    var store = new ClosedTabStore(_path, 50, (p, c) => File.WriteAllText(p, c));
    store.Load();
    Assert.True(store.LastReadFailed);
    Assert.Equal(0, store.Count);

    // A readable file lets writes proceed again.
    File.WriteAllText(_path, "[]");
    var recovered = new ClosedTabStore(_path, 50, (p, c) => File.WriteAllText(p, c));
    Assert.True(recovered.Record("https://ok.example/", "Ok", false));
    Assert.Equal("https://ok.example/", Assert.Single(recovered.Load()).Url);
}

[Fact]
public void LockedFile_ReadFailureDoesNotThrow()
{
    // A file held open exclusively cannot be read. That is a recoverable
    // condition, not a reason to take the browser down or to destroy the file.
    using var handle = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    var store = new ClosedTabStore(_path, 50, (p, c) => File.WriteAllText(p, c));

    var loaded = store.Load();

    Assert.Empty(loaded);
    Assert.True(store.LastReadFailed);
    Assert.False(store.Record("https://a.example/", "A", false));
}

[Fact]
public void ARecoverablePersistenceErrorNeverThrows()
{
    var fault = new FaultyWriter();
    var store = new ClosedTabStore(_path, 50, fault.Write);
    fault.FailNow = true;

    // These are the calls a tab close makes. None of them may throw. Record still
    // answers false, and the reopen still hands the closure back: it never reached
    // the file, so nothing durable needs removing to consume it.
    var record = store.Record("https://a.example/", "A", false);
    var take = store.TakeNewest();
    var clear = store.Clear();
    var load = store.Load();

    Assert.False(record);
    Assert.Equal("https://a.example/", take!.Url);
    Assert.False(clear);
    Assert.Empty(load);
    Assert.True(store.LastWriteFailed);
}

[Fact]
public void FailedRemovalThenRetryRestoresChronologyNotReversal()
{
    var fault = new FaultyWriter((p, c) => File.WriteAllText(p, c));
    var store = new ClosedTabStore(_path, 50, fault.Write);
    store.Record("https://one.example/", "One", false);
    store.Record("https://two.example/", "Two", false);
    store.Record("https://three.example/", "Three", false);

    fault.FailNow = true;
    Assert.Null(store.TakeNewest());

    fault.FailNow = false;
    Assert.Equal("https://three.example/", store.TakeNewest()!.Url);
    Assert.Equal("https://two.example/", store.TakeNewest()!.Url);
    Assert.Equal("https://one.example/", store.TakeNewest()!.Url);
    Assert.Null(store.TakeNewest());
}

[Fact]
public void EveryWrite_ReplacesTheWholeFile()
    {
        // No appended fragments and no second file: a reader either sees the
        // whole previous state or the whole new one.
        var store = NewStore();
        store.Record("https://a.example/", "A", false);
        var afterFirst = File.ReadAllBytes(_path);

        store.Record("https://b.example/", "B", false);
        var afterSecond = File.ReadAllBytes(_path);

        Assert.NotEqual(afterFirst, afterSecond);
        Assert.StartsWith("[", System.Text.Encoding.UTF8.GetString(afterSecond));
    }

    [Fact]
    public void Clear_EmptiesHistoryAndLeavesReadableFile()
    {
        var store = NewStore();
        store.Record("https://a.example/", "A", false);
        store.Record("https://b.example/", "B", false);

        store.Clear();

        Assert.Empty(NewStore().Load());
        Assert.Null(NewStore().TakeNewest());
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void ClosedHistoryIsSeparateFromTheStartupSnapshot()
    {
        var sessionPath = Path.Combine(_dir.FullName, "session.json");
        var session = new SessionStore(sessionPath);
        var closed = NewStore();

        session.Save([("https://open.example/", "Open", false, true)]);
        closed.Record("https://closed.example/", "Closed", false);

        // Clearing the snapshot leaves the reopen history intact.
        session.Clear();

        Assert.Empty(new SessionStore(sessionPath).Load());
        Assert.Equal("https://closed.example/", NewStore().TakeNewest()!.Url);
    }
}