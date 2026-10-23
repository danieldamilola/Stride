using System;
using System.IO;
using System.Linq;
using UmberBrowser.Helpers;
using UmberBrowser.Models;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// What Ctrl+Shift+T can reopen after the closed tab history file refuses a write.
///
/// The store used to roll its in memory state back to match the file whenever a
/// write failed, and the tab engine ignores the answer Record gives, so a tab the
/// user closed became unreopenable for the rest of the run the moment the file
/// stopped taking writes. These cases pin the narrower rule that replaced it: a
/// closure that never reached the file is still a closure, so it stays reopenable
/// in this process, while nothing pretends it was saved.
///
/// Every case uses a real file in a fresh temp directory. The faults are injected
/// at the writer, except the clear-on-exit cases, which hold the file open so the
/// delete fails for real.
/// </summary>
public sealed class ClosedTabPendingFailureTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("umber-closed-pending");
    private readonly string _path;

    public ClosedTabPendingFailureTests()
    {
        _path = Path.Combine(_dir.FullName, "closed-tabs.json");
    }

    public void Dispose()
    {
        try { _dir.Delete(true); }
        catch { /* a handle a failed case left open must not fail the run */ }
    }

    /// <summary>
    /// A writer that counts its attempts and either always refuses or refuses until
    /// told otherwise. Successful writes are the real atomic write, so the bytes on
    /// disk are the ones a restart would read.
    /// </summary>
    private sealed class Writer
    {
        public int Calls { get; private set; }
        public bool Failing { get; set; }

        public void Write(string path, string content)
        {
            Calls++;
            if (Failing) throw new IOException("injected write fault");
            AtomicFileWriter.WriteAllText(path, content);
        }
    }

    private ClosedTabStore Store(Writer writer, int capacity = ClosedTabStore.DefaultCapacity)
        => new(_path, capacity, writer.Write);

    private static string[] Urls(ClosedTabStore store) => [.. store.Load().Select(r => r.Url)];

    /// <summary>What a fresh store reads from the file, which is what a restart sees.</summary>
    private string[] OnDisk() =>
        File.Exists(_path) ? [.. new ClosedTabStore(_path, 50).Load().Select(r => r.Url)] : [];

    [Fact]
    public void AClosureWhoseWriteFailedIsStillReopenable()
    {
        var writer = new Writer { Failing = true };
        var store = Store(writer);

        var recorded = store.Record("https://kept-open.example/", "Kept open", true);

        // False is about the file. The closure is still here.
        Assert.False(recorded);
        Assert.True(store.LastWriteFailed);
        Assert.Empty(OnDisk());

        var reopened = store.TakeNewest();
        Assert.NotNull(reopened);
        Assert.Equal("https://kept-open.example/", reopened!.Url);
        Assert.Equal("Kept open", reopened.Title);
        Assert.True(reopened.WasPinned);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void AFailedWriteNeverBlocksTheTabClose()
    {
        // The store's contract with the caller: no throw, no lost tab. The engine
        // discards the answer, so a throw here would take the close down with it.
        var writer = new Writer { Failing = true };
        var store = Store(writer);

        var exception = Record.Exception(() =>
        {
            store.Record("https://a.example/", "A", false);
            store.Record("not a url at all", "Bad", false);
            store.TakeNewest();
            store.Clear();
            store.ClearAndDelete();
            store.Load();
            _ = store.Count;
        });

        Assert.Null(exception);
    }

    [Fact]
    public void APendingClosureIsReadBackWithItsPinTitleAndWholeUrl()
    {
        var writer = new Writer { Failing = true };
        var store = Store(writer);
        var longUrl = "https://search.example/results?q=" + new string('x', 20000);

        store.Record("https://pinned.example/", new string('t', 4000), true);
        store.Record(longUrl, "Long", false);

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal(longUrl, loaded[1].Url);
        Assert.Equal(20000 + "https://search.example/results?q=".Length, loaded[1].Url.Length);
        Assert.Equal(512, loaded[0].Title.Length);
        Assert.True(loaded[0].WasPinned);
    }

    [Fact]
    public void APendingUrlThatCouldNeverBeReopenedIsNotRemembered()
    {
        // False here means no record at all, not a record held in memory. An
        // address that cannot be navigated to would reopen as a different tab.
        var writer = new Writer { Failing = true };
        var store = Store(writer);

        store.Record("javascript:void(0)", "Bad", false);
        store.Record("/relative/path", "Bad", false);
        store.Record("", "Bad", false);

        Assert.Equal(0, store.Count);
        Assert.Null(store.TakeNewest());
    }

    [Fact]
    public void PendingClosuresComeBackInClosureOrder()
    {
        var writer = new Writer { Failing = true };
        var store = Store(writer);
        store.Record("https://first.example/", "First", false);
        store.Record("https://second.example/", "Second", false);
        store.Record("https://third.example/", "Third", false);

        // Three Ctrl+Shift+T presses with the file refusing every one of them.
        Assert.Equal("https://third.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://second.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://first.example/", store.TakeNewest()!.Url);
        Assert.Null(store.TakeNewest());
    }

    [Fact]
    public void TheSameUrlClosedTwiceWhilePendingIsTwoClosures()
    {
        var writer = new Writer { Failing = true };
        var store = Store(writer);
        store.Record("https://same.example/", "Same", false);
        store.Record("https://same.example/", "Same", false);

        Assert.Equal(2, store.Count);
        Assert.Equal("https://same.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://same.example/", store.TakeNewest()!.Url);
        Assert.Null(store.TakeNewest());
    }

    [Fact]
    public void TakingAPendingClosureWritesNothing()
    {
        var writer = new Writer { Failing = true };
        var store = Store(writer);
        store.Record("https://never-saved.example/", "Never saved", false);
        var afterRecord = writer.Calls;

        var reopened = store.TakeNewest();

        // The entry was never on disk, so consuming it owes the file no write. This
        // is what makes the fallback work while the writer is still broken.
        Assert.Equal("https://never-saved.example/", reopened!.Url);
        Assert.Equal(afterRecord, writer.Calls);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void AConsumedPendingClosureNeverComesBack()
    {
        var writer = new Writer { Failing = true };
        var store = Store(writer);
        store.Record("https://first.example/", "First", false);
        store.Record("https://second.example/", "Second", false);

        // The newest closure is the one that goes first.
        Assert.Equal("https://second.example/", store.TakeNewest()!.Url);

        writer.Failing = false;
        Assert.True(store.Record("https://later.example/", "Later", false));

        // The write carried what was left, and nothing brought the consumed entry
        // back, on disk or in memory.
        Assert.Equal(
            new[] { "https://first.example/", "https://later.example/" },
            OnDisk());
        Assert.Equal("https://later.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://first.example/", store.TakeNewest()!.Url);
        Assert.Null(store.TakeNewest());
    }

    [Fact]
    public void ADiskClosureIsNotHandedOutWhileItsRemovalFailed()
    {
        // The other half of the rule. A closure that is on disk must not be
        // reported as consumed while the file still holds it, because the next
        // launch would open it a second time.
        var writer = new Writer();
        var store = Store(writer);
        store.Record("https://saved.example/", "Saved", false);
        Assert.True(File.Exists(_path));

        writer.Failing = true;
        Assert.Null(store.TakeNewest());
        Assert.True(store.LastWriteFailed);

        Assert.Equal("https://saved.example/", Assert.Single(OnDisk()));
        Assert.Equal("https://saved.example/", Assert.Single(store.Load()).Url);
    }

    [Fact]
    public void AFailedRemovalIsNotAnsweredFromMemoryEither()
    {
        var writer = new Writer();
        var store = Store(writer);
        store.Record("https://one.example/", "One", false);
        store.Record("https://two.example/", "Two", false);

        writer.Failing = true;

        // Both are on disk, so neither may come back while the removal cannot land.
        Assert.Null(store.TakeNewest());
        Assert.Equal(2, store.Count);
        Assert.Null(store.TakeNewest());
        Assert.Equal(2, store.Count);

        writer.Failing = false;
        Assert.Equal("https://two.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://one.example/", store.TakeNewest()!.Url);
    }

    [Fact]
    public void TheNextWriteThatLandsSavesThePendingClosuresInOrder()
    {
        var writer = new Writer();
        var store = Store(writer);
        store.Record("https://saved-one.example/", "Saved one", false);

        writer.Failing = true;
        store.Record("https://pending-one.example/", "Pending one", false);
        store.Record("https://pending-two.example/", "Pending two", true);

        writer.Failing = false;
        store.Record("https://saved-two.example/", "Saved two", false);

        // One atomic write carries all four, oldest first, pins intact.
        Assert.Equal(
            new[] { "https://saved-one.example/", "https://pending-one.example/", "https://pending-two.example/", "https://saved-two.example/" },
            OnDisk());
        Assert.True(Assert.Single(
            new ClosedTabStore(_path, 50).Load(),
            r => r.Url == "https://pending-two.example/").WasPinned);
    }

    [Fact]
    public void AFailedClearForgetsNothingOfEitherKind()
    {
        var writer = new Writer();
        var store = Store(writer);
        store.Record("https://saved.example/", "Saved", false);

        writer.Failing = true;
        store.Record("https://pending.example/", "Pending", false);
        Assert.False(store.Clear());

        // The file still holds the saved closure and the memory only one is still
        // reopenable, in the order they were closed.
        Assert.Equal("https://saved.example/", Assert.Single(OnDisk()));
        Assert.Equal(
            new[] { "https://saved.example/", "https://pending.example/" },
            Urls(store));

        // The one that never reached the file is handed over while the writer is
        // still refusing, because consuming it owes the file nothing.
        Assert.Equal("https://pending.example/", store.TakeNewest()!.Url);

        // The one that is on disk is not, because that removal cannot land.
        Assert.Null(store.TakeNewest());
        Assert.Equal("https://saved.example/", Assert.Single(OnDisk()));
    }

    [Fact]
    public void AFailedClearDoesNotDiscardAPendingAdditionRecordedEarlier()
    {
        var writer = new Writer { Failing = true };
        var store = Store(writer);
        store.Record("https://first.example/", "First", false);
        store.Record("https://second.example/", "Second", false);

        Assert.False(store.Clear());

        Assert.Equal(
            new[] { "https://first.example/", "https://second.example/" },
            Urls(store));
    }

    [Fact]
    public void ASuccessfulClearForgetsTheSavedAndThePendingAlike()
    {
        var writer = new Writer();
        var store = Store(writer);
        store.Record("https://saved.example/", "Saved", false);

        writer.Failing = true;
        store.Record("https://pending.example/", "Pending", false);

        writer.Failing = false;
        Assert.True(store.Clear());

        Assert.Empty(OnDisk());
        Assert.Empty(store.Load());
        Assert.Null(store.TakeNewest());
    }

    [Fact]
    public void AFailedClearOnExitKeepsBothKindsAndAnswersFalse()
    {
        var writer = new Writer();
        var store = Store(writer);
        store.Record("https://saved.example/", "Saved", false);
        var savedBytes = File.ReadAllText(_path);

        writer.Failing = true;
        store.Record("https://pending.example/", "Pending", false);

        // The delete itself is what a real exit does. Holding the file open without
        // sharing makes it fail on Windows, which is the shape of a real fault.
        using (var handle = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var deleted = new ClosedTabStore(_path, 50).ClearAndDelete();
            Assert.False(deleted);
        }

        Assert.Equal(savedBytes, File.ReadAllText(_path));
        Assert.Equal("https://saved.example/", Assert.Single(new ClosedTabStore(_path, 50).Load()).Url);
    }

    [Fact]
    public void AFailedClearOnExitKeepsThePendingClosureForThisRun()
    {
        var writer = new Writer();
        var store = Store(writer);
        store.Record("https://saved.example/", "Saved", false);

        writer.Failing = true;
        store.Record("https://pending.example/", "Pending", false);
        writer.Failing = false;

        // The delete is the part that has to fail, so this is a real fault: the
        // file is held open without sharing while the exit asks for it gone.
        using (var handle = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(store.ClearAndDelete());

        // The delete never happened, so both closures are still remembered here.
        Assert.True(File.Exists(_path));
        Assert.Equal(
            new[] { "https://saved.example/", "https://pending.example/" },
            Urls(store));
        Assert.Equal("https://pending.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://saved.example/", Assert.Single(OnDisk()));
    }

    [Fact]
    public void ASuccessfulClearOnExitForgetsThePendingClosureToo()
    {
        var writer = new Writer();
        var store = Store(writer);
        store.Record("https://saved.example/", "Saved", false);

        writer.Failing = true;
        store.Record("https://pending.example/", "Pending", false);

        writer.Failing = false;
        Assert.True(store.ClearAndDelete());

        Assert.False(File.Exists(_path));
        Assert.Empty(store.Load());
        Assert.Null(store.TakeNewest());
    }

    [Fact]
    public void ACorruptHistoryKeepsItsBytesAndStillReopensThisRun()
    {
        // Two promises at once. The unreadable bytes are the only surviving record
        // of whatever closures are in them, so no write may replace them. The tab
        // the user just closed is still reopenable, because that is a separate
        // promise and it costs the file nothing.
        const string original = "{ this is not json at all";
        File.WriteAllText(_path, original);

        var writer = new Writer { Failing = true };
        var store = Store(writer);
        store.Load();
        Assert.True(store.LastReadFailed);

        Assert.False(store.Record("https://new.example/", "New", false));
        Assert.Equal(original, File.ReadAllText(_path));
        Assert.Equal("https://new.example/", store.TakeNewest()!.Url);

        // Still refusing to write while the file stays unreadable.
        Assert.False(store.Record("https://second.example/", "Second", false));
        Assert.Equal(original, File.ReadAllText(_path));
    }

    [Fact]
    public void ACorruptHistoryIsStillNotReplacedByAHistoryBuiltWithoutIt()
    {
        const string original = "{ this is not json at all";
        File.WriteAllText(_path, original);

        var writer = new Writer();
        var store = Store(writer);
        store.Load();

        // The writer is healthy here. The refusal is about the unreadable bytes.
        Assert.False(store.Record("https://new.example/", "New", false));
        Assert.False(store.Clear());
        Assert.False(store.ClearAndDelete());
        Assert.Equal(original, File.ReadAllText(_path));
    }

    [Fact]
    public void PendingClosuresStayInsideTheFiftyEntryBound()
    {
        var writer = new Writer { Failing = true };
        var store = Store(writer, capacity: 3);
        for (var i = 1; i <= 6; i++)
            store.Record($"https://t{i}.example/", $"T{i}", false);

        Assert.Equal(3, store.Count);
        Assert.Equal(
            new[] { "https://t4.example/", "https://t5.example/", "https://t6.example/" },
            Urls(store));
        Assert.Equal("https://t6.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://t5.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://t4.example/", store.TakeNewest()!.Url);
        Assert.Null(store.TakeNewest());
    }

    [Fact]
    public void TheBoundStillTrimsTheSavedClosuresWhenAPendingOneIsFlushed()
    {
        var writer = new Writer();
        var store = Store(writer, capacity: 3);
        store.Record("https://t1.example/", "T1", false);
        store.Record("https://t2.example/", "T2", false);

        writer.Failing = true;
        store.Record("https://t3.example/", "T3", false);

        writer.Failing = false;
        Assert.True(store.Record("https://t4.example/", "T4", false));

        Assert.Equal(
            new[] { "https://t2.example/", "https://t3.example/", "https://t4.example/" },
            OnDisk());
    }

    [Fact]
    public void EveryWindowInTheRunSharesTheSameFallback()
    {
        // The engine is scoped per window, the store is one singleton for the
        // process. A closure the first window closed while the file refused is
        // therefore still reopenable when the second window reopens, and a removal
        // the file refuses is refused for both rather than answered from one
        // window's private view.
        var writer = new Writer();
        IClosedTabStore firstWindow = Store(writer);
        IClosedTabStore secondWindow = firstWindow;

        firstWindow.Record("https://saved.example/", "Saved", false);

        writer.Failing = true;
        firstWindow.Record("https://pending.example/", "Pending", false);

        Assert.Equal("https://pending.example/", secondWindow.TakeNewest()!.Url);
        Assert.Null(firstWindow.TakeNewest());
        Assert.Equal("https://saved.example/", Assert.Single(OnDisk()));
    }

    [Fact]
    public void ARecoverableWriterFaultLosesNothingAcrossTheRecovery()
    {
        // One writer that refuses three times and then works, which is what a
        // transient fault looks like: the file comes back and every closure is
        // still there.
        var writer = new Writer { Failing = true };
        var store = Store(writer);

        Assert.False(store.Record("https://one.example/", "One", false));
        Assert.False(store.Record("https://two.example/", "Two", false));
        Assert.Equal("https://two.example/", store.TakeNewest()!.Url);
        Assert.False(store.Record("https://three.example/", "Three", false));

        writer.Failing = false;
        Assert.True(store.Record("https://four.example/", "Four", false));

        Assert.Equal(
            new[] { "https://one.example/", "https://three.example/", "https://four.example/" },
            OnDisk());
    }

    [Fact]
    public void ReadingTheHistoryNeverWritesItAndNeverTouchesAPendingClosure()
    {
        var writer = new Writer();
        var store = Store(writer);
        store.Record("https://saved.example/", "Saved", false);

        writer.Failing = true;
        store.Record("https://pending.example/", "Pending", false);
        var calls = writer.Calls;

        var loaded = store.Load();
        _ = store.Count;
        _ = loaded.Count;
        ((System.Collections.Generic.List<ClosedTabRecord>)store.Load()).Clear();

        Assert.Equal(calls, writer.Calls);
        Assert.Equal(2, store.Count);
        Assert.Equal("https://pending.example/", store.TakeNewest()!.Url);
    }
}