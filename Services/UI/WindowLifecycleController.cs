using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.ViewModels;

namespace StrideBrowser.Services.UI;

/// <summary>
/// Coordinates window lifecycle events: startup initialization, session restoration,
/// command line processing, single instance messaging, fullscreen transitions, and shutdown.
/// </summary>
public sealed class WindowLifecycleController
{
    private readonly Window _window;
    private readonly FrameworkElement _toolbar;
    private readonly TabEngine _engine;
    private readonly BrowserViewModel _vm;
    private readonly ISettingsStore _settingsStore;
    private readonly ISessionStore _sessionStore;
    private readonly Action _onPostInit;
    private readonly Action _bringToFront;

    private bool _isShuttingDown;
    private bool _isFullscreen;
    private WindowState _preFullscreenState;

    public bool IsFullscreen => _isFullscreen;

    public WindowLifecycleController(
        Window window,
        FrameworkElement toolbar,
        TabEngine engine,
        BrowserViewModel vm,
        ISettingsStore settingsStore,
        ISessionStore sessionStore,
        Action onPostInit,
        Action bringToFront)
    {
        _window = window;
        _toolbar = toolbar;
        _engine = engine;
        _vm = vm;
        _settingsStore = settingsStore;
        _sessionStore = sessionStore;
        _onPostInit = onPostInit;
        _bringToFront = bringToFront;
    }

    public async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _engine.InitializeAsync();

            _onPostInit();

            await RestoreSessionOrCreateTab();

            await HandleCommandLineUrls();

            SingleInstanceManager.InstanceMessageReceived += OnInstanceMessageReceived;

            if (!DefaultBrowserRegistrar.IsRegistered())
                DefaultBrowserRegistrar.Register();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"OnWindowLoaded failed: {ex}");
        }
    }

    public void OnInstanceMessageReceived(string[] args)
    {
        _window.Dispatcher.InvokeAsync(async () =>
        {
            foreach (var arg in args)
            {
                if (arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var tab = _engine.CreateTab(arg);
                    _engine.SwitchTo(tab);
                    await _engine.ActivateAsync(tab);
                    break;
                }
            }

            _bringToFront();
        });
    }

    public async Task RestoreSessionOrCreateTab()
    {
        var restored = false;
        if (_vm.Settings.RestoreSessionOnStartup)
        {
            var session = _sessionStore.Load();
            if (session.Count > 0)
            {
                foreach (var entry in session)
                {
                    var tab = _engine.CreateTab(entry.Url, blockDuplicates: false);
                    tab.Title = entry.Title;
                    tab.IsPinned = entry.IsPinned;
                    _ = _engine.LoadFaviconAsync(tab);
                }
                _engine.SwitchTo(_engine.Tabs[0]);
                await _engine.ActivateAsync(_engine.Tabs[0]);
                restored = true;
            }
        }

        if (!_vm.Settings.HasCompletedOnboarding)
        {
            var tab = _engine.CreateTab(InternalUrls.Onboarding);
            tab.Title = "Welcome to Stride";
            _engine.SwitchTo(tab);
            await _engine.ActivateAsync(tab);
            _engine.NavigateToOnboarding(tab);
            _vm.Settings.HasCompletedOnboarding = true;
            _settingsStore.Save(_vm.Settings);
            if (!restored) return;
        }

        if (!restored)
        {
            var newTab = _engine.CreateTab();
            _engine.SwitchTo(newTab);
            await _engine.ActivateAsync(newTab);
        }
    }

    public async Task HandleCommandLineUrls()
    {
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args.Skip(1))
        {
            if (arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var tab = _engine.CreateTab(arg);
                _engine.SwitchTo(tab);
                await _engine.ActivateAsync(tab);
                break;
            }
        }
    }

    public void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            _preFullscreenState = _window.WindowState;
            _toolbar.Visibility = Visibility.Collapsed;
            _window.WindowState = WindowState.Maximized;
        }
        else
        {
            _toolbar.Visibility = Visibility.Visible;
            _window.WindowState = _preFullscreenState;
        }
    }

    public async void OnClosing(CancelEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        e.Cancel = true;
        _isShuttingDown = true;

        try
        {
            if (_vm.Settings.RestoreSessionOnStartup)
            {
                var tabs = _engine.Tabs
                    .Where(t => !InternalUrls.IsInternal(t.Url))
                    .Select(t => (t.Url, t.Title, t.IsPinned));
                _sessionStore.Save(tabs);
            }

            if (_vm.Settings.ClearDataOnExit)
            {
                try
                {
                    var profile = _engine.GetCoreWebView2()?.Profile;
                    if (profile is not null)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await profile.ClearBrowsingDataAsync().WaitAsync(cts.Token);
                    }
                }
                catch { /* Timeout or disposal — best-effort cleanup */ }
            }

            _settingsStore.Save(_vm.Settings);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"OnClosing save failed: {ex}");
        }
        finally
        {
            try { _engine.Shutdown(); }
            catch (Exception ex) { Trace.WriteLine($"Engine shutdown failed: {ex}"); }

            _ = Application.Current.Dispatcher.InvokeAsync(() => _window.Close(), DispatcherPriority.Normal);
        }
    }
}
