using System.IO;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// The recently-closed stack is capped so a long session cannot grow it without bound,
/// and it is persisted because Ctrl+Shift+T is the only way back to a closed tab when
/// session restore is off.
/// </summary>
public sealed class ClosedTabStackTests
{
    [Fact]
    public void Cap_MatchesTheEngineLimit()
    {
        // TabEngine keeps MaxClosedTabs in memory; the store must not persist more than
        // that or a reload would resurrect entries the engine would immediately trim.
        Assert.Equal(50, ClosedTabStore.MaxEntries);
    }

    [Fact]
    public void TrimmedStack_KeepsTheMostRecentlyClosed()
    {
        var entries = Enumerable.Range(0, 60)
            .Select(i => new ClosedTabEntry($"https://example.com/{i}", $"Tab {i}"))
            .ToList();

        var trimmed = entries.TakeLast(ClosedTabStore.MaxEntries).ToList();

        Assert.Equal(ClosedTabStore.MaxEntries, trimmed.Count);
        // Oldest entries fall off the front; the newest stays last so Ctrl+Shift+T
        // reopens the tab that was closed most recently.
        Assert.Equal("https://example.com/59", trimmed[^1].Url);
        Assert.Equal("https://example.com/10", trimmed[0].Url);
    }

    [Fact]
    public void EmptyStack_StaysEmpty()
    {
        var trimmed = Array.Empty<ClosedTabEntry>().TakeLast(ClosedTabStore.MaxEntries).ToList();
        Assert.Empty(trimmed);
    }

    [Fact]
    public void ClosedTab_RoundTripsThroughDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stride-closed-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ClosedTabStore(path);
            store.Save([
                new ClosedTabEntry("https://example.com/a", "A"),
                new ClosedTabEntry("https://example.com/b", "B"),
            ]);

            // A fresh store stands in for the next browser launch.
            var reloaded = new ClosedTabStore(path).Load();

            Assert.Equal(2, reloaded.Count);
            Assert.Equal("https://example.com/b", reloaded[^1].Url);
            Assert.Equal("A", reloaded[0].Title);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_TruncatesToTheNewestEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stride-closed-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ClosedTabStore(path);
            store.Save(Enumerable.Range(0, 70)
                .Select(i => new ClosedTabEntry($"https://example.com/{i}", $"Tab {i}")));

            var reloaded = store.Load();

            Assert.Equal(ClosedTabStore.MaxEntries, reloaded.Count);
            Assert.Equal("https://example.com/69", reloaded[^1].Url);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Clear_RemovesThePersistedStack()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stride-closed-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ClosedTabStore(path);
            store.Save([new ClosedTabEntry("https://example.com/a", "A")]);
            Assert.True(File.Exists(path));

            store.Clear();

            Assert.False(File.Exists(path));
            Assert.Empty(store.Load());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}