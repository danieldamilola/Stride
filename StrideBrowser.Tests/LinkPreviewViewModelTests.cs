using System.Windows;
using StrideBrowser.Models;
using StrideBrowser.Models.LinkPreview;
using StrideBrowser.Services;
using StrideBrowser.Services.LinkPreview;
using StrideBrowser.ViewModels.LinkPreview;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class LinkPreviewViewModelTests
{
    private sealed class FakeStore : ISettingsStore
    {
        private BrowserSettings _s = new();
        public BrowserSettings Load() => _s;
        public void Save(BrowserSettings settings) => _s = settings;
    }

    private (LinkPreviewService svc, LinkPreviewViewModel vm) Create()
    {
        var settings = new BrowserSettings();
        var store = new FakeStore();
        var suppressor = new LinkPreviewDownloadSuppressor();
        var svc = new LinkPreviewService(settings, store, new LinkPreviewPolicy(), suppressor);
        var vm = new LinkPreviewViewModel(svc);
        return (svc, vm);
    }

    [Fact]
    public void ViewModel_MirrorsServiceState()
    {
        var (svc, vm) = Create();
        var tabId = Guid.NewGuid();
        svc.RequestPeek(tabId, "https://example.com", new Rect(5, 5, 50, 15), LinkPreviewTrigger.AltPress, "https://cur.com");
        Assert.True(vm.IsVisible);
        Assert.Equal("https://example.com", vm.Url);
        Assert.Equal(tabId, vm.OriginTabId);
        Assert.Equal(LinkPreviewTrigger.AltPress, vm.Trigger);
    }

    [Fact]
    public void ViewModel_DismissClears()
    {
        var (svc, vm) = Create();
        svc.RequestPeek(Guid.NewGuid(), "https://example.com", new Rect(5, 5, 50, 15), LinkPreviewTrigger.AltPress, "https://cur.com");
        Assert.True(vm.IsVisible);
        vm.Dismiss();
        Assert.False(vm.IsVisible);
        Assert.Equal(string.Empty, vm.Url);
    }

    [Fact]
    public void ViewModel_NotifyLoadedClearsIsLoading()
    {
        var (svc, vm) = Create();
        svc.RequestPeek(Guid.NewGuid(), "https://example.com", new Rect(5, 5, 50, 15), LinkPreviewTrigger.AltPress, "https://cur.com");
        Assert.True(vm.IsLoading);
        vm.NotifyLoaded();
        Assert.False(vm.IsLoading);
    }
}
