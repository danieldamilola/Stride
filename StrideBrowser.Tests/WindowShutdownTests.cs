using System.IO;
using StrideBrowser.Services;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Exercises the order a window close writes in, with the stores replaced by recording
/// fakes. The point is the ordering under failure and how each reported result is carried,
/// not the write itself: the real stores are exercised in WindowShutdownRealStoreTests.
/// </summary>
public sealed class WindowShutdownWriteTests
{
    private sealed class Recorder
    {
        public List<string> Calls { get; } = [];
        public void Record(string call) => Calls.Add(call);
    }

    private static WindowShutdownWrites Build(
        Recorder log,
        bool writeSnapshot = true,
        bool clearBrowsingData = false,
        Func<Task>? browse = null,
        Func<bool>? clearHistory = null,
        Action? save = null,
        Func<bool>? saveResult = null,
        Func<bool>? snapshotResult = null)
        => new()
        {
            WriteSnapshot = writeSnapshot,
            ClearBrowsingData = clearBrowsingData,
            WriteSnapshotRows = () => { log.Record("snapshot"); return snapshotResult?.Invoke() ?? true; },
            ClearSnapshot = () => { log.Record("snapshot-clear"); return snapshotResult?.Invoke() ?? true; },
            ClearClosedTabHistory = () => { log.Record("closed-history"); return clearHistory?.Invoke() ?? true; },
            SaveSettings = () => { log.Record("settings"); save?.Invoke(); return saveResult?.Invoke() ?? true; },
            ClearBrowsingDataAsync = browse ?? (() => { log.Record("browse"); return Task.CompletedTask; })
        };

    [Fact]
    public async Task BrowserCleanupRunsBeforeTheHistoryAndSettings()
    {
        var log = new Recorder();
        var result = await Build(log, clearBrowsingData: true).RunAsync();

        Assert.Equal(["snapshot", "browse", "closed-history", "settings"], log.Calls);
        Assert.True(result.Complete);
    }

    [Fact]
    public async Task BrowserCleanupTimeoutStillClearsHistoryAndSavesSettings()
    {
        // The real cleanup is bounded by a three second deadline. A timeout here used to
        // jump out of the whole write block, leaving closed tabs and settings unwritten.
        var log = new Recorder();
        var result = await Build(log, clearBrowsingData: true,
            browse: () => { log.Record("browse"); throw new TimeoutException("deadline"); }).RunAsync();

        Assert.Equal(["snapshot", "browse", "closed-history", "settings"], log.Calls);
        Assert.False(result.BrowsingDataCleared);
        Assert.True(result.ClosedTabHistoryCleared);
        Assert.True(result.SettingsSaved);
        Assert.True(result.RecoveryStateIntact);
        Assert.False(result.Complete);
    }

    [Fact]
    public async Task BrowserCleanupComFailureStillClearsHistoryAndSavesSettings()
    {
        var log = new Recorder();
        var result = await Build(log, clearBrowsingData: true,
            browse: () => throw new InvalidOperationException("RPC_E_DISCONNECTED")).RunAsync();

        Assert.True(result.ClosedTabHistoryCleared);
        Assert.True(result.SettingsSaved);
        Assert.False(result.BrowsingDataCleared);
    }

    [Fact]
    public async Task CancelledBrowserCleanupStillWritesEverything()
    {
        var log = new Recorder();
        var result = await Build(log, clearBrowsingData: true,
            browse: () => { log.Record("browse"); return Task.FromCanceled(new CancellationToken(true)); }).RunAsync();

        Assert.Equal(["snapshot", "browse", "closed-history", "settings"], log.Calls);
        Assert.False(result.BrowsingDataCleared);
        Assert.True(result.ClosedTabHistoryCleared);
    }

    [Fact]
    public async Task HistoryClearReportingFalseIsRecordedNotSwallowed()
    {
        // A store that answers false did not reach its file. That has to be visible, not
        // folded into a green result.
        var log = new Recorder();
        var result = await Build(log, clearBrowsingData: true, clearHistory: () => false).RunAsync();

        Assert.True(result.BrowsingDataCleared);
        Assert.True(result.SettingsSaved);
        Assert.False(result.ClosedTabHistoryCleared);
        Assert.False(result.Complete);
    }

