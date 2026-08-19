using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

public class TabHibernationManagerTests
{
    [Fact]
    public void SuspendBackgroundTabs_WhenSleepDisabled_DoesNotTouchWebViews()
    {
        var settings = new BrowserSettings { TabSleepEnabled = false };
        var manager = new TabHibernationManager(new FakeDownloadStore(), settings);
        var activeTab = new BrowserTab();
        manager.Attach(
            () => new List<BrowserTab> { activeTab },
            () => new Dictionary<System.Guid, dynamic> { [activeTab.Id] = new BrokenWebView() },
            _ => { },
            10);

        manager.SuspendBackgroundTabs(activeTab);
    }

    [Fact]
    public void SuspendBackgroundTabs_WhenSleepEnabled_SuspendsBackgroundTabs()
    {
        var settings = new BrowserSettings { TabSleepEnabled = true };
        var manager = new TabHibernationManager(new FakeDownloadStore(), settings);
        var activeTab = new BrowserTab();
        var backgroundTab = new BrowserTab();
        var backgroundWv = new FakeWebView();
        manager.Attach(
            () => new List<BrowserTab> { activeTab, backgroundTab },
            () => new Dictionary<System.Guid, dynamic> { [activeTab.Id] = new FakeWebView(), [backgroundTab.Id] = backgroundWv },
            _ => { },
            10);

        manager.SuspendBackgroundTabs(activeTab);

        Assert.Equal(1, backgroundWv.CoreWebView2.SuspendCount);
    }

    [Fact]
    public void EvictExcessWebViews_WhenHibernationDisabled_DoesNotTeardown()
    {
        var settings = new BrowserSettings { TabHibernationEnabled = false };
        var manager = new TabHibernationManager(new FakeDownloadStore(), settings);
        var activeTab = new BrowserTab { IsActive = true };
        var tabs = new List<BrowserTab> { activeTab };
        var webViews = new Dictionary<System.Guid, dynamic> { [activeTab.Id] = new BrokenWebView() };
        for (var i = 0; i < 11; i++)
        {
            var tab = new BrowserTab();
            tabs.Add(tab);
            webViews[tab.Id] = new BrokenWebView();
        }

        var teardownCalls = 0;
        manager.Attach(() => tabs, () => webViews, _ => teardownCalls++, 10);

        manager.EvictExcessWebViews(activeTab);

        Assert.Equal(0, teardownCalls);
        Assert.All(tabs, t => Assert.False(t.IsHibernated));
    }

    [Fact]
    public void EvictExcessWebViews_WhenHibernationEnabled_EvictsDownToLimit()
    {
        var settings = new BrowserSettings { TabHibernationEnabled = true };
        var manager = new TabHibernationManager(new FakeDownloadStore(), settings);
        var activeTab = new BrowserTab { IsActive = true };
        var tabs = new List<BrowserTab> { activeTab };
        var webViews = new Dictionary<System.Guid, dynamic> { [activeTab.Id] = new FakeWebView() };
        for (var i = 0; i < 11; i++)
        {
            var tab = new BrowserTab();
            tabs.Add(tab);
            webViews[tab.Id] = new FakeWebView();
        }

        var teardownCalls = 0;
        manager.Attach(() => tabs, () => webViews, _ => teardownCalls++, 10);

        manager.EvictExcessWebViews(activeTab);

        Assert.Equal(2, teardownCalls);
    }
}

public sealed class FakeDownloadStore : IDownloadStore
{
    public ObservableCollection<DownloadItem> Items { get; } = new();
    public void Add(DownloadItem item) => Items.Add(item);
    public void Remove(string id) { }
    public void ClearCompleted() { }
    public DownloadItem? Get(string id) => null;
}

public sealed class FakeCoreWebView
{
    public bool IsDocumentPlayingAudio { get; set; }
    public CoreWebView2MemoryUsageTargetLevel MemoryUsageTargetLevel { get; set; }
    public int SuspendCount { get; private set; }
    public Task TrySuspendAsync() { SuspendCount++; return Task.CompletedTask; }
}

public sealed class FakeWebView
{
    public FakeCoreWebView CoreWebView2 { get; } = new();
    public Uri? Source { get; set; }
}

public sealed class BrokenWebView
{
    public object CoreWebView2 => throw new System.InvalidOperationException("CoreWebView2 should not be accessed");
    public Uri? Source => throw new System.InvalidOperationException("Source should not be accessed");
}