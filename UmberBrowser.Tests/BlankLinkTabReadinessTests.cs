using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using UmberBrowser.Engine;
using UmberBrowser.Models;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Unit tests for production tab readiness coordination and popup adoption.
/// Exercises TabReadinessCoordinator and WebViewIpcBridge.ProcessNewWindowRequestedAsync directly.
/// </summary>
public sealed class BlankLinkTabReadinessTests
{
    private sealed class TestDeferralToken : WebViewIpcBridge.IDeferralToken
    {
        public int CompletionCount { get; private set; }
        public void Complete() => CompletionCount++;
    }

    [Fact]
    public async Task GateContention_TwoCallersShareSingleCreationOperation()
    {
        var coordinator = new TabReadinessCoordinator();
        var tabId = Guid.NewGuid();

        var (task1, isCreator1) = coordinator.GetOrAdd(tabId);
        var (task2, isCreator2) = coordinator.GetOrAdd(tabId);

        Assert.True(isCreator1);
        Assert.False(isCreator2);
        Assert.Same(task1, task2);

        var readyObject = new object();
        coordinator.TrySetResult(tabId, readyObject);

        var result1 = await task1;
        var result2 = await task2;

        Assert.Same(readyObject, result1);
        Assert.Same(readyObject, result2);
    }

    [Fact]
    public void CoreCreated_SetupPending_TryGetReturnsInFlightTask()
    {
        var coordinator = new TabReadinessCoordinator();
        var tabId = Guid.NewGuid();

        var (inFlightTask, isCreator) = coordinator.GetOrAdd(tabId);
        Assert.True(isCreator);

        // Core or control may exist, but setup is pending: TryGet must return the active task
        var hasInFlight = coordinator.TryGet(tabId, out var queriedTask);
        Assert.True(hasInFlight);
        Assert.Same(inFlightTask, queriedTask);
        Assert.False(inFlightTask.IsCompleted);

        coordinator.TrySetResult(tabId, "ready-wv");
        Assert.True(inFlightTask.IsCompletedSuccessfully);

        // Once completed and removed, TryGet returns false
        Assert.False(coordinator.TryGet(tabId, out _));
    }

    [Fact]
    public async Task TwoWaiters_OneCanceledLocally_LeavesOtherWaiterAndUnderlyingTaskIntact()
    {
        var coordinator = new TabReadinessCoordinator();
        var tabId = Guid.NewGuid();

        var (sharedTask, _) = coordinator.GetOrAdd(tabId);

        using var ctsCanceled = new CancellationTokenSource();
        using var ctsActive = new CancellationTokenSource();

        var waiter1 = TabReadinessCoordinator.WaitWithCancellationAsync(sharedTask, ctsCanceled.Token);
        var waiter2 = TabReadinessCoordinator.WaitWithCancellationAsync(sharedTask, ctsActive.Token);

        ctsCanceled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await waiter1);

        // The shared task and the second waiter must remain uncancelled and in flight
        Assert.False(sharedTask.IsCanceled);
        Assert.False(sharedTask.IsCompleted);
        Assert.False(waiter2.IsCompleted);

        coordinator.TrySetResult(tabId, "finished");

