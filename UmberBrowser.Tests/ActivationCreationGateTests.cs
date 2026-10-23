using UmberBrowser.Engine;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class ActivationCreationGateTests
{
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task InternalTabCompletesWhileExternalTabWaitsForExtensionReadiness()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var configured = Signal();
        var extensions = Signal();
        var firstNavigationEntered = Signal();
        var coordinator = new TabReadinessCoordinator();
        var tab = Guid.NewGuid();
        var (popupReady, creator) = coordinator.GetOrAdd(tab);
        Assert.True(creator);
        var control = new object();
        var externalNavigated = false;
        var external = TabEngine.CreateThenNavigateAsync(gate, async () =>
        {
            await configured.Task;
            coordinator.TrySetResult(tab, control);
        }, async () =>
        {
            firstNavigationEntered.SetResult(true);
            await extensions.Task;
            externalNavigated = true;
        }, CancellationToken.None);

        var internalCreated = false;
        var internalNavigated = false;
        var internalTab = TabEngine.CreateThenNavigateAsync(gate, () =>
        {
            internalCreated = true;
            return Task.CompletedTask;
        }, () =>
        {
            internalNavigated = true;
            return Task.CompletedTask;
        }, CancellationToken.None);
        Assert.False(internalCreated);
        configured.SetResult(true);
        await firstNavigationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await internalTab.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(control, await popupReady);
        Assert.True(internalCreated);
        Assert.True(internalNavigated);
        Assert.False(externalNavigated);
        Assert.False(external.IsCompleted);
        extensions.SetResult(true);
        await external.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(externalNavigated);
        Assert.Equal(1, gate.CurrentCount);
    }

    [Fact]
    public async Task CreationFailureReleasesGateAndNeverStartsNavigation()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var navigated = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => TabEngine.CreateThenNavigateAsync(gate,
            () => Task.FromException(new InvalidOperationException("Core failed")),
            () => { navigated = true; return Task.CompletedTask; }, CancellationToken.None));
        Assert.False(navigated);
        Assert.Equal(1, gate.CurrentCount);
        await TabEngine.CreateThenNavigateAsync(gate, () => Task.CompletedTask, () => Task.CompletedTask, CancellationToken.None);
    }

    [Fact]
    public async Task EngineShutdownWhileQueuedDoesNotAcquireOrReleaseSomeoneElsesGate()
    {
        using var gate = new SemaphoreSlim(0, 1);
        using var shutdown = new CancellationTokenSource();
        var called = false;
        var pending = TabEngine.CreateThenNavigateAsync(gate,
            () => { called = true; return Task.CompletedTask; }, () => Task.CompletedTask, shutdown.Token);
        shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(called);
        Assert.Equal(0, gate.CurrentCount);
    }

    [Fact]
    public async Task ShutdownCancelsThePerTabWaitAfterCreationGateWasReleased()
    {
        using var gate = new SemaphoreSlim(1, 1);
        using var shutdown = new CancellationTokenSource();
        var delay = Signal();
        var navigationStarted = Signal();
        var pending = TabEngine.CreateThenNavigateAsync(gate, () => Task.CompletedTask, async () =>
        {
            navigationStarted.SetResult(true);
            await delay.Task.WaitAsync(shutdown.Token);
        }, shutdown.Token);
        await navigationStarted.Task;
        Assert.Equal(1, gate.CurrentCount);
        shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, gate.CurrentCount);
    }
}

