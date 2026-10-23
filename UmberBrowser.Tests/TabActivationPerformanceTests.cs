using System;
using System.Threading;
using System.Threading.Tasks;
using UmberBrowser.Engine;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Covers the production creation boundary used by TabEngine.ActivateAsync.
///
/// TabEngine hands native control setup and that tab's first navigation to
/// TabCreationCoordinator. These tests drive the same coordinator with the same
/// ordering the engine uses, so they prove the gate is released before the first
/// navigation wait instead of asserting on source text.
/// </summary>
public sealed class TabActivationPerformanceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class Control
    {
        public Control(string name) => Name = name;

        public string Name { get; }
    }

    [Fact]
    public async Task InternalTab_BecomesReadyWhileAnotherTabsExtensionWaitIsHeld()
    {
        var coordinator = new TabCreationCoordinator();
        var firstNavigationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstNavigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstNavigationFinished = false;

        // Tab A has an external URL. Its setup finished, then its first navigation
        // waits for the shared extension registration. This is the held gate the
        // controlled probe reproduces with a real TabEngine and WebView2.
        var tabA = coordinator.RunAsync(
            setupAsync: () => Task.FromResult<Control?>(new Control("A")),
            firstNavigationAsync: async _ =>
            {
                firstNavigationEntered.SetResult();
                await releaseFirstNavigation.Task;
                firstNavigationFinished = true;
            },
            CancellationToken.None);

        await firstNavigationEntered.Task.WaitAsync(Timeout);

        // Tab B is an internal page. It needs no extension registration, so its
        // setup and its first navigation must both finish while A is still held.
        var tabBSetupStarted = false;
        var tabBFirstNavigationRan = false;
        var tabB = coordinator.RunAsync(
            setupAsync: () =>
            {
                tabBSetupStarted = true;
                return Task.FromResult<Control?>(new Control("B"));
            },
            firstNavigationAsync: _ =>
            {
                tabBFirstNavigationRan = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await tabB.WaitAsync(Timeout);

        Assert.True(tabBSetupStarted);
        Assert.True(tabBFirstNavigationRan);
        Assert.False(tabA.IsCompleted, "tab A's extension wait must not finish before it is released");

        releaseFirstNavigation.SetResult();
        await tabA.WaitAsync(Timeout);
        Assert.True(firstNavigationFinished);
    }

    [Fact]
    public async Task RapidSelection_DoesNotCancelTheHeldFirstNavigationAndLaterSelectionsStillRun()
    {
        var coordinator = new TabCreationCoordinator();
        var heldNavigationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHeldNavigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heldNavigationFinished = false;

        // First visit to A. Its shared registration wait is in flight.
        var firstA = coordinator.RunAsync(
            setupAsync: () => Task.FromResult<Control?>(new Control("A1")),
            firstNavigationAsync: async _ =>
            {
                heldNavigationEntered.SetResult();
                await releaseHeldNavigation.Task;
                heldNavigationFinished = true;
            },
            CancellationToken.None);
        await heldNavigationEntered.Task.WaitAsync(Timeout);

        // Select B, then select A again while the first wait is still held. A tab
        // switch is display work; it must not cancel the shared registration the
        // first navigation is waiting on.
        var bSelected = coordinator.RunAsync(
            setupAsync: () => Task.FromResult<Control?>(new Control("B")),
            firstNavigationAsync: _ => Task.CompletedTask,
            CancellationToken.None);
        await bSelected.WaitAsync(Timeout);

        var secondA = coordinator.RunAsync(
            setupAsync: () => Task.FromResult<Control?>(new Control("A2")),
            firstNavigationAsync: _ => Task.CompletedTask,
            CancellationToken.None);
        await secondA.WaitAsync(Timeout);

        Assert.False(firstA.IsCompleted, "selecting away and back must not complete or cancel the shared wait");

        releaseHeldNavigation.SetResult();
        await firstA.WaitAsync(Timeout);
        Assert.True(heldNavigationFinished);
    }

    [Fact]
    public async Task CancelWhileWaitingForTheSetupGate_DoesNotRunSetupAndLeavesTheGateUsable()
    {
        var coordinator = new TabCreationCoordinator();
        var holderSetupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHolderSetup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holderNavigationRan = false;

        // One control owns the serialized setup slot.
        var holder = coordinator.RunAsync(
            setupAsync: async () =>
            {
                holderSetupEntered.SetResult();
                await releaseHolderSetup.Task;
                return new Control("holder");
            },
            firstNavigationAsync: _ =>
            {
                holderNavigationRan = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        await holderSetupEntered.Task.WaitAsync(Timeout);

        // A tab closed while it was queued behind the setup slot cancels through
        // the engine lifetime token. Its setup and navigation must not run.
        using var closedTabCts = new CancellationTokenSource();
        var closedTabSetupRan = false;
        var closedTabNavigationRan = false;
        var closedTab = coordinator.RunAsync(
            setupAsync: () =>
            {
                closedTabSetupRan = true;
                return Task.FromResult<Control?>(new Control("closed"));
            },
            firstNavigationAsync: _ =>
            {
                closedTabNavigationRan = true;
                return Task.CompletedTask;
            },
            closedTabCts.Token);

        closedTabCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closedTab.WaitAsync(Timeout));

        Assert.False(closedTabSetupRan);
        Assert.False(closedTabNavigationRan);

        releaseHolderSetup.SetResult();
        await holder.WaitAsync(Timeout);
        Assert.True(holderNavigationRan);

        // The cancelled waiter must not have leaked the setup slot.
        var next = coordinator.RunAsync(
            setupAsync: () => Task.FromResult<Control?>(new Control("next")),
            firstNavigationAsync: _ => Task.CompletedTask,
            CancellationToken.None);
        await next.WaitAsync(Timeout);
    }

    [Fact]
    public async Task SetupFailure_ReleasesTheGateForTheNextTab()
    {
        var coordinator = new TabCreationCoordinator();
        var failedNavigationRan = false;

        var failed = coordinator.RunAsync<Control>(
            setupAsync: () => Task.FromException<Control?>(new InvalidOperationException("runtime failed")),
            firstNavigationAsync: _ =>
            {
                failedNavigationRan = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.WaitAsync(Timeout));
        Assert.False(failedNavigationRan);

        var nextNavigationRan = false;
        var next = coordinator.RunAsync(
            setupAsync: () => Task.FromResult<Control?>(new Control("next")),
            firstNavigationAsync: _ =>
            {
                nextNavigationRan = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await next.WaitAsync(Timeout);
        Assert.True(nextNavigationRan);
    }

    [Fact]
    public async Task AbandonedSetup_SkipsFirstNavigation()
    {
        var coordinator = new TabCreationCoordinator();
        var navigationRan = false;

        var abandoned = await coordinator.RunAsync<Control>(
            setupAsync: () => Task.FromResult<Control?>(null),
            firstNavigationAsync: _ =>
            {
                navigationRan = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Null(abandoned);
        Assert.False(navigationRan);
    }

    [Fact]
    public async Task SharedReadiness_IsVisibleToPopupAdoptionWhileTheFirstNavigationWaits()
    {
        var coordinator = new TabCreationCoordinator();
        var readiness = new TabReadinessCoordinator();
        var tabId = Guid.NewGuid();
        var navigationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNavigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // TabEngine publishes the shared readiness task during setup, before the
        // first navigation starts. Popup adoption waits on that task, not on the
        // navigation, so it must not be held behind the registration wait.
        var (sharedTask, isCreator) = readiness.GetOrAdd(tabId);
        Assert.True(isCreator);

        var control = new Control("popup-sentinel");
        var creation = coordinator.RunAsync(
            setupAsync: () =>
            {
                readiness.TrySetResult(tabId, control);
                return Task.FromResult<Control?>(control);
            },
            firstNavigationAsync: async _ =>
            {
                navigationEntered.SetResult();
                await releaseNavigation.Task;
            },
            CancellationToken.None);

        await navigationEntered.Task.WaitAsync(Timeout);
        Assert.True(sharedTask.IsCompletedSuccessfully);
        Assert.Same(control, await sharedTask.WaitAsync(Timeout));

        releaseNavigation.SetResult();
        await creation.WaitAsync(Timeout);
    }

    [Fact]
    public async Task HibernationWake_RunsSetupAgainOnlyAfterThePreviousFirstNavigationSettles()
    {
        var coordinator = new TabCreationCoordinator();
        var firstFinished = false;
        var secondSetupRan = false;

        var first = coordinator.RunAsync(
            setupAsync: () => Task.FromResult<Control?>(new Control("first")),
            firstNavigationAsync: async _ =>
            {
                await Task.Yield();
                firstFinished = true;
            },
            CancellationToken.None);

        await first.WaitAsync(Timeout);
        Assert.True(firstFinished);

        // A wake rebuilds the control. It must get a fresh setup slot instead of
        // being skipped as if the previous control were still being built.
        var second = coordinator.RunAsync(
            setupAsync: () =>
            {
                secondSetupRan = true;
                return Task.FromResult<Control?>(new Control("second"));
            },
            firstNavigationAsync: _ => Task.CompletedTask,
            CancellationToken.None);

        await second.WaitAsync(Timeout);
        Assert.True(secondSetupRan);
    }
}