        var result2 = await waiter2;
        Assert.Equal("finished", result2);
        Assert.Equal("finished", await sharedTask);
    }

    [Fact]
    public async Task CreationQueuedBehindGate_CreatorDisplayCanceled_SecondWaiterReceivesCompletedResult()
    {
        var coordinator = new TabReadinessCoordinator();
        var gate = new SemaphoreSlim(0, 1);
        var tabId = Guid.NewGuid();

        var (creatorSharedTask, isCreator) = coordinator.GetOrAdd(tabId);
        Assert.True(isCreator);

        var (waiterSharedTask, _) = coordinator.GetOrAdd(tabId);
        Assert.Same(creatorSharedTask, waiterSharedTask);

        using var creatorDisplayCts = new CancellationTokenSource();

        // Simulate creation flow: gate wait is tied to lifetime, display continuation can cancel
        async Task RunCreationFlow(CancellationToken displayCt)
        {
            await gate.WaitAsync(CancellationToken.None);
            coordinator.TrySetResult(tabId, "control-ready");
            if (displayCt.IsCancellationRequested)
            {
                // Display continuation suppressed
                return;
            }
        }

        var creationExecution = RunCreationFlow(creatorDisplayCts.Token);

        // Second waiter (e.g. popup adoption or background activation) awaits the shared task
        var secondWaiterTask = waiterSharedTask;

        // Creator's display is canceled while waiting behind gate
        creatorDisplayCts.Cancel();

        // Gate unlocks and creation proceeds
        gate.Release();
        await creationExecution;

        // The second waiter receives the completed result despite creator display cancellation
        var result = await secondWaiterTask;
        Assert.Equal("control-ready", result);
    }

    [Fact]
    public async Task InitializationFailure_FaultsReadinessTaskExplicitly()
    {
        var coordinator = new TabReadinessCoordinator();
        var tabId = Guid.NewGuid();

        var (sharedTask, _) = coordinator.GetOrAdd(tabId);

        var expectedEx = new InvalidOperationException("Initialization failed");
        coordinator.TrySetException(tabId, expectedEx);

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(async () => await sharedTask);
        Assert.Same(expectedEx, caught);
    }

    [Fact]
    public async Task ClosureDuringCreation_CancelsReadinessTaskCleanly()
    {
        var coordinator = new TabReadinessCoordinator();
        var tabId = Guid.NewGuid();

        var (sharedTask, _) = coordinator.GetOrAdd(tabId);

        coordinator.TryCancel(tabId);

        Assert.True(sharedTask.IsCanceled);
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await sharedTask);
    }

    [Fact]
    public async Task ProductionProcessNewWindowRequested_AdoptsReadyCore_AndCompletesDeferralExactlyOnce()
    {
        var deferral = new TestDeferralToken();
        var fakeCore = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
            typeof(CoreWebView2)) as CoreWebView2;

        BrowserTab? createdTab = null;
        CoreWebView2? assignedCore = null;
        var removedSentinels = new List<BrowserTab>();

        var settings = new BrowserSettings();
        var bridge = new WebViewIpcBridge(
            dispatcher: Dispatcher.CurrentDispatcher,
            settings: settings,
            downloadStore: null!,
            ipcToken: "test-token",
            activeNativeDownloads: new(),
            isTabAlive: id => createdTab != null && createdTab.Id == id,
            getWebView: id => null,
            createTab: url =>
            {
                createdTab = new BrowserTab { Url = url };
                return createdTab;
            },
            activateAsync: _ => Task.CompletedTask,
            closeTab: _ => { },
            ensureTabReadyAsync: (tab, ct) => Task.FromResult<CoreWebView2?>(fakeCore),
            removeSentinelTab: tab => removedSentinels.Add(tab),
            downloadSuppressor: null!);

        await bridge.ProcessNewWindowRequestedAsync(
            deferral: deferral,
            assignNewWindow: core => assignedCore = core,
            uri: "https://example.com/target-window");

        Assert.NotNull(createdTab);
        Assert.Equal("https://example.com/target-window", createdTab!.Url);
        Assert.Same(fakeCore, assignedCore);
        Assert.Empty(removedSentinels);
        Assert.Equal(1, deferral.CompletionCount);
    }

    [Fact]
    public async Task ProductionProcessNewWindowRequested_OnNullReadiness_CleansUpSentinelAndCompletesExactlyOnce()
    {
        var deferral = new TestDeferralToken();
        BrowserTab? createdTab = null;
        CoreWebView2? assignedCore = null;
        var removedSentinels = new List<BrowserTab>();

        var settings = new BrowserSettings();
        var bridge = new WebViewIpcBridge(
            dispatcher: Dispatcher.CurrentDispatcher,
            settings: settings,
            downloadStore: null!,
            ipcToken: "test-token",
            activeNativeDownloads: new(),
            isTabAlive: id => createdTab != null && createdTab.Id == id,
            getWebView: id => null,
            createTab: url =>
            {
                createdTab = new BrowserTab { Url = url };
                return createdTab;
            },
            activateAsync: _ => Task.CompletedTask,
            closeTab: _ => { },
            ensureTabReadyAsync: (tab, ct) => Task.FromResult<CoreWebView2?>(null),
            removeSentinelTab: tab => removedSentinels.Add(tab),
            downloadSuppressor: null!);

        await bridge.ProcessNewWindowRequestedAsync(
            deferral: deferral,
            assignNewWindow: core => assignedCore = core,
            uri: "https://example.com/target-window");

        Assert.NotNull(createdTab);
        Assert.Null(assignedCore);
        Assert.Single(removedSentinels);
        Assert.Same(createdTab, removedSentinels[0]);
        Assert.Equal(1, deferral.CompletionCount);
    }

    [Fact]
    public async Task ProductionProcessNewWindowRequested_OnException_CleansUpSentinelAndCompletesExactlyOnce()
    {
        var deferral = new TestDeferralToken();
        BrowserTab? createdTab = null;
        CoreWebView2? assignedCore = null;
        var removedSentinels = new List<BrowserTab>();

        var settings = new BrowserSettings();
        var bridge = new WebViewIpcBridge(
            dispatcher: Dispatcher.CurrentDispatcher,
            settings: settings,
            downloadStore: null!,
            ipcToken: "test-token",
            activeNativeDownloads: new(),
            isTabAlive: id => createdTab != null && createdTab.Id == id,
            getWebView: id => null,
            createTab: url =>
            {
                createdTab = new BrowserTab { Url = url };
                return createdTab;
            },
            activateAsync: _ => Task.CompletedTask,
            closeTab: _ => { },
            ensureTabReadyAsync: (tab, ct) => Task.FromException<CoreWebView2?>(new InvalidOperationException("Initialization exploded")),
            removeSentinelTab: tab => removedSentinels.Add(tab),
            downloadSuppressor: null!);

        await bridge.ProcessNewWindowRequestedAsync(
            deferral: deferral,
            assignNewWindow: core => assignedCore = core,
            uri: "https://example.com/target-window");

        Assert.NotNull(createdTab);
        Assert.Null(assignedCore);
        Assert.Single(removedSentinels);
        Assert.Same(createdTab, removedSentinels[0]);
        Assert.Equal(1, deferral.CompletionCount);
    }

    [Fact]
    public async Task ProductionProcessNewWindowRequested_OnTabClosureDuringCreation_CleansUpAndCompletesExactlyOnce()
    {
        var deferral = new TestDeferralToken();
        BrowserTab? createdTab = null;
        CoreWebView2? assignedCore = null;
        var removedSentinels = new List<BrowserTab>();

        var fakeCore = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
            typeof(CoreWebView2)) as CoreWebView2;

        var isTabAlive = true;

        var settings = new BrowserSettings();
        var bridge = new WebViewIpcBridge(
            dispatcher: Dispatcher.CurrentDispatcher,
            settings: settings,
            downloadStore: null!,
            ipcToken: "test-token",
            activeNativeDownloads: new(),
            isTabAlive: id => isTabAlive,
            getWebView: id => null,
            createTab: url =>
            {
                createdTab = new BrowserTab { Url = url };
                return createdTab;
            },
            activateAsync: _ => Task.CompletedTask,
            closeTab: _ => { },
            ensureTabReadyAsync: (tab, ct) =>
            {
                // Tab was closed while creation was running
                isTabAlive = false;
                return Task.FromResult<CoreWebView2?>(fakeCore);
            },
            removeSentinelTab: tab => removedSentinels.Add(tab),
            downloadSuppressor: null!);

        await bridge.ProcessNewWindowRequestedAsync(
            deferral: deferral,
            assignNewWindow: core => assignedCore = core,
            uri: "https://example.com/target-window");

        Assert.NotNull(createdTab);
        Assert.Null(assignedCore);
        Assert.Single(removedSentinels);
        Assert.Same(createdTab, removedSentinels[0]);
        Assert.Equal(1, deferral.CompletionCount);
    }
}
