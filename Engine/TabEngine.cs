using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.Helpers;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine;

/// <summary>
/// Manages the full tab lifecycle: creation, switching, hibernation, and disposal.
/// Owns all WebView2 instances. The single deepest module in the browser.
/// </summary>
public sealed class TabEngine : IDisposable
{
    private Panel? _webViewHost;
    private readonly ExtensionManager _extensionManager;
    private readonly YouTubeUnhook _youtubeUnhook;
    private readonly BrowserSettings _settings;
    private readonly FaviconLoader _faviconLoader;
    private readonly InternalPages _pages;
    private readonly IHistoryStore _historyStore;
    private readonly IOneTabStore _oneTabStore;
    private readonly IDownloadStore _downloadStore;
    private readonly FocusBlocklistService _focusBlocklistService;
    private readonly ContentScriptInjector _contentScriptInjector;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _hibernationTimer;
    private readonly Dictionary<Guid, IWebView2> _webViews = new();

    private readonly LinkedList<(string url, string title)> _closedTabs = new();

    private CoreWebView2Environment? _environment;
    private bool _extensionsLoaded;
    private readonly SemaphoreSlim _activationGate = new(1, 1); // Serialize WebView2 creation
    private CancellationTokenSource? _activationCts;
    private const int MaxClosedTabs = 50;
    private static readonly System.Drawing.Color DarkBackground = System.Drawing.Color.FromArgb(255, 24, 24, 30);
    private bool _disposed;

    // Per-session random token embedded in every internal page. IPC messages that
    // don't include this token are rejected — this replaces the origin-based check
    // which broke when internal pages ran on about:blank.
    private readonly string _ipcToken = Guid.NewGuid().ToString("N");

    /// <summary>Max number of live (non-hibernated) WebView2 instances. Beyond this, LRU tabs get hibernated.</summary>
    private const int MaxLiveWebViews = 10;

    /// <summary>Base hibernation interval in minutes — scales down with tab count.</summary>
    private const int BaseHibernateMinutes = 5;

    public ObservableCollection<BrowserTab> Tabs { get; } = [];
    public BrowserTab? ActiveTab { get; private set; }

    /// <summary>Fires when a tab's URL or title changes (for ViewModel to update address bar).</summary>
    public event Action<BrowserTab>? TabStateChanged;

    /// <summary>Fires when navigation starts/completes (for loading indicator).</summary>
    public event Action<BrowserTab, bool>? LoadingStateChanged;

    /// <summary>Fires when a web message is received from an internal page.</summary>
    public event Action<string>? WebMessageReceived;

    /// <summary>Fires when a tab is created (for sidebar flash peek).</summary>
    public event Action<BrowserTab>? TabCreated;

    /// <summary>Fires when WebView2 initialization fails.</summary>
    public event Action<Exception>? InitializationFailed;

    private readonly Services.CustomDownloadManager _customDownloadManager;
    private readonly HashSet<string> _activeNativeDownloads = new();

