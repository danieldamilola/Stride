using System.Collections.ObjectModel;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Downloads;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class DownloadOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttachedDownloadProtectsOnlyItsOwningTabIncludingPausedNativeWork(bool paused)
    {
        var store = new MemoryStore();
        var coordinator = new DownloadCoordinator(store);
        var owner = Guid.NewGuid();
        var item = new DownloadItem();
        coordinator.Attach(item, new Transfer(), owner);
        if (paused) { item.State = DownloadState.Paused; item.CanResumeNative = true; }
        Assert.True(coordinator.IsTabProtectedByDownload(owner));
        Assert.False(coordinator.IsTabProtectedByDownload(Guid.NewGuid()));
    }

    [Fact]
    public void PausedAttachedOperationRemainsProtectedEvenBeforeResumeCapabilityArrives()
    {
        var store = new MemoryStore();
        var coordinator = new DownloadCoordinator(store);
        var tab = Guid.NewGuid();
        var item = new DownloadItem();
        coordinator.Attach(item, new Transfer(), tab);
        item.State = DownloadState.Paused;
        Assert.False(item.CanResumeNative);
        Assert.True(coordinator.IsTabProtectedByDownload(tab));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentHiddenControllerDoesNotProtectTheUiOriginTab(bool paused)
    {
        var store = new MemoryStore();
        var coordinator = new DownloadCoordinator(store);
        var owner = Guid.NewGuid();
        var item = new DownloadItem();
        coordinator.Attach(item, new Transfer(), owner, dependsOnOriginTab: false);
        if (paused) { item.State = DownloadState.Paused; item.CanResumeNative = true; }
        Assert.False(coordinator.IsTabProtectedByDownload(owner));
        Assert.False(coordinator.IsTabProtectedByDownload(Guid.NewGuid()));
    }

    [Fact]
    public void UnknownAttachedOriginConservativelyProtectsAllTabs()
    {
        var store = new MemoryStore();
        var coordinator = new DownloadCoordinator(store);
        coordinator.Attach(new DownloadItem(), new Transfer(), Guid.Empty);
        Assert.True(coordinator.IsTabProtectedByDownload(Guid.NewGuid()));
        Assert.True(coordinator.IsTabProtectedByDownload(Guid.NewGuid()));
    }

    [Fact]
    public void UnattachedActiveRecordIsConservativeButRestoredPausedHistoryIsNotLive()
    {
        var store = new MemoryStore();
        var coordinator = new DownloadCoordinator(store);
        var item = new DownloadItem();
        store.Add(item);
        Assert.True(coordinator.IsTabProtectedByDownload(Guid.NewGuid()));
        item.State = DownloadState.Paused;
        Assert.False(coordinator.IsTabProtectedByDownload(Guid.NewGuid()));
        item.CanResumeNative = true;
        Assert.True(coordinator.IsTabProtectedByDownload(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(DownloadState.Completed)]
    [InlineData(DownloadState.Cancelled)]
    [InlineData(DownloadState.Failed)]
    public void TerminalStateReleasesTheTransferAndItsOwnership(DownloadState state)
    {
        var store = new MemoryStore();
        var coordinator = new DownloadCoordinator(store);
        var owner = Guid.NewGuid();
        var item = new DownloadItem();
        var transfer = new Transfer();
        coordinator.Attach(item, transfer, owner);
        item.State = state;
        Assert.False(coordinator.IsTabProtectedByDownload(owner));
        // A later paused history state cannot regain the old native transfer.
        item.State = DownloadState.Paused;
        coordinator.Resume(item.Id);
        Assert.Equal(0, transfer.Resumes);
        Assert.False(coordinator.IsTabProtectedByDownload(owner));
    }

    [Fact]
    public void CancelDetachesAndCancelsTheNativeTransferOnce()
    {
        var store = new MemoryStore();
        var coordinator = new DownloadCoordinator(store);
        var owner = Guid.NewGuid();
        var item = new DownloadItem();
        var transfer = new Transfer();
        coordinator.Attach(item, transfer, owner);
        coordinator.Cancel(item.Id);
        coordinator.Cancel(item.Id);
        Assert.Equal(1, transfer.Cancellations);
        Assert.False(coordinator.IsTabProtectedByDownload(owner));
        Assert.False(coordinator.IsTabProtectedByDownload(Guid.NewGuid()));
    }

    [Fact]
    public void DetachedPausedOperationDoesNotKeepItsOldTabProtected()
    {
        var store = new MemoryStore();
        var coordinator = new DownloadCoordinator(store);
        var owner = Guid.NewGuid();
        var item = new DownloadItem();
        coordinator.Attach(item, new Transfer(), owner);
        item.State = DownloadState.Paused;
        coordinator.Detach(item.Id);
        Assert.False(coordinator.IsTabProtectedByDownload(owner));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveDownloadAllowsUnrelatedTabEvictionWithoutUnloadingItsOwner(bool paused)
    {
        var store = new MemoryStore();
        var coordinator = new DownloadCoordinator(store);
        var active = new BrowserTab { IsActive = true };
        var owner = new BrowserTab { Url = "https://example.org/file" };
        var unrelated = new BrowserTab { Url = "https://example.org/other" };
        var item = new DownloadItem();
        coordinator.Attach(item, new Transfer(), owner.Id);
        if (paused) item.State = DownloadState.Paused;
        var views = new Dictionary<Guid, dynamic>
        {
            [active.Id] = new FakeWebView(), [owner.Id] = new FakeWebView(), [unrelated.Id] = new FakeWebView()
        };
        var manager = new TabHibernationManager(store, new BrowserSettings());
        manager.Attach(() => new[] { active, owner, unrelated }, () => views, id => views.Remove(id), 1);
        manager.SetDownloadProtectionCheck(coordinator.IsTabProtectedByDownload);
        try
        {
            manager.EvictExcessWebViews(active);
            Assert.False(owner.IsHibernated);
            Assert.True(unrelated.IsHibernated);
            Assert.True(views.ContainsKey(owner.Id));
        }
        finally { manager.Detach(); }
    }

    [Fact]
    public void DownloadOwnershipFollowsTheStableTabIdAcrossWindows()
    {
        var store = new MemoryStore();
        var coordinator = new DownloadCoordinator(store);
        var moved = new BrowserTab { Url = "https://example.org/file" };
        coordinator.Attach(new DownloadItem(), new Transfer(), moved.Id);
        var sourceActive = new BrowserTab { IsActive = true };
        var sourceOther = new BrowserTab { Url = "https://example.org/other" };
        var sourceViews = new Dictionary<Guid, dynamic> { [sourceOther.Id] = new FakeWebView() };
        var destinationViews = new Dictionary<Guid, dynamic> { [moved.Id] = new FakeWebView() };
        var source = new TabHibernationManager(store, new BrowserSettings());
        var destination = new TabHibernationManager(store, new BrowserSettings());
        source.Attach(() => new[] { sourceActive, sourceOther }, () => sourceViews, id => sourceViews.Remove(id), 0);
        destination.Attach(() => new[] { moved }, () => destinationViews, id => destinationViews.Remove(id), 0);
        source.SetDownloadProtectionCheck(coordinator.IsTabProtectedByDownload);
        destination.SetDownloadProtectionCheck(coordinator.IsTabProtectedByDownload);
        try
        {
            source.EvictExcessWebViews(sourceActive);
            destination.EvictExcessWebViews(sourceActive);
            Assert.True(sourceOther.IsHibernated);
            Assert.False(moved.IsHibernated);
        }
        finally { source.Detach(); destination.Detach(); }
    }

    [Fact]
    public async Task DownloadStartingDuringPendingSuspendWakesItsOwner()
    {
        var store = new MemoryStore();
        var coordinator = new DownloadCoordinator(store);
        var active = new BrowserTab { IsActive = true };
        var tab = new BrowserTab { Url = "https://example.org/file" };
        var view = new FakeWebView();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CoreWebView2.SuspendResult = completion.Task;
        var manager = new TabHibernationManager(store, new BrowserSettings());
        manager.Attach(() => new[] { active, tab }, () => new Dictionary<Guid, dynamic> { [tab.Id] = view }, _ => { }, 10);
        manager.SetDownloadProtectionCheck(coordinator.IsTabProtectedByDownload);
        try
        {
            manager.SuspendBackgroundTabs(active);
            coordinator.Attach(new DownloadItem(), new Transfer(), tab.Id);
            completion.SetResult(true);
            await view.CoreWebView2.Resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(tab.IsSleeping);
            Assert.False(view.CoreWebView2.IsSuspended);
        }
        finally { manager.Detach(); }
    }

    [Fact]
    public void ClearAllBatchesCancellationAndClearsTheStoreOnce()
    {
        var store = new MemoryStore();
        var coordinator = new DownloadCoordinator(store);
        var first = new Transfer { OnCancel = () => Assert.True(store.IsUpdating) };
        var second = new Transfer { OnCancel = () => Assert.True(store.IsUpdating) };
        coordinator.Attach(new DownloadItem(), first, Guid.NewGuid());
        var paused = new DownloadItem();
        coordinator.Attach(paused, second, Guid.NewGuid());
        paused.State = DownloadState.Paused;
        var completed = new DownloadItem { State = DownloadState.Completed };
        store.Add(completed);
        coordinator.ClearAll();
        Assert.Equal(1, first.Cancellations);
        Assert.Equal(1, second.Cancellations);
        Assert.Equal(DownloadState.Completed, completed.State);
        Assert.Equal(1, store.BeginCount);
        Assert.Equal(1, store.ClearCount);
        Assert.False(store.IsUpdating);
        Assert.Empty(store.Items);
    }

    private sealed class Transfer : IDownloadTransfer
    {
        internal int Cancellations;
        internal int Resumes;
        internal Action? OnCancel;
        public bool CanResume => true;
        public void Pause() { }
        public void Resume() => Resumes++;
        public void Cancel() { Cancellations++; OnCancel?.Invoke(); }
    }
    private sealed class MemoryStore : IDownloadStore
    {
        public ObservableCollection<DownloadItem> Items { get; } = new();
        internal int BeginCount;
        internal int ClearCount;
        internal bool IsUpdating;
        public void Add(DownloadItem item) => Items.Add(item);
        public DownloadItem? Get(string id) => Items.FirstOrDefault(item => item.Id == id);
        public void Remove(string id) { if (Get(id) is { } item) Items.Remove(item); }
        public void ClearCompleted() { }
        public void ClearAll() { Assert.True(IsUpdating); ClearCount++; Items.Clear(); }
        public IDisposable BeginUpdate() { BeginCount++; IsUpdating = true; return new Scope(() => IsUpdating = false); }
        private sealed class Scope(Action action) : IDisposable { public void Dispose() => action(); }
    }
}
