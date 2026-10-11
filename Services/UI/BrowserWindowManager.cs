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

    public async Task<bool> DockAsync(TabEngine source, TabEngine destination, BrowserTab tab, int index)
    {
        if (_transferring || source == destination || !Owns(source, tab)) return false;
        var from = _windows.FirstOrDefault(w => w.Engine == source && !w.Closing);
        var to = _windows.FirstOrDefault(w => w.Engine == destination && !w.Closing);
        if (from is null || to is null) return false;
        _transferring = true;
        try
        {
            await to.Services.GetRequiredService<BrowserWindowContext>().Ready.Task;
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

    public async void Close(Window window, CancelEventArgs e)
    {
        var entry = _windows.FirstOrDefault(w => w.Window == window);
        if (entry is null || entry.Closing) return;
        e.Cancel = true;
        entry.Closing = true;
        var context = entry.Services.GetRequiredService<BrowserWindowContext>();
        context.IsClosing = true;
        context.Ready.TrySetCanceled();
        var lastWindow = _windows.All(w => w == entry || w.Closing);
        var settings = entry.Services.GetRequiredService<BrowserSettings>();
        try
        {
            if (settings.RestoreSessionOnStartup && !settings.ClearDataOnExit)
            {
                // Restore reopens the tab that was on top, so exactly one entry carries it.
                var surviving = _windows.Where(w => !w.Closing).ToArray();
                var owner = surviving.LastOrDefault() ?? entry;
                var activeId = owner.Engine.ActiveTab?.Id;
                var tabs = surviving.SelectMany(w => w.Engine.Tabs)
                    .Where(t => !InternalUrls.IsInternal(t.Url))
                    .Select(t => (t.Url, t.Title, t.IsPinned, IsActive: t.Id == activeId)).ToArray();
                entry.Services.GetRequiredService<ISessionStore>().Save(tabs);
            }
            if (lastWindow && settings.ClearDataOnExit && entry.Engine.GetCoreWebView2()?.Profile is { } profile)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await profile.ClearBrowsingDataAsync().WaitAsync(cts.Token);
            }
            entry.Services.GetRequiredService<ISettingsStore>().Save(settings);
        }
        catch (Exception ex) { Trace.WriteLine($"Window shutdown failed: {ex}"); }
        finally
        {
            entry.Services.GetRequiredService<ILinkPreviewService>().Dismiss();
            // A tab that was torn out keeps its control, and that control dies with the
            // window it was built in. Rebuild those tabs before the window goes away.
            var handle = WebView2WindowBinding.HandleOf(window);
            foreach (var other in _windows.Where(w => w != entry && !w.Closing).ToArray())
            {
                try { await other.Engine.RecycleTabsHostedBy(handle); }
                catch (Exception ex) { Trace.WriteLine($"Tab recycle after window close failed: {ex.Message}"); }
            }
            entry.Engine.Shutdown();
            _ = window.Dispatcher.BeginInvoke(window.Close);
        }
    }
}
