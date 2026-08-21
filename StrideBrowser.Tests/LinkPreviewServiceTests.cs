using System.Windows;
using StrideBrowser.Models;
using StrideBrowser.Models.LinkPreview;
using StrideBrowser.Services;
using StrideBrowser.Services.LinkPreview;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class LinkPreviewServiceTests
{
    private sealed class FakeStore : ISettingsStore
    {
        private BrowserSettings _s = new();
        public BrowserSettings Load() => _s;
        public void Save(BrowserSettings settings) => _s = settings;
    }

    private LinkPreviewService CreateService(BrowserSettings? settings = null)
    {
        settings ??= new BrowserSettings();
        var store = new FakeStore();
        store.Save(settings);
        var policy = new LinkPreviewPolicy();
        var suppressor = new LinkPreviewDownloadSuppressor();
        return new LinkPreviewService(settings, store, policy, suppressor);
    }

    [Fact]
    public void RequestPeek_ShowsPreviewForValidRequest()
    {
        var svc = CreateService();
        var tabId = Guid.NewGuid();
        var ok = svc.RequestPeek(tabId, "https://example.com/article", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");
        Assert.True(ok);
        Assert.True(svc.IsPreviewVisible);
        Assert.Equal("https://example.com/article", svc.Current.Url);
        Assert.Equal(tabId, svc.Current.TabId);
        Assert.True(svc.Current.IsLoading);
    }

    [Fact]
    public void RequestPeek_RejectsInternalUrl()
    {
        var svc = CreateService();
        var ok = svc.RequestPeek(Guid.NewGuid(), "internal://settings", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");
        Assert.False(ok);
        Assert.False(svc.IsPreviewVisible);
    }

    [Fact]
    public void RequestPeek_RespectsDisabled()
    {
        var settings = new BrowserSettings { LinkPreviewEnabled = false };
        var svc = CreateService(settings);
        var ok = svc.RequestPeek(Guid.NewGuid(), "https://example.com", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");
        Assert.False(ok);
    }

    [Fact]
    public void RequestPeek_DeduplicatesSameUrlQuickly()
    {
        var svc = CreateService();
        var tabId = Guid.NewGuid();
        var rect = new Rect(10, 10, 100, 20);
        svc.RequestPeek(tabId, "https://example.com/a", rect, LinkPreviewTrigger.AltPress, "https://current.com");
        var second = svc.RequestPeek(tabId, "https://example.com/a", rect, LinkPreviewTrigger.AltPress, "https://current.com");
        Assert.True(second);
    }

    [Fact]
    public void RequestPeek_ThrottlesDifferentUrlWithin100ms()
    {
        var svc = CreateService();
        var tabId = Guid.NewGuid();
        var rect = new Rect(10, 10, 100, 20);
        svc.RequestPeek(tabId, "https://example.com/a", rect, LinkPreviewTrigger.AltPress, "https://current.com");
        // Immediately request a different URL - should be throttled since within 100ms
        var second = svc.RequestPeek(tabId, "https://example.com/b", rect, LinkPreviewTrigger.AltPress, "https://current.com");
        Assert.False(second);
    }

    [Fact]
    public void Dismiss_HidesAndClears()
    {
        var svc = CreateService();
        var tabId = Guid.NewGuid();
        svc.RequestPeek(tabId, "https://example.com", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");
        Assert.True(svc.IsPreviewVisible);
        svc.Dismiss();
        Assert.False(svc.IsPreviewVisible);
        Assert.Equal(Guid.Empty, svc.Current.TabId);
    }

    [Fact]
    public void Dismiss_IsNoopWhenHidden()
    {
        var svc = CreateService();
        svc.Dismiss();
        Assert.False(svc.IsPreviewVisible);
    }

    [Fact]
    public void NotifyPreviewLoaded_ClearsIsLoading()
    {
        var svc = CreateService();
        var tabId = Guid.NewGuid();
        svc.RequestPeek(tabId, "https://example.com", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");
        Assert.True(svc.Current.IsLoading);
        svc.NotifyPreviewLoaded("https://example.com");
        Assert.False(svc.Current.IsLoading);
    }

    [Fact]
    public void NotifyPreviewLoaded_IgnoresMismatchedUrl()
    {
        var svc = CreateService();
        svc.RequestPeek(Guid.NewGuid(), "https://example.com/a", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");
        svc.NotifyPreviewLoaded("https://other.com");
        Assert.True(svc.Current.IsLoading);
    }

    [Fact]
    public void RequestPeek_SwitchesOriginAndEmitsResume()
    {
        var svc = CreateService();
        var firstTab = Guid.NewGuid();
        var secondTab = Guid.NewGuid();
        var resumed = new List<Guid>();
        svc.OriginShouldResume += id => resumed.Add(id);

        svc.RequestPeek(firstTab, "https://example.com/a", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");
        // Wait briefly so the second request is not throttled by the 100ms guard
        Thread.Sleep(150);
        svc.RequestPeek(secondTab, "https://example.com/b", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");

        Assert.Equal(secondTab, svc.Current.TabId);
        Assert.Contains(firstTab, resumed);
    }

    [Fact]
    public void RequestPeek_FiresStateChanged()
    {
        var svc = CreateService();
        var fired = 0;
        svc.StateChanged += _ => fired++;
        svc.RequestPeek(Guid.NewGuid(), "https://example.com", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");
        Assert.Equal(1, fired);
        svc.Dismiss();
        Assert.Equal(2, fired);
    }

    [Fact]
    public void UpdateOptions_Persists()
    {
        var svc = CreateService();
        var opts = new LinkPreviewOptions(false, "Alt", true);
        svc.UpdateOptions(opts);
        Assert.False(svc.Options.Enabled);
        Assert.True(svc.Options.AllowPress);
    }

    [Fact]
    public void Dismiss_FiresResumeForCorrectTab()
    {
        var svc = CreateService();
        var tabId = Guid.NewGuid();
        var resumed = new List<Guid>();
        svc.OriginShouldResume += id => resumed.Add(id);
        svc.RequestPeek(tabId, "https://example.com", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");
        svc.Dismiss();
        Assert.Single(resumed);
        Assert.Equal(tabId, resumed[0]);
    }

    [Fact]
    public void UpdatePreviewSize_RejectsSmall()
    {
        var svc = CreateService();
        svc.RequestPeek(Guid.NewGuid(), "https://example.com", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");
        var before = svc.Current.Size;
        svc.UpdatePreviewSize(new Size(100, 100));
        Assert.Equal(before, svc.Current.Size);
    }

    [Fact]
    public void UpdatePreviewSize_RejectsNaN()
    {
        var svc = CreateService();
        svc.RequestPeek(Guid.NewGuid(), "https://example.com", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");
        var before = svc.Current.Size;
        var fired = 0;
        svc.StateChanged += _ => fired++;
        svc.UpdatePreviewSize(new Size(double.NaN, 400));
        svc.UpdatePreviewSize(new Size(500, double.NaN));
        Assert.Equal(before, svc.Current.Size);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void UpdatePreviewSize_AcceptsValidSizeAndFiresStateChanged()
    {
        var svc = CreateService();
        svc.RequestPeek(Guid.NewGuid(), "https://example.com", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, "https://current.com");
        var fired = 0;
        LinkPreviewState? lastState = null;
        svc.StateChanged += state =>
        {
            fired++;
            lastState = state;
        };
        var validSize = new Size(500, 400);
        svc.UpdatePreviewSize(validSize);
        Assert.Equal(1, fired);
        Assert.NotNull(lastState);
        Assert.Equal(validSize, lastState!.Size);
        Assert.Equal(validSize, svc.Current.Size);
    }
}
