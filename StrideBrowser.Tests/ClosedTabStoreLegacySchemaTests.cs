using System;
using System.IO;
using System.Linq;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// The history file already on disk on real installations was written by commit
/// b4847b2, whose entry type was
/// <c>public record ClosedTabEntry(string Url, string Title)</c> serialised
/// camelCase, with no pin and no timestamp. Those files must keep working.
///
/// These tests are read and round trip only. Nothing here touches the real
/// profile; each case writes a scratch file in a temp directory.
/// </summary>
public sealed class ClosedTabStoreLegacySchemaTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("stride-closed-legacy");
    private readonly string _path;

    public ClosedTabStoreLegacySchemaTests() => _path = Path.Combine(_dir.FullName, "closed-tabs.json");

    public void Dispose() => _dir.Delete(true);

    private ClosedTabStore NewStore(int capacity = ClosedTabStore.DefaultCapacity) => new(_path, capacity);

    [Fact]
    public void TwoFieldHistoryIsReadWithOrderIntact()
    {
        File.WriteAllText(_path,
            """
            [{"url":"https://one.example/","title":"One"},
             {"url":"https://two.example/","title":"Two"},
             {"url":"https://three.example/","title":"Three"}]
            """);

        var store = NewStore();
        var loaded = store.Load();

        Assert.False(store.LastReadFailed);
        Assert.Equal(
            new[] { "https://one.example/", "https://two.example/", "https://three.example/" },
            loaded.Select(r => r.Url));
        Assert.Equal(new[] { "One", "Two", "Three" }, loaded.Select(r => r.Title));
    }

    [Fact]
    public void TwoFieldHistoryDefaultsPinAndTimestampRatherThanFailing()
    {
        File.WriteAllText(_path, """[{"url":"https://legacy.example/","title":"Legacy"}]""");

        var loaded = NewStore().Load();

        var only = Assert.Single(loaded);
        Assert.False(only.WasPinned);
        Assert.Equal(default, only.ClosedAtUtc);
    }

    [Fact]
    public void RepeatedUrlsAreAllKept()
    {
        // The legacy writer stored the raw close order and never collapsed
        // repeats. Dropping repeats here would silently lose reopen steps.
        File.WriteAllText(_path,
            """[{"url":"https://same.example/","title":"A"},{"url":"https://same.example/","title":"B"},{"url":"https://same.example/","title":"C"}]""");

        var loaded = NewStore().Load();

        Assert.Equal(3, loaded.Count);
        Assert.Equal(new[] { "A", "B", "C" }, loaded.Select(r => r.Title));
    }

    [Fact]
    public void TwoFieldHistoryIsRewrittenInTheCurrentSchemaWithoutLosingEntries()
    {
        File.WriteAllText(_path,
            """[{"url":"https://one.example/","title":"One"},{"url":"https://two.example/","title":"Two"}]""");

        var store = NewStore();
        store.Load();
        Assert.True(store.Record("https://three.example/", "Three", wasPinned: true));

        var reread = NewStore().Load();
        Assert.Equal(3, reread.Count);
        Assert.Equal("https://one.example/", reread[0].Url);
        Assert.Equal("https://three.example/", reread[^1].Url);
        Assert.True(reread[^1].WasPinned);
    }

    [Fact]
    public void EmptyArrayIsNotAnError()
    {
        File.WriteAllText(_path, "[]");

        var store = NewStore();

        Assert.Empty(store.Load());
        Assert.False(store.LastReadFailed);
    }

    [Fact]
    public void MissingFileIsNotAnError()
    {
        var store = NewStore();

        Assert.Empty(store.Load());
        Assert.False(store.LastReadFailed);
    }

    [Fact]
    public void HistoryLongerThanTheCapIsReadWholeAndTrimmedOnTheNextWrite()
    {
        // b4847b2 already capped saves at 50, but a hand edited or older file can
        // be longer. Reading must not silently drop entries. The cap applies when
        // the next write happens.
        var many = string.Join(",", Enumerable.Range(0, 60)
            .Select(i => $$"""{"url":"https://n{{i}}.example/","title":"T{{i}}"}"""));
        File.WriteAllText(_path, "[" + many + "]");

        var store = NewStore(50);
        Assert.Equal(60, store.Load().Count);

        Assert.True(store.Record("https://new.example/", "New", wasPinned: false));

        var after = NewStore(50).Load();
        Assert.Equal(50, after.Count);
        // The newest closure survives the trim. The oldest is what falls off.
        Assert.Equal("https://new.example/", after[^1].Url);
    }

    [Fact]
    public void WholeUrlsAreStoredAndReadBackWithoutClipping()
    {
        var longUrl = "https://long.example/" + new string('p', 5000) + "?q=" + new string('r', 5000);

        var store = NewStore();
        Assert.True(store.Record(longUrl, "Long", wasPinned: false));

        Assert.Equal(longUrl, NewStore().Load()[0].Url);
    }

    [Fact]
    public void TitlesAreClippedButUrlsAreNot()
    {
        var longTitle = new string('t', 5000);

        var store = NewStore();
        Assert.True(store.Record("https://clip.example/", longTitle, wasPinned: false));

        var entry = NewStore().Load()[0];
        Assert.True(entry.Title!.Length < longTitle.Length);
        Assert.Equal("https://clip.example/", entry.Url);
    }
}