    public TabEngine(EngineDependencies deps)
    {
        _extensionManager = deps.ExtensionManager;
        _youtubeUnhook = deps.YouTubeUnhook;
        _settings = deps.Settings;
        _faviconLoader = deps.FaviconLoader;
        _pages = deps.Pages;
        _historyStore = deps.HistoryStore;
        _oneTabStore = deps.OneTabStore;
        _downloadStore = deps.DownloadStore;
        _focusBlocklistService = deps.FocusBlocklistService;
        _contentScriptInjector = deps.ContentScriptInjector;
        _customDownloadManager = deps.CustomDownloadManager;
        _dispatcher = Application.Current.Dispatcher;

        // Check every 60s whether any tabs have exceeded the hibernation timeout
        _hibernationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _hibernationTimer.Tick += (_, _) => HibernateInactiveTabs();
        _hibernationTimer.Start();

        foreach (var item in _downloadStore.Items)
        {
            AttachCustomResumeHandler(item);
        }

        _downloadStore.Items.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (Models.DownloadItem item in e.NewItems)
                {
                    AttachCustomResumeHandler(item);
                }
            }
        };
    }

    private void AttachCustomResumeHandler(Models.DownloadItem item)
    {
        item.PropertyChanged += async (s, args) =>
        {
            if (args.PropertyName == nameof(Models.DownloadItem.State) && item.State == Models.DownloadState.InProgress)
            {
                // If it's in the active native set, do nothing. Native handles it.
                if (_activeNativeDownloads.Contains(item.Id)) return;

                // Try to get a cookie manager from any active WebView2 instance
                var anyWv = _webViews.Values.FirstOrDefault(w => w.CoreWebView2 != null);
                if (anyWv?.CoreWebView2 != null)
                {
                    await _customDownloadManager.ResumeDownloadAsync(item, anyWv.CoreWebView2.CookieManager);
                }
            }
        };
    }

    public void AttachHost(Panel webViewHost)
    {
        _webViewHost = webViewHost;
    }

    /// <summary>Must be called once at startup to create the WebView2 environment.</summary>
    public async Task InitializeAsync()
    {
        var dataDir = Helpers.AppPaths.WebView2Dir;

        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true,
            // Fluent overlay scrollbar: overlays content without pushing layout, works in Shadow DOM
            ScrollBarStyle = CoreWebView2ScrollbarStyle.FluentOverlay,
            AdditionalBrowserArguments = BuildBrowserArguments(_settings.SmartScreenEnabled, _settings.ForceDarkMode) + 
                (!_settings.HardwareAccelerationEnabled ? " --disable-gpu" : ""),
        };
        _environment = await CoreWebView2Environment.CreateAsync(null, dataDir, options);
        
        if (_settings.AdBlockEnabled)
        {
            // Initializes the background adblock list (does not block startup, finishes in background if downloading)
            _ = AdBlockFilter.InitializeAsync();
        }

        // Handle browser process exit at the environment level
        _environment.BrowserProcessExited += (_, e) =>
        {
            Trace.WriteLine($"WebView2 browser process exited: Kind={e.BrowserProcessExitKind}");
            _dispatcher.Invoke(HandleBrowserProcessDeath);
        };
    }

    /// <summary>
    /// Builds the Chromium command-line flags. Notably:
    /// - The renderer process-limit flag is gone, restoring Chromium's default Site Isolation
    ///   (each site in its own process) rather than capping at a shared pool.
    /// - SmartScreen is ON by default. Disabling it via setting appends the disable flag.
    ///   Note: this flag is read at environment creation, so toggling requires a restart.
    /// - ForceDarkMode uses Chromium's native WebContentsForceDark engine.
    ///   Also read at environment creation — toggling requires a restart.
    /// </summary>
    private static string BuildBrowserArguments(bool smartScreenEnabled, bool forceDarkMode = false)
    {
        var args =
            "--app-user-model-id=Stride " +
            "--disable-background-networking " +
            "--disable-breakpad " +
            "--disable-component-update " +
            "--disable-default-apps " +
            "--disable-domain-reliability " +
            "--disable-sync " +
            "--metrics-recording-only " +
            "--process-per-site " + // Groups pages from the same site into the same process
            "--no-first-run";

        if (forceDarkMode)
            args += " --enable-features=WebContentsForceDark";


        if (!smartScreenEnabled)
            args += " --disable-features=msSmartScreenProtection";

        return args;
    }

    // ──── Tab Lifecycle ────

    public BrowserTab CreateTab(string? url = null)
    {
        var resolvedUrl = url ?? InternalUrls.NewTab;

        // Block duplicate tabs — switch to existing tab if URL matches
        if (_settings.BlockDuplicateTabs && !InternalUrls.IsInternal(resolvedUrl))
        {
            var existing = Tabs.FirstOrDefault(t =>
                string.Equals(t.Url, resolvedUrl, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                SwitchTo(existing);
                return existing;
            }
        }

        var tab = new BrowserTab { Url = resolvedUrl, Title = "New Tab" };
        Tabs.Add(tab);
        SwitchTo(tab);
        TabCreated?.Invoke(tab);
        return tab;
    }

    public void CloseTab(BrowserTab tab)
    {
        if (Tabs.Count <= 1) return;

        var index = Tabs.IndexOf(tab);
        if (index < 0) return; // Tab already removed

        _closedTabs.AddLast((tab.Url, tab.Title));
        if (_closedTabs.Count > MaxClosedTabs)
            _closedTabs.RemoveFirst();

        Tabs.Remove(tab);
        DisposeWebView(tab);

        if (tab == ActiveTab && Tabs.Count > 0)
            SwitchTo(Tabs[Math.Min(index, Tabs.Count - 1)]);
    }

    public BrowserTab? RestoreClosedTab()
    {
        if (_closedTabs.Count == 0) return null;
        var last = _closedTabs.Last!.Value;
        _closedTabs.RemoveLast();
        var tab = CreateTab(last.url);
        tab.Title = last.title;
        return tab;
    }

    public List<(string url, string title)> SendAllToOneTab()
    {
        var entries = Tabs
            .Where(t => !InternalUrls.IsInternal(t.Url))
            .Select(t => (t.Url, t.Title))
            .ToList();

        var toRemove = Tabs.Where(t => !InternalUrls.IsInternal(t.Url)).ToList();
        foreach (var tab in toRemove)
        {
            DisposeWebView(tab);
            Tabs.Remove(tab);
        }

        if (Tabs.Count == 0)
        {
            var newTab = CreateTab();
            SwitchTo(newTab);
        }
        else
        {
            SwitchTo(Tabs[0]);
        }

        return entries;
    }

    public void SwitchTo(BrowserTab tab)
    {
        foreach (var t in Tabs) t.IsActive = false;
        tab.IsActive = true;
        ActiveTab = tab;
        TabStateChanged?.Invoke(tab);
    }

    /// <summary>Activates a tab's WebView2 — creates one if needed, shows it, hides others.</summary>
    public async Task ActivateAsync(BrowserTab tab)
    {
        if (_environment is null) return;

        // Cancel any in-flight activation so rapid clicks don't queue up
        _activationCts?.Cancel();
        _activationCts?.Dispose();
        var cts = _activationCts = new CancellationTokenSource();

        // Removed immediate Visibility.Collapsed to prevent a blank screen flash 
        // while the new tab's WebView2 is booting up or waking from hibernation.

        // If the tab already has a live WebView, show it immediately (no semaphore needed)
        if (_webViews.ContainsKey(tab.Id) && !tab.IsHibernated)
        {
            // Resume from suspension (unfreezes JS, restores rendering)
            if (_webViews.TryGetValue(tab.Id, out var existing) && existing.CoreWebView2 is not null)
                existing.CoreWebView2.Resume();

            ShowOnlyActiveWebView(tab);
            tab.LastActiveTime = DateTime.UtcNow;
            SuspendBackgroundTabs(tab);
            return;
        }

        // WebView needs creation — serialize to prevent overwhelming the runtime
        try
        {
            await _activationGate.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) { return; }

        try
        {
            if (cts.IsCancellationRequested) return;

            if (!_webViews.ContainsKey(tab.Id) || tab.IsHibernated)
                await CreateWebViewForTab(tab);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            InitializationFailed?.Invoke(ex);
            Trace.WriteLine($"ActivateAsync failed for tab {tab.Title}: {ex.Message}");
            return;
        }
        finally
        {
            _activationGate.Release();
        }

        if (cts.IsCancellationRequested) return;

        ShowOnlyActiveWebView(tab);
        tab.LastActiveTime = DateTime.UtcNow;

        SuspendBackgroundTabs(tab);
        EvictExcessWebViews(tab);
    }

    /// <summary>Navigates the active tab to a URL. Internal stride:// pages are served by the WebResourceRequested handler.</summary>
    public void Navigate(BrowserTab tab, string url)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;

        try { wv.CoreWebView2.Navigate(url); }
        catch (ArgumentException) { /* Invalid URL */ }
    }

    public void NavigateToSettings(BrowserTab tab, BrowserSettings settings)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { wv.CoreWebView2.NavigateToString(_pages.SettingsPage(settings, _ipcToken)); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToOneTab(BrowserTab tab, List<OneTabGroup> groups)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { wv.CoreWebView2.NavigateToString(_pages.OneTabPage(_oneTabStore.Load(), _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken)); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToHistory(BrowserTab tab, List<Models.HistoryEntry> entries)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { wv.CoreWebView2.NavigateToString(_pages.HistoryPage(_historyStore.Load(), _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken)); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToDownloads(BrowserTab tab)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { wv.CoreWebView2.NavigateToString(_pages.DownloadsPage(_downloadStore.Items.ToList(), _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken)); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToFocus(BrowserTab tab)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { wv.CoreWebView2.NavigateToString(_pages.FocusPage()); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void GoBack()
    {
        GetActiveCore()?.GoBack();
    }

    public void GoForward()
    {
        GetActiveCore()?.GoForward();
    }

    public void Reload()
    {
        GetActiveCore()?.Reload();
    }

    public async Task FindInPageAsync()
    {
        if (ActiveTab is not null && _webViews.TryGetValue(ActiveTab.Id, out var wv) && wv.CoreWebView2 is not null)
        {
            try
            {
                var script = Helpers.ResourceLoader.Load("Resources.Scripts.find-in-page.js");
                await wv.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex) { Trace.WriteLine($"FindInPage failed: {ex.Message}"); }
        }
    }

    public void PostMessageToActiveTab(string message)
    {
        if (ActiveTab is not null && _webViews.TryGetValue(ActiveTab.Id, out var wv) && wv.CoreWebView2 is not null)
        {
            try
            {
                wv.CoreWebView2.PostWebMessageAsString(message);
            }
            catch (Exception ex) { Trace.WriteLine($"PostMessageToActiveTab failed: {ex.Message}"); }
        }
    }

    public void Print()
    {
        if (ActiveTab is not null && _webViews.TryGetValue(ActiveTab.Id, out var wv) && wv.CoreWebView2 is not null)
        {
            try { _ = wv.CoreWebView2.ExecuteScriptAsync("window.print()"); }
            catch { /* Ignore */ }
        }
    }

    public void Zoom(double delta)
    {
        if (ActiveTab is not null && _webViews.TryGetValue(ActiveTab.Id, out var wv))
            wv.ZoomFactor = Math.Clamp(wv.ZoomFactor + delta, 0.25, 5.0);
    }

    public void ResetZoom()
    {
        if (ActiveTab is not null && _webViews.TryGetValue(ActiveTab.Id, out var wv))
            wv.ZoomFactor = _settings.DefaultZoom / 100.0;
    }

    public double GetZoomLevel()
    {
        if (ActiveTab is not null && _webViews.TryGetValue(ActiveTab.Id, out var wv))
            return wv.ZoomFactor;
        return 1.0;
    }

    public void ApplyDarkModeToAll(bool enabled)
    {
        foreach (var (id, wv) in _webViews)
        {
            var isInternal = Tabs.FirstOrDefault(t => t.Id == id)?.Url?.StartsWith("internal://") ?? true;
            
            // For internal pages, use Transparent to let WPF ThemeManager handle the background color
            var bgColor = isInternal ? System.Drawing.Color.Transparent : (enabled ? DarkBackground : System.Drawing.Color.White);
            
            if (wv is Microsoft.Web.WebView2.Wpf.WebView2 std1) std1.DefaultBackgroundColor = bgColor;
            else if (wv is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl comp1) comp1.DefaultBackgroundColor = bgColor;
        }
    }

    public CoreWebView2? GetCoreWebView2()
    {
        return GetActiveCore();
    }

    private CoreWebView2? GetActiveCore()
    {
        if (ActiveTab is not null && _webViews.TryGetValue(ActiveTab.Id, out var wv))
            return wv.CoreWebView2;
        return null;
    }

    public void ExecuteScript(Guid tabId, string script)
    {
        if (_webViews.TryGetValue(tabId, out var wv) && wv.CoreWebView2 is not null)
        {
            _ = wv.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    public void ApplyAppThemeToWebViews()
    {
        // PreferredColorScheme is a profile-level setting, so setting it on one active WebView updates it for all
        var firstActive = _webViews.Values.FirstOrDefault(wv => wv.CoreWebView2 is not null);
        if (firstActive?.CoreWebView2 is not null)
        {
            firstActive.CoreWebView2.Profile.PreferredColorScheme = Services.ThemeManager.IsCurrentlyDark() 
                ? CoreWebView2PreferredColorScheme.Dark 
                : CoreWebView2PreferredColorScheme.Light;
        }

        // Also update background colors for external tabs if they are transparent/white
        foreach (var (id, wv) in _webViews)
        {
            var url = Tabs.FirstOrDefault(t => t.Id == id)?.Url ?? "";
            bool isInternal = url.StartsWith("internal://") || string.IsNullOrEmpty(url);
            if (wv is Microsoft.Web.WebView2.Wpf.WebView2 std2) std2.DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White);
            else if (wv is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl comp2) comp2.DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White);
        }
    }

    // ──── WebView2 Creation ────

    private async Task CreateWebViewForTab(BrowserTab tab)
    {
        var isInternal = tab.Url?.StartsWith("internal://") ?? true;

        IWebView2 wv = CreateWebViewControl(isInternal);

        _webViewHost!.Children.Add((FrameworkElement)wv);
        try
        {
            await wv.EnsureCoreWebView2Async(_environment);

            if (_settings.AdBlockEnabled)
                AdBlockFilter.Apply(wv.CoreWebView2);

            WireInlinedContextMenu(wv.CoreWebView2);
            ConfigureCoreWebView(wv.CoreWebView2);
        }
        catch
        {
            _webViewHost!.Children.Remove((FrameworkElement)wv);
            try { wv.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            throw;
        }

        WireNavigationEvents(wv, tab);
        WireTitleAndSourceEvents(wv, tab);
        WireMessageAndWindowEvents(wv, tab);
        WireContextMenuEvents(wv, tab);
        HandleProcessFailure(wv, tab);

        // Network-level ad blocking — block known ad URLs before they load
        // Note: We now rely entirely on the native uBlock Origin extension (ExtensionManager)
        // rather than the rudimentary AdBlockFilter, as it provides far superior blocking without breaking sites.

        TryInitializeExtensions(wv);

        await _contentScriptInjector.InjectAsync(wv.CoreWebView2, _settings, _ipcToken);

        _webViews[tab.Id] = wv;
        tab.IsHibernated = false;

        NavigateInitialUrl(wv, tab);
    }

    private IWebView2 CreateWebViewControl(bool isInternal)
    {
        if (_settings.UseFloatingCommandBar)
        {
            return new WebView2CompositionControl
            {
                DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White),
                Visibility = Visibility.Hidden
            };
        }

        return new Microsoft.Web.WebView2.Wpf.WebView2
        {
            DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White),
            Visibility = Visibility.Collapsed
        };
    }

    private void WireInlinedContextMenu(CoreWebView2 core)
    {
        core.ContextMenuRequested += (object? sender, CoreWebView2ContextMenuRequestedEventArgs args) =>
        {
            var deferral = args.GetDeferral();
            args.Handled = true;

            _webViewHost!.Dispatcher.InvokeAsync(() =>
            {
                var cm = new ContextMenu();
                cm.PlacementTarget = _webViewHost;

                var back = new MenuItem { Header = "Back", IsEnabled = core.CanGoBack, InputGestureText = "Alt+Left Arrow" };
                back.Click += (s, e) => core.GoBack();
                cm.Items.Add(back);

                var forward = new MenuItem { Header = "Forward", IsEnabled = core.CanGoForward, InputGestureText = "Alt+Right Arrow" };
                forward.Click += (s, e) => core.GoForward();
                cm.Items.Add(forward);

                var reload = new MenuItem { Header = "Reload", InputGestureText = "Ctrl+R" };
                reload.Click += (s, e) => core.Reload();
                cm.Items.Add(reload);

                cm.Items.Add(new Separator { Background = (System.Windows.Media.Brush)Application.Current.Resources["BorderBrush"] });

                var saveAs = new MenuItem { Header = "Save as...", InputGestureText = "Ctrl+S" };
                saveAs.Click += (s, e) => { /* Not supported in this SDK version */ };
                cm.Items.Add(saveAs);

                var print = new MenuItem { Header = "Print...", InputGestureText = "Ctrl+P" };
                print.Click += (s, e) => { try { core.ShowPrintUI(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); } };
                cm.Items.Add(print);

                cm.Items.Add(new Separator { Background = (System.Windows.Media.Brush)Application.Current.Resources["BorderBrush"] });

                var viewSource = new MenuItem { Header = "View page source", InputGestureText = "Ctrl+U" };
                // View source can be a navigation to view-source: URI
                viewSource.Click += (s, e) => { try { core.Navigate("view-source:" + core.Source); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); } };
                cm.Items.Add(viewSource);

                var inspect = new MenuItem { Header = "Inspect", InputGestureText = "Ctrl+Shift+I" };
                inspect.Click += (s, e) => core.OpenDevToolsWindow();
                cm.Items.Add(inspect);

                cm.IsOpen = true;
                deferral.Complete();
            });
        };
    }

    private void ConfigureCoreWebView(CoreWebView2 core)
    {
        core.Profile.PreferredColorScheme = Services.ThemeManager.IsCurrentlyDark()
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;

        core.SetVirtualHostNameToFolderMapping("local.assets", System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Pages"), Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        var userAssetsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stride", "Backgrounds");
        if (!System.IO.Directory.Exists(userAssetsPath)) System.IO.Directory.CreateDirectory(userAssetsPath);
        core.SetVirtualHostNameToFolderMapping("user.assets", userAssetsPath, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

        // Strip native Edge bloat UI
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;

        try { core.Settings.IsSwipeNavigationEnabled = true; } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    private void TryInitializeExtensions(IWebView2 wv)
    {
        if (_extensionsLoaded) return;
        _extensionsLoaded = true;
        _ = Task.Run(async () =>
        {
            try
            {
                // MUST dispatch to UI thread!
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => {
                    // Extensions
                    _ = _extensionManager.InitializeAsync(wv.CoreWebView2, _settings);

                    // Context Menu
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Extension init failed: {ex.Message}");
            }
        });
    }

    private void NavigateInitialUrl(IWebView2 wv, BrowserTab tab)
    {
        // Settings/OneTab/History are navigated by NavigateToSettings/OneTab/History
        // after activation — skip here to avoid a double load.
        var callerManagedUrls = new HashSet<string>
        {
            InternalUrls.Settings, InternalUrls.OneTab, InternalUrls.History, "internal://pending-native"
        };
        if (callerManagedUrls.Contains(tab.Url))
            return;

        // NewTab is served as HTML string so there's no real URL to navigate to.
        if (tab.Url == InternalUrls.NewTab || string.IsNullOrEmpty(tab.Url))
        {
            try { wv.CoreWebView2.NavigateToString(_pages.NewTabPage(_settings.NewTabShortcuts, _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken, _settings.DefaultZoom)); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            return;
        }

        try { wv.CoreWebView2.Navigate(tab.Url); }
        catch (ArgumentException) { try { wv.CoreWebView2.NavigateToString(_pages.NewTabPage(_settings.NewTabShortcuts, _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken, _settings.DefaultZoom)); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); } }
    }




    /// <summary>
    /// Re-injects the YouTube Unhook script into all active YouTube tabs.
    /// Called when unhook settings change for live-reload without page refresh.
    /// Strips all stride-unhook-* classes and re-applies with new config.
    /// </summary>
    public async Task ReInjectUnhookAsync()
    {
        // Remove all unhook classes + reset loaded flag so the script re-runs class toggles
        const string cleanup =
            "document.documentElement.className = " +
            "document.documentElement.className.replace(/\\bstride-unhook-\\w+/g, '').trim(); " +
            "window.__STRIDE_UNHOOK_LOADED = false;";
        var script = _youtubeUnhook.GetScript(_settings);

        foreach (var (_, wv) in _webViews)
        {
            if (wv.CoreWebView2 is null) continue;
            var url = wv.Source?.ToString() ?? "";
            if (!url.Contains("youtube.com")) continue;

            try
            {
                await wv.CoreWebView2.ExecuteScriptAsync(cleanup);
                if (!string.IsNullOrEmpty(script))
                    await wv.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Unhook re-injection failed: {ex.Message}");
            }
        }
    }


    // ──── WebView Event Wiring ────

    private void WireNavigationEvents(IWebView2 wv, BrowserTab tab)
    {
        CoreWebView2 core = wv.CoreWebView2;
        core.NavigationStarting += (_, e) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;

            ApplyNavigationBackground(wv, e.Uri);

            if (_settings.DefaultZoom != 100)
            {
                try 
                { 
                    var targetZoom = _settings.DefaultZoom / 100.0;
                    if (Math.Abs(wv.ZoomFactor - targetZoom) > 0.01)
                    {
                        wv.ZoomFactor = targetZoom; 
                    }
                } 
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            }

            if (TryHandleCustomProtocol(e, wv, tab)) return;

            if (_settings.FocusLocked && TryHandleFocusLock(e, tab)) return;

            if (_settings.ForceHttps && TryUpgradeToHttps(e, wv)) return;

            _dispatcher.InvokeAsync(() =>
            {
                tab.IsLoading = true;
                LoadingStateChanged?.Invoke(tab, true);
            });
        };

        core.NavigationCompleted += (_, e) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;
            _ = _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (!_webViews.ContainsKey(tab.Id)) return;

                    if (!e.IsSuccess &&
                        e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled &&
                        e.WebErrorStatus != CoreWebView2WebErrorStatus.Unknown &&
                        e.WebErrorStatus != CoreWebView2WebErrorStatus.ConnectionAborted)
                    {
                        tab.IsLoading = false;
                        LoadingStateChanged?.Invoke(tab, false);
                        var errorMsg = e.WebErrorStatus switch
                        {
                            CoreWebView2WebErrorStatus.ServerUnreachable => "The server could not be reached. Check your internet connection.",
                            CoreWebView2WebErrorStatus.Timeout => "The connection timed out.",
                            CoreWebView2WebErrorStatus.ConnectionAborted => "The connection was interrupted.",
                            CoreWebView2WebErrorStatus.ConnectionReset => "The connection was reset.",
                            CoreWebView2WebErrorStatus.Disconnected => "You appear to be offline.",
                            CoreWebView2WebErrorStatus.CannotConnect => "Could not connect to the server.",
                            CoreWebView2WebErrorStatus.HostNameNotResolved => "The site's address could not be found.",
                            CoreWebView2WebErrorStatus.CertificateIsInvalid => "The site's security certificate is not trusted.",
                            _ => $"Navigation failed: {e.WebErrorStatus} (Code: {(int)e.WebErrorStatus})"
                        };
                        try { wv.CoreWebView2.NavigateToString(_pages.ErrorPage(tab.Url, errorMsg, _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor))); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                        return;
                    }

                    tab.IsLoading = false;
                    LoadingStateChanged?.Invoke(tab, false);
                    UpdateTabFromWebView(wv, tab);
                }
                catch (Exception ex) { Trace.WriteLine($"NavigationCompleted error: {ex.Message}"); }
            });
        };
    }

    private void ApplyNavigationBackground(IWebView2 wv, string? url)
    {
        if (url is null) return;
        var isInternal = url.StartsWith("internal://") || url.StartsWith("https://local.assets/") || url.StartsWith("https://user.assets/");
        if (wv is Microsoft.Web.WebView2.Wpf.WebView2 std3) std3.DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White);
        else if (wv is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl comp3) comp3.DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White);
    }

    private bool TryHandleCustomProtocol(CoreWebView2NavigationStartingEventArgs e, IWebView2 wv, BrowserTab tab)
    {
        if (e.Uri is not string uriForCustom || !Uri.TryCreate(uriForCustom, UriKind.Absolute, out var parsedCustomUri))
            return false;

        var scheme = parsedCustomUri.Scheme.ToLowerInvariant();
        if (scheme == "http" || scheme == "https" || scheme == "file" || scheme == "data" ||
            scheme == "about" || scheme == "edge" || scheme == "chrome" || scheme == "stride" || scheme == "javascript" ||
            scheme == "extension" || scheme == "chrome-extension" || scheme == "internal")
            return false;

        e.Cancel = true;
        _dispatcher.InvokeAsync(() =>
        {
            // SECURITY: confirm before handing off to an external app — unprompted
            // protocol-handler invocation is a known RCE vector for some installed apps.
            var displayUrl = uriForCustom.Length > 100 ? uriForCustom.Substring(0, 97) + "..." : uriForCustom;
            var dialog = new BaseBrowserDialogWindow
            {
                Owner = System.Windows.Application.Current.MainWindow,
                DialogTitle = "Open External App",
                DialogMessage = $"This page wants to open an external application to handle this link:\n\n{displayUrl}\n\nOnly continue if you trust this site.",
                CancelVisibility = System.Windows.Visibility.Visible,
                OkButtonText = "Open App",
                CancelButtonText = "Cancel"
            };
            dialog.ShowDialog();

            if (!dialog.IsAccepted) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uriForCustom) { UseShellExecute = true });
                if (tab.Url == InternalUrls.NewTab || tab.Url == "about:blank")
                    CloseTab(tab);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Custom protocol launch failed: {ex.Message}"); }
        });
        return true;
    }

    private bool TryHandleFocusLock(CoreWebView2NavigationStartingEventArgs e, BrowserTab tab)
    {
        if (e.Uri is not string uriStrFocus || !Uri.TryCreate(uriStrFocus, UriKind.Absolute, out var parsedFocusUri))
            return false;

        var host = parsedFocusUri.Host;
        if (string.IsNullOrEmpty(host) || !_focusBlocklistService.IsBlocked(host))
            return false;

        e.Cancel = true;
        _dispatcher.InvokeAsync(() =>
        {
            tab.Url = InternalUrls.Focus;
            NavigateToFocus(tab);
        });
        return true;
    }

    private bool TryUpgradeToHttps(CoreWebView2NavigationStartingEventArgs e, IWebView2 wv)
    {
        if (e.Uri is not string uriStr || !uriStr.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return false;

        var isLocalHostOrIp = false;
        if (Uri.TryCreate(uriStr, UriKind.Absolute, out var parsedUri))
        {
            isLocalHostOrIp = parsedUri.IsLoopback ||
                              parsedUri.HostNameType == UriHostNameType.IPv4 ||
                              parsedUri.HostNameType == UriHostNameType.IPv6 ||
                              parsedUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            isLocalHostOrIp = uriStr.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
                              uriStr.StartsWith("http://127.", StringComparison.OrdinalIgnoreCase);
        }

        if (isLocalHostOrIp)
            return false;

        e.Cancel = true;
        var httpsUri = "https://" + uriStr["http://".Length..];
        _dispatcher.InvokeAsync(() => { try { wv.CoreWebView2.Navigate(httpsUri); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); } });
        return true;
    }

    private void WireTitleAndSourceEvents(IWebView2 wv, BrowserTab tab)
    {
        CoreWebView2 core = wv.CoreWebView2;
        core.DocumentTitleChanged += (_, _) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;
            _dispatcher.Invoke(() =>
            {
                if (!_webViews.ContainsKey(tab.Id)) return;
                var source = wv.Source?.ToString() ?? "";
                if (!InternalUrls.IsDataOrBlank(source))
                    tab.Title = wv.CoreWebView2.DocumentTitle;
            });
        };

        core.SourceChanged += (_, _) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;
            _dispatcher.Invoke(() =>
            {
                if (!_webViews.ContainsKey(tab.Id)) return;
                var source = wv.Source?.ToString() ?? "";
                if (!InternalUrls.IsDataOrBlank(source))
                {
                    tab.Url = source;
                    TabStateChanged?.Invoke(tab);
                }
            });
        };

        // Use WebView2's native favicon API (like Chrome/Firefox)
        // instead of relying solely on a 3rd-party service
        core.FaviconChanged += (_, _) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;
            string source = (string)wv.Source?.ToString() ?? "";
            _ = _faviconLoader.HandleFaviconChangedAsync(core, source).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result is not null)
                    _dispatcher.Invoke(() => { tab.Favicon = t.Result; TabStateChanged?.Invoke(tab); });
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        };
    }

    public event Action<bool>? FullScreenChanged;

    private void WireMessageAndWindowEvents(IWebView2 wv, BrowserTab tab)
    {
        CoreWebView2 core = wv.CoreWebView2;
        core.ContainsFullScreenElementChanged += (_, _) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;
            _dispatcher.InvokeAsync(() =>
            {
                FullScreenChanged?.Invoke(core.ContainsFullScreenElement);
            });
        };
        WireWebMessageReceived(core, tab);

        Handlers.TabDownloadHandler.Wire(wv.CoreWebView2, _dispatcher, _downloadStore, _activeNativeDownloads);

        WireNewWindowRequested(core, tab);

        core.WindowCloseRequested += (_, _) =>
        {
            _dispatcher.Invoke(() => CloseTab(tab));
        };

        Handlers.TabDialogHandler.Wire(wv.CoreWebView2, _dispatcher, _settings);
    }

    private void WireWebMessageReceived(CoreWebView2 core, BrowserTab tab)
    {
        core.WebMessageReceived += (_, e) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;

            var msg = e.TryGetWebMessageAsString();
            var src = e.Source ?? "null";

            if (string.IsNullOrEmpty(msg)) return;

            bool isTrustedLocalAsset = false;
            try 
            { 
                if (e.Source.StartsWith("http://local.assets/", StringComparison.OrdinalIgnoreCase) || 
                    e.Source.StartsWith("https://local.assets/", StringComparison.OrdinalIgnoreCase) ||
                    e.Source.StartsWith("https://user.assets/", StringComparison.OrdinalIgnoreCase)) 
                {
                    isTrustedLocalAsset = true; 
                }
            } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }

            if (msg.StartsWith("THEME_COLOR:"))
            {
                var colorStr = msg.Substring("THEME_COLOR:".Length);
                _dispatcher.InvokeAsync(() => {
                    tab.ThemeColor = colorStr;
                    TabStateChanged?.Invoke(tab);
                });
                return;
            }

            if (!isTrustedLocalAsset)
            {
                // SECURITY: Internal pages loaded via NavigateToString (origin = about:blank) embed a per-session secret token
                if (!msg.StartsWith(_ipcToken + ":", StringComparison.Ordinal))
                {
                    return;
                }
                // Strip the token prefix before forwarding the payload
                msg = msg[(_ipcToken.Length + 1)..];
            }

            if (!string.IsNullOrEmpty(msg))
            {
                _dispatcher.Invoke(() => WebMessageReceived?.Invoke(msg));
            }
        };
    }

    private void WireNewWindowRequested(CoreWebView2 core, BrowserTab tab)
    {
        core.NewWindowRequested += (_, e) =>
        {
            // Treat as popup if it requests specific dimensions or isn't explicitly user-initiated (e.g. OAuth flows)
            if (e.WindowFeatures.HasPosition || e.WindowFeatures.HasSize || !e.IsUserInitiated)
            {
                // Let WebView2 handle the popup natively! This is the most reliable way to handle OAuth popups
                // without breaking window.opener or causing the site to think popups are blocked.
                e.Handled = false;
                return;
            }

            // Otherwise, handle it ourselves by opening a new Stride tab
            e.Handled = true;
            var deferral = e.GetDeferral();
            
            _ = _dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // Create tab with a sentinel URL so NavigateInitialUrl does NOT navigate it.
                    // This keeps the CoreWebView2 clean so we can assign it to e.NewWindow.
                    var newTab = CreateTab("internal://pending-native");
                    
                    // ActivateAsync is triggered via TabStateChanged but we await it directly to ensure readiness
                    await ActivateAsync(newTab);

                    if (_webViews.TryGetValue(newTab.Id, out var wv) && wv.CoreWebView2 != null)
                    {
                        // Fix background color (internal:// made it transparent, we want opaque for the popup)
                        if (wv is Microsoft.Web.WebView2.Wpf.WebView2 std4) std4.DefaultBackgroundColor = _settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White;
                        else if (wv is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl comp4) comp4.DefaultBackgroundColor = _settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White;
                        
                        // Let WebView2 do the navigation natively! 
                        // This allows extensions (like uBlock Origin) to intercept the new window request!
                        e.NewWindow = wv.CoreWebView2;
                        newTab.Url = e.Uri; // Update the UI to reflect the requested URL
                    }
                }
                catch (Exception ex) { Trace.WriteLine($"NewWindowRequested error: {ex.Message}"); }
                finally
                {
                    deferral.Complete();
                }
            });
        };
    }

    private void WireContextMenuEvents(IWebView2 wv, BrowserTab tab)
    {
        Handlers.TabContextMenuHandler.Wire(
            wv.CoreWebView2, 
            _dispatcher, 
            _settings, 
            url => { _ = _dispatcher.InvokeAsync(async () => { try { var newTab = CreateTab(url); await ActivateAsync(newTab); } catch (Exception ex) { Trace.WriteLine(ex); } }); },
            () => { _settings.ForceDarkMode = !_settings.ForceDarkMode; }
        );
    }



    private void HandleProcessFailure(IWebView2 wv, BrowserTab tab)
    {
        CoreWebView2 core = wv.CoreWebView2;
        core.ProcessFailed += (_, e) =>
        {
            _dispatcher.Invoke(() =>
            {
                try
                {
                    Trace.WriteLine($"WebView2 process failed: {e.ProcessFailedKind} for tab '{tab.Title}'");

                    if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                    {
                        HandleBrowserProcessDeath();
                        return;
                    }

                    TeardownWebView(tab.Id);
                    tab.IsHibernated = true;
                    tab.IsLoading = false;
                    tab.Title = $"[Crashed] {tab.Title}";
                }
                catch (Exception ex) { Trace.WriteLine($"ProcessFailed handler error: {ex.Message}"); }
            });
        };
    }

    private void HandleBrowserProcessDeath()
    {
        if (_webViews.Count == 0) return;

        foreach (var t in Tabs)
        {
            t.IsHibernated = true;
            t.IsLoading = false;
        }
        foreach (var (_, wv) in _webViews)
        {
            _webViewHost!.Children.Remove((FrameworkElement)wv);
            try { wv.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }
        _webViews.Clear();
    }

    private void UpdateTabFromWebView(IWebView2 wv, BrowserTab tab)
    {
        string source = (string)wv.Source?.ToString() ?? "";
        if (InternalUrls.IsDataOrBlank(source))
        {
            HandleInternalPageTitles(tab);
            return;
        }

        tab.Url = source;
        tab.Title = string.IsNullOrWhiteSpace((string)wv.CoreWebView2.DocumentTitle)
            ? source : (string)wv.CoreWebView2.DocumentTitle;
        _ = _faviconLoader.LoadAsync(source).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && t.Result is not null)
                _dispatcher.Invoke(() => { tab.Favicon = t.Result; TabStateChanged?.Invoke(tab); });
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
        TabStateChanged?.Invoke(tab);

        _historyStore.Add(source, tab.Title);
    }

    private static void HandleInternalPageTitles(BrowserTab tab)
    {
        tab.Title = tab.Url switch
        {
            InternalUrls.NewTab => "New Tab",
            InternalUrls.Settings => "Settings",
            InternalUrls.OneTab => "OneTab",
            InternalUrls.Downloads => "Downloads",
            InternalUrls.Focus => "Focus Locked",
            _ => tab.Title
        };
    }

    // ──── Memory Management ────
    
    public async Task LoadFaviconAsync(BrowserTab tab)
    {
        if (string.IsNullOrWhiteSpace(tab.Url)) return;
        
        var bitmap = await _faviconLoader.LoadAsync(tab.Url);
        if (bitmap is not null)
        {
            _dispatcher.Invoke(() => 
            { 
                tab.Favicon = bitmap; 
                TabStateChanged?.Invoke(tab); 
            });
        }
    }

    private bool IsTabSafeToHibernate(BrowserTab tab)
    {
        if (tab.IsActive || tab.IsHibernated) return false;
        if (tab.IsPinned) return false;
        if (InternalUrls.IsInternal(tab.Url)) return false;

        // Check if tab is playing audio
        if (_webViews.TryGetValue(tab.Id, out var wv))
        {
            try
            {
                if (wv.CoreWebView2 != null && wv.CoreWebView2.IsDocumentPlayingAudio)
                    return false;
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        // If there are ANY active downloads, disable all hibernation to be safe,
        // because destroying a WebView might abort downloads originating from it.
        if (_downloadStore.Items.Any(d => d.State == Models.DownloadState.InProgress))
            return false;

        return true;
    }

    private void SuspendBackgroundTabs(BrowserTab activeTab)
    {
        foreach (var (id, wv) in _webViews)
        {
            if (id == activeTab.Id) continue;
            if (wv.CoreWebView2 is null) continue;

            try
            {
                wv.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                _ = wv.CoreWebView2.TrySuspendAsync();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SuspendBackgroundTabs failed for tab {id}: {ex.Message}");
            }
        }
    }

    private void EvictExcessWebViews(BrowserTab activeTab)
    {
        if (_webViews.Count <= MaxLiveWebViews) return;

        var candidates = Tabs
            .Where(t => t.Id != activeTab.Id && !t.IsHibernated && _webViews.ContainsKey(t.Id))
            .Where(t => IsTabSafeToHibernate(t))
            .OrderBy(t => t.LastActiveTime)
            .ToList();

        var toEvict = _webViews.Count - MaxLiveWebViews;
        foreach (var tab in candidates.Take(toEvict))
        {
            Trace.WriteLine($"Evicting WebView for '{tab.Title}' (last active: {tab.LastActiveTime:HH:mm:ss})");
            HibernateTab(tab);
        }
    }

    // ──── Hibernation ────

    private void HibernateInactiveTabs()
    {
        var hibernateMinutes = Tabs.Count switch
        {
            <= 5 => BaseHibernateMinutes,
            <= 10 => 3,
            <= 15 => 2,
            _ => 1
        };

        var cutoff = DateTime.UtcNow.AddMinutes(-hibernateMinutes);

        foreach (var tab in Tabs.ToList())
        {
            if (!IsTabSafeToHibernate(tab)) continue;
            if (tab.LastActiveTime > cutoff) continue;

            HibernateTab(tab);
        }
    }

    private void HibernateTab(BrowserTab tab)
    {
        if (_webViews.TryGetValue(tab.Id, out var wv))
            tab.Url = wv.Source?.ToString() ?? tab.Url;
        TeardownWebView(tab.Id);
        tab.IsHibernated = true;
    }

    // ──── Visibility ────

    private void ShowOnlyActiveWebView(BrowserTab activeTab)
    {
        foreach (var (id, wv) in _webViews)
        {
            var isActive = id == activeTab.Id;
            
            // Use Hidden for CompositionControl to prevent flicker.
            // But HwndHost (standard WebView2) ignores Hidden and keeps rendering the HWND, so we MUST use Collapsed.
            // Check the actual type of the control, because the setting might have been toggled at runtime before a restart.
            bool isHwndHost = wv is Microsoft.Web.WebView2.Wpf.WebView2;
            ((FrameworkElement)wv).Visibility = isActive 
                ? Visibility.Visible 
                : (isHwndHost ? Visibility.Collapsed : Visibility.Hidden);
            Panel.SetZIndex((FrameworkElement)wv, isActive ? 1 : 0);
            
            if (wv.CoreWebView2 is null) continue;

            // Active tab: normal memory budget. Background tabs: aggressive GC
            wv.CoreWebView2.MemoryUsageTargetLevel = isActive
                ? CoreWebView2MemoryUsageTargetLevel.Normal
                : CoreWebView2MemoryUsageTargetLevel.Low;
        }
    }

    // ──── Disposal ────

    public void DisposeWebView(BrowserTab tab) => TeardownWebView(tab.Id);

    private void TeardownWebView(Guid tabId)
    {
        if (!_webViews.TryGetValue(tabId, out var wv)) return;
        _webViewHost!.Children.Remove((FrameworkElement)wv);
        try { wv.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        _webViews.Remove(tabId);
    }

    public void Shutdown()
    {
        _hibernationTimer.Stop();
        _activationCts?.Cancel();
        _activationCts?.Dispose();
        _webViewHost!.Children.Clear();
        foreach (var (_, wv) in _webViews)
        {
            try { wv.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }
        _webViews.Clear();
        _activationGate.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shutdown();
    }
}


