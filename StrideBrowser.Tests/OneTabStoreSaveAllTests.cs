using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

public class OneTabStoreSaveAllTests
{
    [Fact]
    public void SaveAll_FiltersInternalUrls()
    {
        var store = new OneTabStore();
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
    }

    [Fact]
    public void SaveAll_ReturnsNullWhenNoSaveableTabs()
    {
        var store = new OneTabStore();
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
        var store = new OneTabStore();
        var group = store.SaveAll(new List<BrowserTab>());
        Assert.Null(group);
    }
}
