using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UmberBrowser.Helpers;
using UmberBrowser.Models;
using UmberBrowser.Services;
using UmberBrowser.Services.UI;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// The shutdown write sequence against the real stores and real files, wired the way the
/// window manager wires them.
///
/// A close used to hand the stores void callbacks and read the result off "nothing
/// threw". The stores swallow write faults, so a snapshot or a settings file that never
/// reached the disk was reported as written. These cases wire the real stores, fail them
/// at the write, and read what each one answers.
///
/// Every path here is inside a fresh temp directory that the constructor proves is
/// outside the installed browser's profile.
/// </summary>
public sealed class WindowShutdownRealStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _sessionPath;
    private readonly string _settingsPath;
    private readonly string _closedTabsPath;
    private readonly List<string> _writes = [];

    public WindowShutdownRealStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "UmberShutdownStores", Guid.NewGuid().ToString("N"));
        _sessionPath = Path.Combine(_dir, "session.json");
        _settingsPath = Path.Combine(_dir, "settings.json");
        _closedTabsPath = Path.Combine(_dir, "closed-tabs.json");

        // Containment: no case in this class may read or write the real profile.
        Assert.False(AppPaths.IsSameOrAncestor(AppPaths.ProductionDataDirectory, _dir),
            "the temp profile must not sit inside the installed browser's data");
        Assert.False(AppPaths.IsSameOrAncestor(_dir, AppPaths.ProductionDataDirectory),
            "the temp profile must not contain the installed browser's data");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { /* a handle a failed case left open must not fail the run */ }
    }

    private static readonly SnapshotSource[] Cohort =
    [
        new("only", [("t1", "https://one.example/", "One", false), ("t2", "https://two.example/", "Two", true)],
            "t2", Closing: true)
    ];

    /// <summary>A writer that records the write, then performs the real atomic write.</summary>
    private Action<string, string> Recorder(string label) => (path, json) =>
    {
        _writes.Add(label);
        AtomicFileWriter.WriteAllText(path, json);
    };

    private static readonly Action<string, string> ThrowingWriter =
        (_, _) => throw new IOException("injected writer fault");

    /// <summary>The wiring BrowserWindowManager.Close uses, with the stores named.</summary>
    private WindowShutdownWrites Build(
        ISessionStore sessions,
        ISettingsStore settingsStore,
        BrowserSettings settings,
        IClosedTabStore closedTabs,
        bool writeSnapshot = true,
        bool clearBrowsingData = false)
        => new()
        {
            WriteSnapshot = writeSnapshot,
            ClearBrowsingData = clearBrowsingData,
            WriteSnapshotRows = () => sessions.TrySave(SessionSnapshot.Rows(Cohort)),
            ClearSnapshot = sessions.TryClear,
            ClearClosedTabHistory = closedTabs.Clear,
            SaveSettings = () => settingsStore.TrySave(settings),
            ClearBrowsingDataAsync = () => { _writes.Add("browse"); return Task.CompletedTask; }
        };

    private BrowserSettings Settings() => new() { SearchEngine = "Brave" };

    [Fact]
    public async Task ACloseThatLandsEveryWriteReportsComplete()
    {
        var sessions = new SessionStore(_sessionPath, Recorder("snapshot"));
        var settingsStore = new SettingsStore(_settingsPath, Recorder("settings"));
        var closedTabs = new ClosedTabStore(_closedTabsPath, ClosedTabStore.DefaultCapacity, Recorder("closed-history"));
        var result = await Build(sessions, settingsStore, Settings(), closedTabs).RunAsync();

        Assert.True(result.SessionSnapshotWritten);
        Assert.True(result.SettingsSaved);
        Assert.True(result.ClosedTabHistoryCleared);
        Assert.True(result.RecoveryStateIntact);
        Assert.True(result.Complete);
        Assert.Equal(["snapshot", "settings"], _writes);

        // The rows landed with their titles, pins and the one active row.
        var loaded = sessions.Load();
        Assert.Equal(["https://one.example/", "https://two.example/"], loaded.Select(e => e.Url));
        Assert.Equal(["One", "Two"], loaded.Select(e => e.Title));
        Assert.Equal([false, true], loaded.Select(e => e.IsPinned));
        Assert.Equal(1, SessionStore.ResolveActiveIndex(loaded));
        Assert.Equal("Brave", settingsStore.Load().SearchEngine);
    }

    [Fact]
    public async Task ASnapshotThatCannotReachTheDiskTurnsTheResultRed()
    {
        var sessions = new SessionStore(_sessionPath, ThrowingWriter);
        var settingsStore = new SettingsStore(_settingsPath, Recorder("settings"));
        var closedTabs = new ClosedTabStore(_closedTabsPath, ClosedTabStore.DefaultCapacity, Recorder("closed-history"));

        var result = await Build(sessions, settingsStore, Settings(), closedTabs).RunAsync();

        Assert.False(result.SessionSnapshotWritten);
        Assert.False(result.RecoveryStateIntact);
        Assert.False(result.Complete);

        // The write after the failure still ran and still landed.
        Assert.True(result.SettingsSaved);
        Assert.Equal(["settings"], _writes);
        Assert.Equal("Brave", settingsStore.Load().SearchEngine);
        Assert.Empty(sessions.Load());
    }

    [Fact]
    public async Task SettingsThatCannotReachTheDiskTurnsTheResultRedAndKeepsTheSnapshot()
    {
        var sessions = new SessionStore(_sessionPath, Recorder("snapshot"));
        var settingsStore = new SettingsStore(_settingsPath, ThrowingWriter);
        var closedTabs = new ClosedTabStore(_closedTabsPath, ClosedTabStore.DefaultCapacity, Recorder("closed-history"));

        var result = await Build(sessions, settingsStore, Settings(), closedTabs).RunAsync();

        Assert.False(result.SettingsSaved);
        Assert.False(result.RecoveryStateIntact);
        Assert.False(result.Complete);
        Assert.True(result.SessionSnapshotWritten);
        Assert.Equal(["snapshot"], _writes);

        var loaded = sessions.Load();
        Assert.Equal(["https://one.example/", "https://two.example/"], loaded.Select(e => e.Url));
        Assert.True(loaded[1].IsActive);
    }

    [Fact]
    public async Task RestoreOffEmptiesTheSnapshotOnDiskBeforeTheSettings()
    {
        var sessions = new SessionStore(_sessionPath, Recorder("snapshot-clear"));
        var settingsStore = new SettingsStore(_settingsPath, Recorder("settings"));
        var closedTabs = new ClosedTabStore(_closedTabsPath, ClosedTabStore.DefaultCapacity, Recorder("closed-history"));
        Assert.True(new SessionStore(_sessionPath).TrySave([("https://stale.example/", "Stale", false, true)]));

        var result = await Build(sessions, settingsStore, Settings(), closedTabs, writeSnapshot: false).RunAsync();

        Assert.True(result.Complete);
        Assert.Equal(["snapshot-clear", "settings"], _writes);
        Assert.Empty(sessions.Load());
        Assert.True(File.Exists(_sessionPath));
        Assert.Equal("Brave", settingsStore.Load().SearchEngine);
    }

    [Fact]
    public async Task ADataClearingExitWritesInOrderAndReportsComplete()
    {
        var sessions = new SessionStore(_sessionPath, Recorder("snapshot-clear"));
        var settingsStore = new SettingsStore(_settingsPath, Recorder("settings"));
        var closedTabs = new ClosedTabStore(_closedTabsPath, ClosedTabStore.DefaultCapacity, Recorder("closed-history"));
        Assert.True(new ClosedTabStore(_closedTabsPath, ClosedTabStore.DefaultCapacity).Record("https://closed.example/", "Closed", false));
        Assert.True(new SessionStore(_sessionPath).TrySave([("https://stale.example/", "Stale", false, true)]));

        var result = await Build(sessions, settingsStore, Settings(), closedTabs,
            writeSnapshot: false, clearBrowsingData: true).RunAsync();

        Assert.True(result.Complete);
        // Snapshot, then the browser cleanup, then the closed history, then the settings.
        Assert.Equal(["snapshot-clear", "browse", "closed-history", "settings"], _writes);
        Assert.Empty(sessions.Load());
        Assert.Empty(closedTabs.Load());
        Assert.Equal("Brave", settingsStore.Load().SearchEngine);
    }

    [Fact]
    public async Task AHistoryThatCannotBeForgottenIsReportedAndTheRestStillWrites()
    {
        // A history that is already on disk, and a store whose write refuses, so the
        // closures survive a data clearing exit and the result says so.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_closedTabsPath,
            """[{"url":"https://closed.example/","title":"Closed","wasPinned":false,"closedAtUtc":"2026-10-14T10:00:00Z"}]""");
        var sessions = new SessionStore(_sessionPath, Recorder("snapshot-clear"));
        var settingsStore = new SettingsStore(_settingsPath, Recorder("settings"));
        var closedTabs = new ClosedTabStore(_closedTabsPath, ClosedTabStore.DefaultCapacity, ThrowingWriter);

        var result = await Build(sessions, settingsStore, Settings(), closedTabs,
            writeSnapshot: false, clearBrowsingData: true).RunAsync();

        Assert.False(result.ClosedTabHistoryCleared);
        Assert.False(result.Complete);
        Assert.True(result.BrowsingDataCleared);

        // The history write refused, so its bytes are still on disk, and the snapshot and
        // settings writes either side of it both landed.
        Assert.Equal("https://closed.example/", Assert.Single(closedTabs.Load()).Url);
        Assert.Empty(sessions.Load());
        Assert.Equal(["snapshot-clear", "browse", "settings"], _writes);
        Assert.Equal("Brave", settingsStore.Load().SearchEngine);
    }

    [Fact]
    public async Task ALockedSnapshotFileIsReportedAsNotWritten()
    {
        // A real file fault rather than an injected one: something else holds the file
        // while the window is closing.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_sessionPath, """[{"url":"https://held.example/","title":"Held","isPinned":false,"isActive":true}]""");
        var sessions = new SessionStore(_sessionPath);
        var settingsStore = new SettingsStore(_settingsPath, Recorder("settings"));
        var closedTabs = new ClosedTabStore(_closedTabsPath, ClosedTabStore.DefaultCapacity, Recorder("closed-history"));

        using var held = new FileStream(_sessionPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await Build(sessions, settingsStore, Settings(), closedTabs).RunAsync();

        Assert.False(result.SessionSnapshotWritten);
        Assert.False(result.RecoveryStateIntact);
        Assert.False(result.Complete);
        Assert.True(result.SettingsSaved);
        Assert.Equal(["settings"], _writes);

        held.Dispose();
        Assert.Equal("https://held.example/", Assert.Single(sessions.Load()).Url);
    }
}
