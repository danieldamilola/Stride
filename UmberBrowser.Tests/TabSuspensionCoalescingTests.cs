using UmberBrowser.Engine;
using UmberBrowser.Models;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class TabSuspensionCoalescingTests
{
    [Fact]
    public void RepeatedActivationsSkipConfirmedSleepingControls()
    {
        var active = new BrowserTab { IsActive = true };
        var background = new BrowserTab();
        var view = new FakeWebView();
        var manager = Create(active, background, view);
        try
        {
            for (var i = 0; i < 20; i++) manager.SuspendBackgroundTabs(active);
            Assert.True(background.IsSleeping);
            Assert.Equal(1, view.CoreWebView2.SuspendCount);
        }
        finally { manager.Detach(); }
    }

    [Fact]
    public async Task RepeatedActivationsJoinOnePendingNativeSuspension()
    {
        var active = new BrowserTab { IsActive = true };
        var background = new BrowserTab();
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new FakeWebView();
        view.CoreWebView2.SuspendResult = result.Task;
        var manager = Create(active, background, view);
        try
        {
            for (var i = 0; i < 20; i++) manager.SuspendBackgroundTabs(active);
            Assert.Equal(1, view.CoreWebView2.SuspendCount);
            Assert.False(background.IsSleeping);
            result.SetResult(true);
            await UntilAsync(() => background.IsSleeping);
            manager.SuspendBackgroundTabs(active);
            Assert.Equal(1, view.CoreWebView2.SuspendCount);
        }
        finally { manager.Detach(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledNativeWorkAllowsAFreshRequest(bool cancelled)
    {
        var active = new BrowserTab { IsActive = true };
        var background = new BrowserTab();
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new FakeWebView();
        view.CoreWebView2.SuspendResult = result.Task;
        var manager = Create(active, background, view);
        try
        {
            manager.SuspendBackgroundTabs(active);
            view.CoreWebView2.SuspendResult = Task.FromResult(true);
            if (cancelled) result.SetCanceled(); else result.SetResult(false);
            await UntilAsync(() => { manager.SuspendBackgroundTabs(active); return background.IsSleeping; });
            Assert.Equal(2, view.CoreWebView2.SuspendCount);
        }
        finally { manager.Detach(); }
    }

    [Fact]
    public async Task SettingsGenerationChangeResumesLateWorkAndAllowsAnotherRequest()
    {
        var active = new BrowserTab { IsActive = true };
        var background = new BrowserTab();
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new FakeWebView();
        view.CoreWebView2.SuspendResult = result.Task;
        var settings = new BrowserSettings();
        var manager = Create(active, background, view, settings);
        try
        {
            manager.SuspendBackgroundTabs(active);
            settings.TabSleepEnabled = false;
            manager.ApplySleepSettings();
            view.CoreWebView2.Resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            result.SetResult(true);
            await view.CoreWebView2.Resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(background.IsSleeping);
            settings.TabSleepEnabled = true;
            view.CoreWebView2.SuspendResult = Task.FromResult(true);
            await UntilAsync(() => { manager.SuspendBackgroundTabs(active); return background.IsSleeping; });
            Assert.Equal(2, view.CoreWebView2.SuspendCount);
        }
        finally { manager.Detach(); }
    }

    [Fact]
    public async Task ReplacingTheControlResumesOldCompletionAndDoesNotMarkReplacementSleeping()
    {
        var active = new BrowserTab { IsActive = true };
        var background = new BrowserTab();
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldView = new FakeWebView();
        oldView.CoreWebView2.SuspendResult = result.Task;
        var replacement = new FakeWebView();
        var views = new Dictionary<Guid, dynamic> { [background.Id] = oldView };
        var manager = new TabHibernationManager(new FakeDownloadStore(), new BrowserSettings());
        manager.Attach(() => new[] { active, background }, () => views, _ => { }, 10);
        try
        {
            manager.SuspendBackgroundTabs(active);
            views[background.Id] = replacement;
            result.SetResult(true);
            await oldView.CoreWebView2.Resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(background.IsSleeping);
            manager.SuspendBackgroundTabs(active);
            Assert.True(background.IsSleeping);
            Assert.Equal(1, replacement.CoreWebView2.SuspendCount);
        }
        finally { manager.Detach(); }
    }

    [Fact]
    public async Task TransferCoalescesPendingNativeWorkAcrossManagers()
    {
        var active = new BrowserTab { IsActive = true };
        var background = new BrowserTab();
        var sourceTabs = new List<BrowserTab> { active, background };
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new FakeWebView();
        view.CoreWebView2.SuspendResult = result.Task;
        var views = new Dictionary<Guid, dynamic> { [background.Id] = view };
        var source = new TabHibernationManager(new FakeDownloadStore(), new BrowserSettings());
        var destination = new TabHibernationManager(new FakeDownloadStore(), new BrowserSettings());
        source.Attach(() => sourceTabs, () => views, _ => { }, 10);
        destination.Attach(() => new[] { active, background }, () => views, _ => { }, 10);
        try
        {
            source.SuspendBackgroundTabs(active);
            sourceTabs.Remove(background);
            destination.SuspendBackgroundTabs(active);
            Assert.Equal(1, view.CoreWebView2.SuspendCount);
            result.SetResult(true);
            await view.CoreWebView2.Resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            view.CoreWebView2.SuspendResult = Task.FromResult(true);
            await UntilAsync(() => { destination.SuspendBackgroundTabs(active); return background.IsSleeping; });
            Assert.Equal(2, view.CoreWebView2.SuspendCount);
        }
        finally { source.Detach(); destination.Detach(); }
    }

    private static TabHibernationManager Create(BrowserTab active, BrowserTab background, FakeWebView view, BrowserSettings? settings = null)
    {
        var manager = new TabHibernationManager(new FakeDownloadStore(), settings ?? new BrowserSettings());
        manager.Attach(() => new[] { active, background },
            () => new Dictionary<Guid, dynamic> { [background.Id] = view }, _ => { }, 10);
        return manager;
    }
    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}