    [Fact]
    public async Task HistoryClearThatThrowsDoesNotStopSettings()
    {
        var log = new Recorder();
        var result = await Build(log, clearBrowsingData: true,
            clearHistory: () => throw new IOException("locked")).RunAsync();

        Assert.Equal(["snapshot", "browse", "closed-history", "settings"], log.Calls);
        Assert.False(result.ClosedTabHistoryCleared);
        Assert.True(result.SettingsSaved);
    }

    [Fact]
    public async Task SnapshotWriteFailureIsReportedAndSettingsStillSave()
    {
        var log = new Recorder();
        var writes = new WindowShutdownWrites
        {
            WriteSnapshot = true,
            ClearBrowsingData = false,
            WriteSnapshotRows = () => throw new IOException("disk full"),
            ClearSnapshot = () => { log.Record("snapshot-clear"); return true; },
            ClearClosedTabHistory = () => true,
            SaveSettings = () => { log.Record("settings"); return true; },
            ClearBrowsingDataAsync = () => Task.CompletedTask
        };

        var result = await writes.RunAsync();

        Assert.Equal(["settings"], log.Calls);
        Assert.False(result.SessionSnapshotWritten);
        Assert.True(result.SettingsSaved);
        Assert.False(result.RecoveryStateIntact);
    }

    [Fact]
    public async Task SettingsSaveFailureIsReported()
    {
        var log = new Recorder();
        var result = await Build(log, save: () => throw new IOException("read only")).RunAsync();

        Assert.False(result.SettingsSaved);
        Assert.True(result.SessionSnapshotWritten);
        Assert.False(result.RecoveryStateIntact);
    }

    [Fact]
    public async Task SnapshotStoreAnsweringFalseIsRecordedNotSwallowed()
    {
        // The stores swallow write faults and cannot throw, so a close that only watched
        // for exceptions reported a saved snapshot that was never on disk. A store that
        // answers false has to turn the result red on its own.
        var log = new Recorder();
        var result = await Build(log, snapshotResult: () => false).RunAsync();

        Assert.Equal(["snapshot", "settings"], log.Calls);
        Assert.False(result.SessionSnapshotWritten);
        Assert.True(result.SettingsSaved);
        Assert.False(result.RecoveryStateIntact);
        Assert.False(result.Complete);
    }

    [Fact]
    public async Task SettingsStoreAnsweringFalseIsRecordedNotSwallowed()
    {
        var log = new Recorder();
        var result = await Build(log, saveResult: () => false).RunAsync();

        Assert.False(result.SettingsSaved);
        Assert.True(result.SessionSnapshotWritten);
        Assert.False(result.RecoveryStateIntact);
        Assert.False(result.Complete);
    }

    [Fact]
    public async Task RestoreOffAnswersFalseWhenTheClearDoesNotReachTheStore()
    {
        var log = new Recorder();
        var result = await Build(log, writeSnapshot: false, snapshotResult: () => false).RunAsync();

        Assert.Equal(["snapshot-clear", "settings"], log.Calls);
        Assert.False(result.SessionSnapshotWritten);
        Assert.True(result.SettingsSaved);
    }

    [Fact]
    public async Task RestoreOffEmptiesTheSnapshotAndTouchesNoBrowserState()
    {
        var log = new Recorder();
        var result = await Build(log, writeSnapshot: false, clearBrowsingData: false).RunAsync();

        Assert.Equal(["snapshot-clear", "settings"], log.Calls);
        Assert.True(result.SessionSnapshotWritten);
        Assert.True(result.ClosedTabHistoryCleared);
        Assert.True(result.SettingsSaved);
    }

    [Fact]
    public async Task NoDataClearMeansNoHistoryClearAndNoBrowserClear()
    {
        var log = new Recorder();
        var result = await Build(log, clearBrowsingData: false).RunAsync();

        Assert.Equal(["snapshot", "settings"], log.Calls);
        Assert.True(result.Complete);
    }
}

