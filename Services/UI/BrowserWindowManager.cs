using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services.LinkPreview;
using StrideBrowser.Services.Reader;
using StrideBrowser.Services.Startup;

namespace StrideBrowser.Services.UI;

/// <summary>Owns window scopes and coordinates tab transfers and application shutdown.</summary>
public sealed class BrowserWindowManager
{
    private sealed class Entry(Window window, TabEngine engine, ListBox strip, IServiceProvider services)
    {
        public Window Window { get; } = window;
        public TabEngine Engine { get; } = engine;
        public ListBox Strip { get; } = strip;
        public IServiceProvider Services { get; } = services;
        public IServiceScope? Scope { get; set; }
        public bool Closing { get; set; }
    }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly List<Entry> _windows = [];
    private Entry? _active;
    private bool _transferring;

    public BrowserWindowManager(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        SingleInstanceManager.InstanceMessageReceived += args => Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var target = _active is { Closing: false } ? _active : _windows.FirstOrDefault(w => !w.Closing);
            if (target is null) return;
            try
            {
                await target.Services.GetRequiredService<BrowserWindowContext>().Ready.Task;
                await target.Services.GetRequiredService<StartupCoordinator>().HandleCommandLineArgsAsync(args);
                if (target.Window.WindowState == WindowState.Minimized) target.Window.WindowState = WindowState.Normal;
                target.Window.Activate();
            }
            catch (Exception ex) { Trace.WriteLine($"Opening external URL failed: {ex}"); }
        });
    }

    public void Register(Window window, TabEngine engine, ListBox strip, IServiceProvider services)
    {
        var entry = new Entry(window, engine, strip, services);
        _windows.Add(entry);
        window.Activated += (_, _) => _active = entry;
        window.Closed += (_, _) =>
        {
            _windows.Remove(entry);
            if (_active == entry) _active = _windows.LastOrDefault(w => !w.Closing);
            if (Application.Current.MainWindow == window) Application.Current.MainWindow = _active?.Window;
            // The scope owns this window's engine, view model, reader and link preview.
            // Disposing it releases the window's subscriptions without touching the
            // process wide stores, which are singletons and live longer.
            entry.Scope?.Dispose();
        };
    }

    /// <summary>
    /// The strip of another open window under the given screen point, or null when the
    /// pointer is not over one. Used to decide when a drag has left this window and should
    /// become a cross-window drag.
    /// </summary>
    public ListBox? StripUnderPoint(Point screenPoint, TabEngine exclude)
    {
        foreach (var entry in _windows)
        {
            if (entry.Closing || entry.Engine == exclude || !entry.Window.IsVisible) continue;
            try
            {
                var size = entry.Strip.RenderSize;
                if (entry.Strip.IsVisible && entry.Strip.IsHitTestVisible &&
                    new Rect(0, 0, size.Width, size.Height).Contains(entry.Strip.PointFromScreen(screenPoint)))
                    return entry.Strip;
            }
            catch (InvalidOperationException)
            {
                // Window torn down between the visibility check and the hit test.
            }
        }
        return null;
    }

    /// <summary>
    /// Docks a tab into the window that owns the given strip. Returns false when no open
    /// window owns it, which leaves the caller to tear the tab off instead.
    /// </summary>
    public async Task<bool> DockAt(TabTransferHost source, ListBox strip, BrowserTab tab, int index, Point screenPoint)
    {
        if (_transferring || source is null || tab is null) return false;
        var to = _windows.FirstOrDefault(w => !w.Closing && ReferenceEquals(w.Strip, strip) && !ReferenceEquals(w.Engine, source.Engine));
        if (to is null) return false;
        return await DockAsync(source.Engine, to.Engine, tab, Math.Clamp(index, 0, to.Engine.Tabs.Count));
    }

    public Window CreateWindow(bool restoreStartupSession = false)
    {
        var scope = _scopeFactory.CreateScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<BrowserWindowContext>().RestoreStartupSession = restoreStartupSession;
            Composition.WireWindowServices(scope.ServiceProvider);
            var window = scope.ServiceProvider.GetRequiredService<MainWindow>();
            _windows.Single(w => w.Window == window).Scope = scope;
            return window;
        }
        catch { scope.Dispose(); throw; }
    }

    public bool Owns(TabEngine engine, BrowserTab tab) =>
        !_transferring && _windows.Any(w => w.Engine == engine && !w.Closing) && engine.CanTransfer(tab);

    /// <summary>
    /// Waits for a window to be able to show a tab. False means the window closed before
    /// it got there, which is an aborted transfer and not a fault. A window that failed to
    /// load is a fault and is allowed through, so it surfaces instead of looking like a
    /// user decision.
    /// </summary>
    public static async Task<bool> WaitForReadyAsync(Task ready, string windowName)
    {
        try { await ready; return true; }
        catch (OperationCanceledException)
        {
            Trace.WriteLine($"Dock abandoned: {windowName} closed before it was ready");
            return false;
        }
    }

    public async Task<bool> DockAsync(TabEngine source, TabEngine destination, BrowserTab tab, int index)
    {
        if (_transferring || source == destination || !Owns(source, tab)) return false;
        var from = _windows.FirstOrDefault(w => w.Engine == source && !w.Closing);
        var to = _windows.FirstOrDefault(w => w.Engine == destination && !w.Closing);
        if (from is null || to is null) return false;
        _transferring = true;
        try
        {
            if (!await WaitForReadyAsync(
                    to.Services.GetRequiredService<BrowserWindowContext>().Ready.Task,
                    to.Window.Name ?? "a window"))
                return false;
            if (from.Closing || to.Closing || !source.CanTransfer(tab)) return false;
            from.Services.GetRequiredService<ILinkPreviewService>().Dismiss();
            if (!source.TransferTo(destination, tab, index)) return false;
            if (from.Services.GetRequiredService<IReaderService>() is ReaderService reader &&
                to.Services.GetRequiredService<IReaderService>() is ReaderService targetReader)
                reader.TransferSessionTo(targetReader, tab.Id);
            destination.SwitchTo(tab);
            await destination.ActivateAsync(tab);
            to.Strip.ScrollIntoView(tab);
            to.Window.Activate();
            if (source.Tabs.Count == 0) from.Window.Close();
            return true;
        }
        finally { _transferring = false; }
    }

    public async Task DetachAsync(TabEngine source, BrowserTab tab, Point screenPoint)
    {
        if (!Owns(source, tab)) return;
        var from = _windows.Single(w => w.Engine == source);
        // Tearing off the only tab moves its existing window.
        if (source.Tabs.Count == 1)
        {
            from.Window.WindowState = WindowState.Normal;
            TabDragGeometry.PositionWindow(from.Window, screenPoint);
            from.Window.Activate();
            return;
        }
        var window = CreateWindow();
        var to = _windows.Single(w => w.Window == window);
        window.Width = Math.Max(window.MinWidth, from.Window.RestoreBounds.Width);
        window.Height = Math.Max(window.MinHeight, from.Window.RestoreBounds.Height);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        try
        {
            window.Show();
            TabDragGeometry.PositionWindow(window, screenPoint);
            if (!await DockAsync(source, to.Engine, tab, 0)) window.Close();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Tab detach failed: {ex.Message}");
            if (to.Engine.Tabs.Count == 0) window.Close();
            throw;
        }
    }

    /// <summary>
    /// The tab rows this window's tabs contribute, in tab order, with the active tab's id.
    /// </summary>
    private SnapshotSource SnapshotOf(Entry entry) => new(
        string.IsNullOrEmpty(entry.Window.Name) ? $"window{_windows.IndexOf(entry)}" : entry.Window.Name,
        entry.Engine.Tabs
            .Select(t => (t.Id.ToString(), t.Url, t.Title, t.IsPinned))
            .ToArray(),
        entry.Engine.ActiveTab?.Id.ToString(),
        entry.Closing);

    /// <summary>
    /// The WPF closing entry for a window. <see cref="Window.OnClosing(CancelEventArgs)"/>
    /// reads <c>e.Cancel</c> the moment this returns, so the cancel and the closing mark
    /// are set here, before anything is awaited. Everything else runs on
    /// <see cref="CloseAsync"/>, which the event cannot wait for and which never lets a
    /// fault reach the dispatcher.
    /// </summary>
    public void Close(Window window, CancelEventArgs e)
    {
        var entry = _windows.FirstOrDefault(w => w.Window == window);
        if (entry is null || entry.Closing) return;
        e.Cancel = true;
        entry.Closing = true;
        _ = CloseAsync(entry, window);
    }

    /// <summary>
    /// Runs what a close owes and then closes the window for real. Starts only after
    /// <see cref="Close"/> has cancelled the close and marked the entry, so from here on
    /// a fault cannot be recovered by the user pressing close again: the entry is already
    /// marked closing and a second close returns without touching it.
    /// </summary>
    private async Task CloseAsync(Entry entry, Window window)
    {
        await RunCloseSteps(
            ("window shutdown writes", () => WriteShutdownAsync(entry)),
            ("tab recycle after window close", () => RecycleHostsAsync(entry, window)),
            ("engine shutdown", () => { entry.Engine.Shutdown(); return Task.CompletedTask; }),
            ("store shutdown flush", () => FlushStoresAsync(entry)));
        // The final close is scheduled on the window's own dispatcher. Scheduling it can
        // itself fault when the dispatcher is already going away; that is traced rather than
        // left to escape as an unobserved fault. Whether the window then actually closes is
        // WPF behaviour this project does not exercise.
        try { _ = window.Dispatcher.BeginInvoke(window.Close); }
        catch (Exception ex) { Trace.WriteLine($"Scheduling the final close failed: {ex}"); }
    }

    /// <summary>
    /// Runs the steps of a close in order, reporting a fault in one of them rather than
    /// letting it escape, so that every later step still runs and the close reaches the
    /// point where its final close is scheduled. A step that throws used to skip the rest,
    /// which left the window on screen with its close already cancelled and no way left to
    /// shut it, and handed the fault to the dispatcher as an unhandled exception.
    /// </summary>
    public static async Task RunCloseSteps(params (string What, Func<Task> Step)[] steps)
    {
        foreach (var (what, step) in steps)
        {
            try { await step(); }
            catch (Exception ex) { Trace.WriteLine($"{what} failed: {ex}"); }
        }
    }

    private async Task FlushStoresAsync(Entry entry)
    {
        if (_windows.Any(window => window != entry && !window.Closing)) return;
        await RunCloseSteps(
            ("history flush", async () => ReportFlush("history", await entry.Services.GetRequiredService<IHistoryStore>().FlushAsync())),
            ("download flush", async () => ReportFlush("downloads", await entry.Services.GetRequiredService<IDownloadStore>().FlushAsync())),
            ("saved tabs flush", async () => ReportFlush("saved tabs", await entry.Services.GetRequiredService<IOneTabStore>().FlushAsync())));
    }

    private static void ReportFlush(string store, bool success)
    {
        if (!success) Trace.WriteLine($"Shutdown could not persist {store}. The last successful atomic file remains on disk.");
    }

    private async Task WriteShutdownAsync(Entry entry)
    {
        var context = entry.Services.GetRequiredService<BrowserWindowContext>();
        context.IsClosing = true;
        // Anything waiting to dock into this window must stop waiting, or it waits
        // for a window that is already gone.
        context.Ready.TrySetCanceled();
        var lastWindow = _windows.All(w => w == entry || w.Closing);
        var settings = entry.Services.GetRequiredService<BrowserSettings>();
        var sessionStore = entry.Services.GetRequiredService<ISessionStore>();
        var settingsStore = entry.Services.GetRequiredService<ISettingsStore>();
        var closedTabStore = entry.Services.GetRequiredService<IClosedTabStore>();
        var historyStore = entry.Services.GetRequiredService<IHistoryStore>();

        // Read the whole registry before anything can unregister from it, so the snapshot
        // is taken from the set of windows that were open when this close started.
        var registry = _windows.ToArray();
        var snapshot = SnapshotOf(entry);

        try
        {
            var cohort = SessionSnapshot.Cohort(registry.Select(SnapshotOf).ToArray(), snapshot.Name);
            var result = await new WindowShutdownWrites
            {
                WriteSnapshot = settings.RestoreSessionOnStartup && !settings.ClearDataOnExit,
                ClearBrowsingData = lastWindow && settings.ClearDataOnExit,
                WriteSnapshotRows = () => sessionStore.TrySave(SessionSnapshot.Rows(cohort)),
                ClearSnapshot = sessionStore.TryClear,
                ClearClosedTabHistory = closedTabStore.Clear,
                ClearHistory = historyStore.Clear,
                SaveSettings = () => settingsStore.TrySave(settings),
                ClearBrowsingDataAsync = async () =>
                {
                    if (entry.Engine.GetCoreWebView2()?.Profile is not { } profile) return;
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await profile.ClearBrowsingDataAsync().WaitAsync(cts.Token);
                }
            }.RunAsync();
            if (!result.Complete)
                Trace.WriteLine($"Window shutdown incomplete for {snapshot.Name}: {result}");
        }
        catch (Exception ex)
        {
            // The writes are reported rather than retried. The window still has to shut down.
            Trace.WriteLine($"Window shutdown planning failed: {ex}");
        }
    }

    /// <summary>
    /// Hands the tabs this window was still hosting back to the windows that took them,
    /// and drops the link preview. A torn-off tab keeps its control, and that control
    /// dies with the window it was built in, so those tabs are rebuilt before the window
    /// goes away. One window failing to rebuild its tabs does not stop the others.
    /// Preview dismissal and tab recycling are isolated so a fault in either does not
    /// skip the other.
    /// </summary>
    private async Task RecycleHostsAsync(Entry entry, Window window)
    {
        var handle = WebView2WindowBinding.HandleOf(window);
        var survivors = _windows
            .Where(w => w != entry && !w.Closing)
            .Select(w => (Func<Task>)(() => w.Engine.RecycleTabsHostedBy(handle)))
            .ToArray();
        await RecycleHostsCoreAsync(entry.Services.GetRequiredService<ILinkPreviewService>(), survivors);
    }

    /// <summary>
    /// The recycle half of a close, run for real by <see cref="RecycleHostsAsync"/>. The
    /// link preview is dismissed first and each surviving window's tabs are rebuilt after,
    /// each in isolation, so a fault in the preview or in one survivor does not skip the
    /// rest. Kept separate so the isolation is measured against the code the close path
    /// runs rather than a copy of it.
    /// </summary>
    public static async Task RecycleHostsCoreAsync(ILinkPreviewService linkPreview, IReadOnlyList<Func<Task>> recycleSurvivors)
    {
        // Dismiss link preview. Isolated so a fault here does not skip tab recycling.
        try { linkPreview.Dismiss(); }
        catch (Exception ex) { Trace.WriteLine($"Link preview dismiss failed: {ex}"); }

        // Recycle tabs in surviving windows. Each window's recycle is isolated.
        foreach (var recycle in recycleSurvivors)
        {
            try { await recycle(); }
            catch (Exception ex) { Trace.WriteLine($"Tab recycle after window close failed: {ex.Message}"); }
        }
    }
}
