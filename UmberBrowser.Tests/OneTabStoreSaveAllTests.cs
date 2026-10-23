using UmberBrowser.Models;
using UmberBrowser.Services;
using Xunit;
using System.IO;

namespace UmberBrowser.Tests;

public class OneTabStoreSaveAllTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrideOneTabTests", Guid.NewGuid().ToString("N"));
    private OneTabStore CreateStore() => new(Path.Combine(_directory, "saved-tabs.json"));
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public async Task SaveAll_FiltersInternalUrls()
    {
        var store = CreateStore();
        var tabs = new List<BrowserTab>
        {
            new() { Url = "https://example.com", Title = "Example" },
            new() { Url = "internal://newtab", Title = "New Tab" },
            new() { Url = "about:blank", Title = "Blank" },
            new() { Url = "data:text/html,hello", Title = "Data" },
            new() { Url = "https://test.org", Title = "Test" }
        };

        var group = store.SaveAll(tabs);

        Assert.NotNull(group);
        Assert.Equal(2, group.Tabs.Count);
        Assert.Equal("https://example.com", group.Tabs[0].Url);
        Assert.Equal("https://test.org", group.Tabs[1].Url);

        store.RemoveGroup(group.Id);
        Assert.True(await store.FlushAsync());
    }

    [Fact]
    public void SaveAll_ReturnsNullWhenNoSaveableTabs()
    {
        var store = CreateStore();
        var tabs = new List<BrowserTab>
        {
            new() { Url = "internal://settings", Title = "Settings" }
        };

        var group = store.SaveAll(tabs);

        Assert.Null(group);
    }

    [Fact]
    public void SaveAll_ReturnsNullWhenEmpty()
    {
        var store = CreateStore();
        var group = store.SaveAll(new List<BrowserTab>());
        Assert.Null(group);
    }
}
