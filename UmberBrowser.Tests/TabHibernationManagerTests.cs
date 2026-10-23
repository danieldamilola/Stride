using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
using UmberBrowser.Engine;
using UmberBrowser.Models;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

public class TabHibernationManagerTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 1)]
    public void PinnedSleep_RequiresBothPreferences(bool sleep, bool allowPinned, int expected)
    {
        var settings = new BrowserSettings { TabSleepEnabled = sleep, AllowPinnedTabsToSleep = allowPinned };
        var active = new BrowserTab { IsActive = true };
        var pinned = new BrowserTab { IsPinned = true };
        var webView = new FakeWebView();
        var manager = new TabHibernationManager(new FakeDownloadStore(), settings);
        manager.Attach(() => new[] { active, pinned },
            () => new Dictionary<Guid, dynamic> { [pinned.Id] = webView }, _ => { }, 10);
        try
        {
            manager.SuspendBackgroundTabs(active);
            Assert.Equal(expected, webView.CoreWebView2.SuspendCount);
            Assert.Equal(expected == 1, pinned.IsSleeping);
        }
        finally { manager.Detach(); }
    }

    [Fact]
    public void DisablingSleep_ResumesAllLiveTabsAndRestoresMemoryTarget()
    {
        var settings = new BrowserSettings { AllowPinnedTabsToSleep = true };
        var active = new BrowserTab { IsActive = true };
        var pinned = new BrowserTab { IsPinned = true };
        var regular = new BrowserTab();
        var views = new Dictionary<Guid, dynamic>
        {
            [pinned.Id] = new FakeWebView(), [regular.Id] = new FakeWebView()
        };
        var manager = new TabHibernationManager(new FakeDownloadStore(), settings);
        manager.Attach(() => new[] { active, pinned, regular }, () => views, _ => { }, 10);
        try
        {
            manager.SuspendBackgroundTabs(active);
            Assert.True(pinned.IsSleeping);
            Assert.True(regular.IsSleeping);
            settings.TabSleepEnabled = false;
            manager.ApplySleepSettings();
            foreach (var tab in new[] { pinned, regular })
            {
                FakeCoreWebView core = views[tab.Id].CoreWebView2;
                Assert.Equal(1, core.ResumeCount);
                Assert.False(core.IsSuspended);
                Assert.Equal(CoreWebView2MemoryUsageTargetLevel.Normal, core.MemoryUsageTargetLevel);
                Assert.False(tab.IsSleeping);
            }
        }
        finally { manager.Detach(); }
    }

    [Fact]
    public void DisablingPinnedSleep_WakesPinnedTabsAndKeepsRegularTabsAsleep()
    {
        var settings = new BrowserSettings { AllowPinnedTabsToSleep = true };
        var active = new BrowserTab { IsActive = true };
        var pinned = new BrowserTab { IsPinned = true };
        var regular = new BrowserTab();
        var pinnedView = new FakeWebView();
        var regularView = new FakeWebView();
        var manager = new TabHibernationManager(new FakeDownloadStore(), settings);
        manager.Attach(() => new[] { active, pinned, regular },
            () => new Dictionary<Guid, dynamic> { [pinned.Id] = pinnedView, [regular.Id] = regularView }, _ => { }, 10);
        try
        {
            manager.SuspendBackgroundTabs(active);
            settings.AllowPinnedTabsToSleep = false;
            manager.ApplySleepSettings();
            Assert.False(pinned.IsSleeping);
            Assert.False(pinnedView.CoreWebView2.IsSuspended);
            Assert.Equal(1, pinnedView.CoreWebView2.ResumeCount);
            Assert.True(regular.IsSleeping);
            Assert.True(regularView.CoreWebView2.IsSuspended);
            Assert.Equal(0, regularView.CoreWebView2.ResumeCount);
        }
        finally { manager.Detach(); }
    }

    [Theory]
    [InlineData("sleepOff")]
    [InlineData("pinnedOff")]
    [InlineData("pinTab")]
    [InlineData("selected")]
    [InlineData("transferred")]
    [InlineData("detached")]
    public async Task PendingSuspension_ResumesWhenTheTabMustStayAwake(string change)
    {
        var settings = new BrowserSettings { AllowPinnedTabsToSleep = change != "pinTab" };
        var active = new BrowserTab { IsActive = true };
        var tab = new BrowserTab { IsPinned = change == "pinnedOff" };
        var tabs = new List<BrowserTab> { active, tab };
        var completion = new TaskCompletionSource<bool>();
        var webView = new FakeWebView();
        webView.CoreWebView2.SuspendResult = completion.Task;
        var manager = new TabHibernationManager(new FakeDownloadStore(), settings);
        manager.Attach(() => tabs, () => new Dictionary<Guid, dynamic> { [tab.Id] = webView }, _ => { }, 10);
        try
        {
            manager.SuspendBackgroundTabs(active);
            Assert.Equal(1, webView.CoreWebView2.SuspendCount);
            switch (change)
            {
                case "sleepOff": settings.TabSleepEnabled = false; manager.ApplySleepSettings(); break;
                case "pinnedOff": settings.AllowPinnedTabsToSleep = false; manager.ApplySleepSettings(); break;
                case "pinTab": tab.IsPinned = true; break;
                case "selected": tab.IsActive = true; break;
                case "transferred": tabs.Remove(tab); break;
                case "detached": manager.Detach(); break;
            }
            webView.CoreWebView2.Resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            completion.SetResult(true);
            await webView.CoreWebView2.Resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Yield();
            Assert.False(webView.CoreWebView2.IsSuspended);
            Assert.False(tab.IsSleeping);
        }
        finally { manager.Detach(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void PinnedHibernation_RequiresBothPreferencesForIdleAndEviction(bool hibernate, bool allowPinned, bool idle)
    {
        var settings = new BrowserSettings { TabHibernationEnabled = hibernate, AllowPinnedTabsToHibernate = allowPinned };
        var active = new BrowserTab { IsActive = true, LastActiveTime = DateTime.UtcNow.AddMinutes(-10) };
        var tab = new BrowserTab { IsPinned = true, Url = "https://example.org/", LastActiveTime = DateTime.UtcNow.AddMinutes(-10) };
        var views = new Dictionary<Guid, dynamic> { [active.Id] = new FakeWebView(), [tab.Id] = new FakeWebView() };
        var removed = new List<Guid>();
        var manager = new TabHibernationManager(new FakeDownloadStore(), settings);
        manager.Attach(() => new[] { active, tab }, () => views, id => { removed.Add(id); views.Remove(id); }, 1);
        try
        {
            if (idle) RunIdleCheck(manager);
            else manager.EvictExcessWebViews(active);
            Assert.Equal(hibernate && allowPinned, tab.IsHibernated);
            Assert.Equal(hibernate && allowPinned, removed.Contains(tab.Id));
            Assert.DoesNotContain(active.Id, removed);
        }
        finally { manager.Detach(); }
    }

    [Theory]
    [InlineData("audio")]
    [InlineData("download")]
    [InlineData("preview")]
    [InlineData("internal")]
    public void AllowingPinnedHibernation_PreservesAutomaticSafetyChecks(string protection)
    {
        var settings = new BrowserSettings { AllowPinnedTabsToHibernate = true };
        var tab = new BrowserTab { IsPinned = true, Url = "https://example.org/", LastActiveTime = DateTime.UtcNow.AddMinutes(-10) };
        var view = new FakeWebView();
        var downloads = new FakeDownloadStore();
        if (protection == "audio") view.CoreWebView2.IsDocumentPlayingAudio = true;
        if (protection == "download") downloads.Items.Add(new DownloadItem { State = DownloadState.InProgress });
        if (protection == "internal") tab.Url = InternalUrls.Settings;
        var manager = new TabHibernationManager(downloads, settings);
        var removed = new List<Guid>();
        manager.Attach(() => new[] { tab }, () => new Dictionary<Guid, dynamic> { [tab.Id] = view }, removed.Add, 0);
        manager.SetPreviewOriginCheck(id => protection == "preview" && id == tab.Id);
        try
        {
            RunIdleCheck(manager);
            manager.EvictExcessWebViews(new BrowserTab { IsActive = true });
            Assert.False(tab.IsHibernated);
            Assert.Empty(removed);
        }
        finally { manager.Detach(); }
    }

    private static void RunIdleCheck(TabHibernationManager manager) => typeof(TabHibernationManager)
        .GetMethod("HibernateInactiveTabs", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, null);

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

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void HibernateRestoredTabs_HibernatesEveryBackgroundTabRegardlessOfPreferencesOrPin(
        bool hibernationEnabled,
        bool allowPinnedHibernation)
    {
        var settings = new BrowserSettings
        {
            TabHibernationEnabled = hibernationEnabled,
            AllowPinnedTabsToHibernate = allowPinnedHibernation
        };
        var manager = new TabHibernationManager(new FakeDownloadStore(), settings);
        var activeTab = new BrowserTab { IsActive = true };
        var pinnedTab = new BrowserTab { IsPinned = true };
        var backgroundTab = new BrowserTab();
        var tornDown = new List<Guid>();

        manager.Attach(
            () => new List<BrowserTab> { activeTab, pinnedTab, backgroundTab },
            () => new Dictionary<Guid, dynamic>
            {
                [activeTab.Id] = new FakeWebView(),
                [pinnedTab.Id] = new FakeWebView(),
                [backgroundTab.Id] = new FakeWebView()
            },
            id => tornDown.Add(id),
            10);

        manager.HibernateRestoredTabs(activeTab);

        Assert.False(activeTab.IsHibernated);
        Assert.True(pinnedTab.IsHibernated);
        Assert.True(backgroundTab.IsHibernated);
        Assert.DoesNotContain(activeTab.Id, tornDown);
        Assert.Contains(pinnedTab.Id, tornDown);
        Assert.Contains(backgroundTab.Id, tornDown);
    }

    [Fact]
    public void HibernateRestoredTabs_MarksColdTabsWithoutTouchingTheActiveTab()
    {
        var settings = new BrowserSettings
        {
            TabHibernationEnabled = false,
            AllowPinnedTabsToHibernate = false
        };
        var manager = new TabHibernationManager(new FakeDownloadStore(), settings);
        var activeTab = new BrowserTab { IsActive = true };
        var pinnedTab = new BrowserTab { IsPinned = true };
        var webViews = new Dictionary<Guid, dynamic>();

        manager.Attach(
            () => new List<BrowserTab> { activeTab, pinnedTab },
            () => webViews,
            _ => throw new Xunit.Sdk.XunitException("A cold tab has no WebView to tear down."),
            10);

        manager.HibernateRestoredTabs(activeTab);

        Assert.False(activeTab.IsHibernated);
        Assert.True(pinnedTab.IsHibernated);
    }
}

public sealed class FakeDownloadStore : IDownloadStore
{
    public ObservableCollection<DownloadItem> Items { get; } = new();
    public void Add(DownloadItem item) => Items.Add(item);
    public void Remove(string id) { }
    public void ClearCompleted() { }
    public void ClearAll() { }
    public DownloadItem? Get(string id) => null;
}

public sealed class FakeCoreWebView
{
    public bool IsDocumentPlayingAudio { get; set; }
    public CoreWebView2MemoryUsageTargetLevel MemoryUsageTargetLevel { get; set; }
    public int SuspendCount { get; private set; }
    public int ResumeCount { get; private set; }
    public bool IsSuspended { get; private set; }
    public Task<bool> SuspendResult { get; set; } = Task.FromResult(true);
    public TaskCompletionSource Resumed { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<bool> TrySuspendAsync()
    {
        SuspendCount++;
        var result = await SuspendResult;
        IsSuspended = result;
        return result;
    }
    public void Resume() { ResumeCount++; IsSuspended = false; Resumed.TrySetResult(); }
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
