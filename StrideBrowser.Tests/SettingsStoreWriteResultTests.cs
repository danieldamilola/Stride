using System;
using System.IO;
using System.Linq;
using StrideBrowser.Helpers;
using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// What SettingsStore reports when a write does or does not reach the disk, and the
/// shape of the file it writes. The store catches write faults and returns, so a caller
/// that watched for exceptions could not tell a saved profile from one that never landed.
///
/// Every path here is inside a fresh temp directory that the constructor proves is
/// outside the installed browser's profile.
/// </summary>
public sealed class SettingsStoreWriteResultTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SettingsStoreWriteResultTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "StrideStoreFaults", Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_dir, "settings.json");

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

    private SettingsStore Store(Action<string, string>? write = null) => new(_path, write);

    private void SeedPreviousFile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, """{"searchEngine":"Startpage","restoreSessionOnStartup":false}""");
    }

    [Fact]
    public void AWriteThatLandsReportsTrueAndRoundTrips()
    {
        var store = Store();
        var settings = new BrowserSettings
        {
            SearchEngine = "Brave",
            RestoreSessionOnStartup = false,
            BlockDuplicateTabs = false,
            DefaultZoom = 120
        };

        Assert.True(store.TrySave(settings));

        var loaded = store.Load();
        Assert.Equal("Brave", loaded.SearchEngine);
        Assert.False(loaded.RestoreSessionOnStartup);
        Assert.False(loaded.BlockDuplicateTabs);
        Assert.Equal(120, loaded.DefaultZoom);
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void TheKeysKeepTheirCamelCaseNames()
    {
        // Settings files written by earlier versions use these names. Renaming a key
        // would silently reset that setting on the next launch.
        var store = Store();
        Assert.True(store.TrySave(new BrowserSettings { RestoreSessionOnStartup = false }));

        var json = File.ReadAllText(_path);

        Assert.Contains("\"restoreSessionOnStartup\"", json);
        Assert.Contains("\"blockDuplicateTabs\"", json);
        Assert.Contains("\"newTabShortcuts\"", json);
        Assert.Contains("\"customShortcuts\"", json);
    }

    [Fact]
    public void AWriterFaultReportsFalseAndLeavesThePreviousFileOnDisk()
    {
        SeedPreviousFile();
        var before = File.ReadAllBytes(_path);
        var store = Store((_, _) => throw new IOException("injected writer fault"));

        Assert.False(store.TrySave(new BrowserSettings { SearchEngine = "Brave" }));

        Assert.Equal(before, File.ReadAllBytes(_path));
        Assert.Equal("Startpage", store.Load().SearchEngine);
        Assert.False(store.Load().RestoreSessionOnStartup);
    }

    [Fact]
    public void ALockedFileIsARealFaultAndReportsFalse()
    {
        SeedPreviousFile();
        var store = Store();
        using var held = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.False(store.TrySave(new BrowserSettings { SearchEngine = "Brave" }));

        held.Dispose();
        Assert.Equal("Startpage", store.Load().SearchEngine);
    }

    [Fact]
    public void APathThatCannotBeCreatedReportsFalse()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "not a directory");
        var store = new SettingsStore(Path.Combine(blocker, "settings.json"));

        Assert.False(store.TrySave(new BrowserSettings { SearchEngine = "Brave" }));
    }

    [Fact]
    public void SaveStillNeverThrows()
    {
        // The void call the settings page, the reader and the message handlers use keeps
        // its behaviour: a fault is logged and swallowed.
        var store = Store((_, _) => throw new IOException("injected writer fault"));

        store.Save(new BrowserSettings { SearchEngine = "Brave" });

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void AMissingFileLoadsDefaultsWithNoImportedShortcuts()
    {
        var loaded = Store().Load();

        Assert.Equal("DuckDuckGo", loaded.SearchEngine);
        Assert.True(loaded.RestoreSessionOnStartup);
        // Edge bookmarks are not imported without consent.
        Assert.Empty(loaded.NewTabShortcuts);
        Assert.Empty(loaded.CustomShortcuts);
    }

    [Fact]
    public void AnOlderFileWithNullCollectionsLoadsWithoutThrowing()
    {
        // A hand edited or earlier format file can hold null collections, which would NRE
        // in the settings page, the new tab page and the shortcut handlers.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, """{"searchEngine":"Ecosia","newTabShortcuts":null,"customShortcuts":null}""");

        var loaded = Store().Load();

        Assert.Equal("Ecosia", loaded.SearchEngine);
        Assert.NotNull(loaded.NewTabShortcuts);
        Assert.NotNull(loaded.CustomShortcuts);
        Assert.Empty(loaded.NewTabShortcuts);
    }

    [Fact]
    public void AFileThatCannotBeParsedFallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "not json at all");

        Assert.Equal("DuckDuckGo", Store().Load().SearchEngine);
    }
}