/// <summary>
/// The destination window of a dock is waited on before anything moves. A window that
/// closes during that wait has to read as an aborted transfer, not as a fault.
/// </summary>
public sealed class DockReadyTests
{
    [Fact]
    public async Task ReadyWindowIsAccepted()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ready.SetResult();
        Assert.True(await BrowserWindowManager.WaitForReadyAsync(ready.Task, "w"));
    }

    [Fact]
    public async Task ClosingWindowIsAnAbortedTransferNotAFault()
    {
        // This is exactly what Close does to a window before it tears it down.
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ready.TrySetCanceled();

        Assert.False(await BrowserWindowManager.WaitForReadyAsync(ready.Task, "w"));
    }

    [Fact]
    public async Task ClosedBeforeLoadedAlsoReadsAsAborted()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = BrowserWindowManager.WaitForReadyAsync(ready.Task, "w");
        ready.TrySetCanceled();

        Assert.False(await waiter);
    }

    [Fact]
    public async Task WindowThatFailedToLoadIsStillAFault()
    {
        // Swallowing this would hide a broken window behind a silent "aborted".
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ready.SetException(new InvalidOperationException("engine init failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BrowserWindowManager.WaitForReadyAsync(ready.Task, "w"));
    }
}

/// <summary>
/// The startup snapshot against a real session file, so "latest wins" and the row shape
/// are measured rather than asserted.
/// </summary>
public sealed class SessionSnapshotFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "StrideSnapshotTests", Guid.NewGuid().ToString("N"));

    private ISessionStore NewStore() => new SessionStore(Path.Combine(_dir, "session.json"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { /* a locked temp file must not fail the run */ }
    }

    private static SnapshotSource Window(string name, string? activeId, bool closing, params (string, string, string, bool)[] tabs) =>
        new(name, tabs, activeId, closing);

    private static (string, string, string, bool) Tab(string id, string url, string title = "t", bool pin = false) => (id, url, title, pin);

    [Fact]
    public void SequentialClosesDoNotAccumulateAbandonedCohorts()
    {
        var a = Window("A", "a2", closing: false, Tab("a1", "https://a1"), Tab("a2", "https://a2"));
        var b = Window("B", "b1", closing: false, Tab("b1", "https://b1"));
        var store = NewStore();

        // A closes first. B is still open, so the snapshot is B alone.
        var afterFirst = SessionSnapshot.Cohort([a with { Closing = true }, b], "A");
        Assert.Equal(["B"], afterFirst.Select(w => w.Name));
        store.Save(SessionSnapshot.Rows(afterFirst));
        Assert.Equal(["https://b1"], store.Load().Select(e => e.Url));

        // A is then unregistered and B closes last. A must not reappear.
        var afterSecond = SessionSnapshot.Cohort([b with { Closing = true }], "B");
        Assert.Equal(["B"], afterSecond.Select(w => w.Name));
        store.Save(SessionSnapshot.Rows(afterSecond));
        Assert.Equal(["https://b1"], store.Load().Select(e => e.Url));
    }

    [Fact]
    public void TwoWindowsClosingTogetherEndOnTheOneThatWroteLast()
    {
        var a = Window("A", "a1", closing: true, Tab("a1", "https://a1"));
        var b = Window("B", "b1", closing: true, Tab("b1", "https://b1"));
        var store = NewStore();

        store.Save(SessionSnapshot.Rows(SessionSnapshot.Cohort([a, b], "A")));
        Assert.Equal(["https://a1"], store.Load().Select(e => e.Url));

        store.Save(SessionSnapshot.Rows(SessionSnapshot.Cohort([a, b], "B")));
        Assert.Equal(["https://b1"], store.Load().Select(e => e.Url));
    }

    [Fact]
    public void ApplicationShutdownWithNoPeersSavesTheLastWindow()
    {
        var only = Window("only", "t2", closing: true, Tab("t1", "https://one"), Tab("t2", "https://two"));
        var rows = SessionSnapshot.Rows(SessionSnapshot.Cohort([only], "only"));

        Assert.Equal(["https://one", "https://two"], rows.Select(r => r.Url));
        Assert.Equal([false, true], rows.Select(r => r.IsActive));
    }

    [Fact]
    public void EmptyWindowAfterADockContributesNothing()
    {
        // A window left with no tabs by a dock must not add a blank row or become the
        // owner that carries the active flag.
        var survivor = Window("survivor", "s1", closing: true, Tab("s1", "https://s1"));
        var empty = Window("empty", null, closing: true);

        var rows = SessionSnapshot.Rows(SessionSnapshot.Cohort([survivor, empty], "survivor"));

        Assert.Single(rows);
        Assert.Equal("https://s1", rows[0].Url);
        Assert.True(rows[0].IsActive);
    }

    [Fact]
    public void InternalPagesAreDroppedButNothingElseIs()
    {
        var w = Window("w", "real", closing: true,
            Tab("i1", "internal://newtab"),
            Tab("r1", "https://one", "One", pin: true),
            Tab("d1", "https://one", "One", pin: true),
            Tab("r2", "https://two", "Two"));
        var store = NewStore();

        store.Save(SessionSnapshot.Rows(SessionSnapshot.Cohort([w], "w")));
        var loaded = store.Load();

        // Two rows with the same url and title both survive, and the pin travels with its row.
        // The active tab here is an internal page, so its row is dropped and no row carries
        // the flag. That is covered on its own below.
        Assert.Equal(["https://one", "https://one", "https://two"], loaded.Select(e => e.Url));
        Assert.Equal(["One", "One", "Two"], loaded.Select(e => e.Title));
        Assert.Equal([true, true, false], loaded.Select(e => e.IsPinned));
        Assert.All(loaded, e => Assert.False(e.IsActive));
    }

    [Fact]
    public void ActiveFlagFollowsTheLastWindowInTheCohortThatHasOne()
    {
        var first = Window("first", "f1", closing: false, Tab("f1", "https://f1"));
        var second = Window("second", "s2", closing: false, Tab("s1", "https://s1"), Tab("s2", "https://s2"));
        var closing = Window("closing", "c1", closing: true, Tab("c1", "https://c1"));
        var rows = SessionSnapshot.Rows(SessionSnapshot.Cohort([first, second, closing], "closing"));

        Assert.Equal(["first", "second"], SessionSnapshot.Cohort([first, second, closing], "closing").Select(w => w.Name));
        Assert.Equal(["https://f1", "https://s1", "https://s2"], rows.Select(r => r.Url));
        Assert.Equal([false, false, true], rows.Select(r => r.IsActive));
    }

    [Fact]
    public void ActiveInternalTabLeavesNoRowMarkedActive()
    {
        var w = Window("w", "i1", closing: true, Tab("i1", "internal://settings"), Tab("r1", "https://one"));
        var rows = SessionSnapshot.Rows(SessionSnapshot.Cohort([w], "w"));

        Assert.Single(rows);
        Assert.False(rows[0].IsActive);
    }

    [Fact]
    public void AWindowWithNoActiveTabStillProducesAReadableSnapshot()
    {
        var w = Window("w", null, closing: true, Tab("r1", "https://one"), Tab("r2", "https://two"));
        var store = NewStore();

        store.Save(SessionSnapshot.Rows(SessionSnapshot.Cohort([w], "w")));
        var loaded = store.Load();

        // No owner meant no flag, and the restore path falls back to the first row.
        Assert.Equal(["https://one", "https://two"], loaded.Select(e => e.Url));
        Assert.All(loaded, e => Assert.False(e.IsActive));
        Assert.Equal(0, SessionStore.ResolveActiveIndex(loaded));
    }

    [Fact]
    public void EmptyRegistryWritesNothing()
    {
        Assert.Empty(SessionSnapshot.Cohort([], "gone"));
        Assert.Empty(SessionSnapshot.Rows([]));
    }
}