using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly TabHibernationManager _hibernationManager;
    private readonly NavigationPolicyEngine _navigationPolicyEngine;
    private readonly ThemeManager _themeManager;
    private readonly Dictionary<Guid, dynamic> _webViews = new();
    private readonly WebViewFactory _webViewFactory;
    private readonly WebViewIpcBridge _ipcBridge;

    private readonly LinkedList<(string url, string title)> _closedTabs = new();

    private readonly SemaphoreSlim _activationGate = new(1, 1); // Serialize WebView2 creation
    private CancellationTokenSource? _activationCts;
    private const int MaxClosedTabs = 50;
    private bool _disposed;

    // Per-session random token embedded in every internal page. IPC messages that
    // don't include this token are rejected - this replaces the origin-based check
    // which broke when internal pages ran on about:blank.
    private readonly string _ipcToken = Guid.NewGuid().ToString("N");

    /// <summary>The per-session token embedded in internal pages. Used to frame host-to-page messages.</summary>
    public string IpcToken => _ipcToken;



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

    /// <summary>Fires when a tab is closed. ReaderService uses this to drop its per-tab session.</summary>
    public event Action<Guid>? TabClosed;

    /// <summary>Fires when the active tab changes.</summary>
    public event Action<BrowserTab?>? ActiveTabChanged;

    /// <summary>Fires when Alt plus click requests a link preview. On demand, no background timer.</summary>
    public event Action<BrowserTab, string, System.Windows.Rect, StrideBrowser.Models.LinkPreview.LinkPreviewTrigger>? LinkPreviewRequested;

    public CoreWebView2Environment? WebViewEnvironment => _webViewFactory.Environment;

    // Reader mode single-WebView guard. MainWindow sets this true around its own NavigateToString and Navigate calls
    // so the NavigationStarting handler does not treat them as an implicit exit.
    public bool IsProgrammaticReaderNavigation { get; set; }

    // Injected after construction via Composition to avoid a constructor cycle. Returns true when the tab is in reader.
    public Func<Guid, bool>? IsReaderActive { get; set; }

    // Set by Composition to allow TabEngine to exit reader on link navigation without depending on the service at ctor time.
    public Func<Guid, Task>? ExitReaderAsync { get; set; }

    private readonly Services.CustomDownloadManager _customDownloadManager;
    private readonly HashSet<string> _activeNativeDownloads = new();

    /// <summary>Initializes a new instance of the TabEngine.</summary>
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
        _hibernationManager = deps.HibernationManager;
        _navigationPolicyEngine = deps.NavigationPolicyEngine;
        _themeManager = deps.ThemeManager;
        _dispatcher = Application.Current.Dispatcher;

        _webViewFactory = new WebViewFactory(_settings, _themeManager, _pages, _extensionManager, _ipcToken);
        _ipcBridge = new WebViewIpcBridge(
            _dispatcher,
            _settings,
            _downloadStore,
            _ipcToken,
            _activeNativeDownloads,
            isTabAlive: id => _webViews.ContainsKey(id),
            getWebView: id => _webViews.TryGetValue(id, out var wv) ? wv : null,
            createTab: url => CreateTab(url),
            activateAsync: ActivateAsync,
            closeTab: CloseTab,
            downloadSuppressor: deps.DownloadSuppressor);
        _ipcBridge.WebMessageReceived += msg => WebMessageReceived?.Invoke(msg);
        _ipcBridge.FullScreenChanged += fs => FullScreenChanged?.Invoke(fs);
        _ipcBridge.TabStateChanged += tab => TabStateChanged?.Invoke(tab);
        _ipcBridge.LinkPreviewRequested += (tab, url, rect, trigger) => LinkPreviewRequested?.Invoke(tab, url, rect, trigger);

        _hibernationManager.Attach(
            getTabs: () => Tabs,
            getWebViews: () => _webViews.ToDictionary(kvp => kvp.Key, kvp => (dynamic)kvp.Value),
            teardownWebView: id => TeardownWebView(id),
            maxLiveWebViews: 10
        );
        _hibernationManager.SetPreviewOriginCheck(id => _previewOriginTabId == id);

        foreach (var item in _downloadStore.Items)
        {
            // No-op: WebView2 handles download resume natively;
            // no need to attach custom resume handlers.
        }

        _downloadStore.Items.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                // No-op: no custom resume handler needed.
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
        await _webViewFactory.InitializeAsync();
        _webViewFactory.BrowserProcessExited += () => _dispatcher.Invoke(HandleBrowserProcessDeath);
    }

    // ──── Tab Lifecycle ────

    public BrowserTab CreateTab(string? url = null, bool blockDuplicates = true)
    {
        var resolvedUrl = url ?? InternalUrls.NewTab;

        // Block duplicate tabs - switch to existing tab if URL matches.
        // Restore and duplicate paths pass blockDuplicates: false so they always
        // create a fresh tab even when the URL is already open.
        if (blockDuplicates && _settings.BlockDuplicateTabs && !InternalUrls.IsInternal(resolvedUrl))
        {
            var existing = Tabs.FirstOrDefault(t =>
                string.Equals(t.Url, resolvedUrl, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                SwitchTo(existing);
                return existing;
            }
        }

        var title = resolvedUrl switch
        {
            InternalUrls.NewTab => "New Tab",
            InternalUrls.Settings => "Settings",
            InternalUrls.OneTab => "OneTab",
            InternalUrls.History => "History",
            InternalUrls.Downloads => "Downloads",
            InternalUrls.Onboarding => "Welcome to Stride",
            InternalUrls.ReleaseNotes => "What's New in Stride",
            InternalUrls.Focus => "Focus Locked",
            _ => "New Tab"
        };

        var tab = new BrowserTab { Url = resolvedUrl, Title = title };
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
        TabClosed?.Invoke(tab.Id);

        if (tab == ActiveTab && Tabs.Count > 0)
            SwitchTo(Tabs[Math.Min(index, Tabs.Count - 1)]);
    }

    public BrowserTab? RestoreClosedTab()
    {
        if (_closedTabs.Count == 0) return null;
        var last = _closedTabs.Last!.Value;
        _closedTabs.RemoveLast();
        var tab = CreateTab(last.url, blockDuplicates: false);
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
        ActiveTabChanged?.Invoke(tab);
    }

    /// <summary>Activates a tab's WebView2 - creates one if needed, shows it, hides others.</summary>
    public async Task ActivateAsync(BrowserTab tab)
    {
        if (_webViewFactory.Environment is null) return;

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

            tab.IsSleeping = false;
            ShowOnlyActiveWebView(tab);
            tab.LastActiveTime = DateTime.UtcNow;
            _hibernationManager.SuspendBackgroundTabs(tab);
            return;
        }

        // WebView needs creation - serialize to prevent overwhelming the runtime
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

        _hibernationManager.SuspendBackgroundTabs(tab);
        _hibernationManager.EvictExcessWebViews(tab);
    }

    /// <summary>Navigates the active tab to a URL. Internal stride:// pages are served by the WebResourceRequested handler.</summary>
    public void Navigate(BrowserTab tab, string url)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;

        tab.Url = url;

        if (url == InternalUrls.NewTab || string.IsNullOrEmpty(url))
        {
            NavigateToNewTab(tab);
            return;
        }
        if (url == InternalUrls.Settings)
        {
            NavigateToSettings(tab, _settings);
            return;
        }
        if (url == InternalUrls.OneTab)
        {
            NavigateToOneTab(tab, _oneTabStore.Load());
            return;
        }
        if (url == InternalUrls.History)
        {
            NavigateToHistory(tab, _historyStore.Load());
            return;
        }
        if (url == InternalUrls.Downloads)
        {
            NavigateToDownloads(tab);
            return;
        }
        if (url == InternalUrls.Onboarding)
        {
            NavigateToOnboarding(tab);
            return;
        }
        if (url == InternalUrls.ReleaseNotes)
        {
            NavigateToReleaseNotes(tab, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.2.0");
            return;
        }
        if (url == InternalUrls.Focus)
        {
            NavigateToFocus(tab);
            return;
        }

        try { wv.CoreWebView2.Navigate(url); }
        catch (ArgumentException) { /* Invalid URL */ }
    }

    public void NavigateToNewTab(BrowserTab tab)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { wv.CoreWebView2.NavigateToString(_pages.NewTabPage(_settings.NewTabShortcuts, _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken, _settings.DefaultZoom)); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
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

    public void NavigateToOnboarding(BrowserTab tab)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { wv.CoreWebView2.NavigateToString(_pages.OnboardingPage(_settings, _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken)); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToReleaseNotes(BrowserTab tab, string version)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try
        {
            var accentRgb = InternalPages.HexToRgb(_settings.AccentColor);
            var releases = ReleaseNotesProvider.GetAllReleases();
            var html = _pages.ReleaseNotesPage(version, releases, _settings.AccentColor, accentRgb, _ipcToken);
            wv.CoreWebView2.NavigateToString(html);
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
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
        _ipcBridge.PostMessageToActiveTab(ActiveTab?.Id, message);
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
            var bgColor = isInternal ? System.Drawing.Color.Transparent : (enabled ? WebViewFactory.DarkBackground : System.Drawing.Color.White);
            
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

    public CoreWebView2? GetCoreForTab(Guid tabId)
    {
        if (_webViews.TryGetValue(tabId, out var wv))
            return wv.CoreWebView2;
        return null;
    }

    public Task<string> ExecuteScriptAsync(Guid tabId, string script)
    {
        if (_webViews.TryGetValue(tabId, out var wv) && wv.CoreWebView2 is not null)
        {
            return wv.CoreWebView2.ExecuteScriptAsync(script);
        }

        return Task.FromResult(string.Empty);
    }

    public string? GetTabUrl(Guid tabId) => Tabs.FirstOrDefault(t => t.Id == tabId)?.Url;

    public void ApplyAppThemeToWebViews()
    {
        // PreferredColorScheme is a profile-level setting, so setting it on one active WebView updates it for all
        var firstActive = _webViews.Values.FirstOrDefault(wv => wv.CoreWebView2 is not null);
        if (firstActive?.CoreWebView2 is not null)
        {
            _dispatcher.Invoke(() => firstActive.CoreWebView2.Profile.PreferredColorScheme = _themeManager.IsCurrentlyDark() 
                ? CoreWebView2PreferredColorScheme.Dark 
                : CoreWebView2PreferredColorScheme.Light);
        }

        // Also update background colors for external tabs if they are transparent/white
        foreach (var (id, wv) in _webViews)
        {
            var url = Tabs.FirstOrDefault(t => t.Id == id)?.Url ?? "";
            bool isInternal = url.StartsWith("internal://") || string.IsNullOrEmpty(url);
            if (wv is Microsoft.Web.WebView2.Wpf.WebView2 std2) std2.DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? WebViewFactory.DarkBackground : System.Drawing.Color.White);
            else if (wv is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl comp2) comp2.DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? WebViewFactory.DarkBackground : System.Drawing.Color.White);
        }
    }

    // ──── WebView2 Creation ────

    /// <summary>Creates a WebView2 control for a tab and wires its events.</summary>
    private async Task CreateWebViewForTab(BrowserTab tab)
    {
        var isInternal = tab.Url?.StartsWith("internal://") ?? true;

        dynamic wv = _webViewFactory.CreateWebViewControl(isInternal);

        _webViewHost!.Children.Add((FrameworkElement)wv);
        try
        {
            await wv.EnsureCoreWebView2Async(_webViewFactory.Environment);

            if (_settings.AdBlockEnabled)
                AdBlockFilter.Apply(wv.CoreWebView2);

            WireInlinedContextMenu(wv.CoreWebView2);
            _webViewFactory.ConfigureCoreWebView(wv.CoreWebView2);
        }
        catch
        {
            _webViewHost!.Children.Remove((FrameworkElement)wv);
            try { wv.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            throw;
        }

        WireNavigationEvents(wv, tab);
        WireTitleAndSourceEvents(wv, tab);
        _ipcBridge.Wire(wv, tab);
        WireContextMenuEvents(wv, tab);
        HandleProcessFailure(wv, tab);

        // Network-level ad blocking - block known ad URLs before they load
        // Note: We now rely entirely on the native uBlock Origin extension (ExtensionManager)
        // rather than the rudimentary AdBlockFilter, as it provides far superior blocking without breaking sites.

        _webViewFactory.TryInitializeExtensions(wv);

        await _contentScriptInjector.InjectAsync(wv.CoreWebView2, _settings, _ipcToken);

        _webViews[tab.Id] = wv;
        tab.IsHibernated = false;
        tab.IsSleeping = false;

        _webViewFactory.NavigateInitialUrl(wv, tab);
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

    private void WireNavigationEvents(dynamic wv, BrowserTab tab)
    {
        CoreWebView2 core = wv.CoreWebView2;
        core.NavigationStarting += (_, e) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;

            // Reader mode single-WebView implicit exit. If the tab is in reader and this navigation
            // was not our own Enter or Exit NavigateToString, treat it as a link click inside reader.
            if (IsReaderActive?.Invoke(tab.Id) == true && !IsProgrammaticReaderNavigation)
            {
                try { core.Settings.IsScriptEnabled = true; } catch (Exception ex) { Trace.WriteLine($"Reader implicit exit script enable failed: {ex.Message}"); }
                if (ExitReaderAsync != null) _ = ExitReaderAsync(tab.Id);
                // Do not cancel, let the navigation proceed as a normal page load.
            }

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

            if (_navigationPolicyEngine.EvaluateAndHandle(
                e, wv, tab, _dispatcher,
                closeTab: (Action<BrowserTab>)CloseTab,
                navigateToFocus: (Action<BrowserTab>)NavigateToFocus,
                isFocusLocked: _settings.FocusLocked,
                forceHttps: _settings.ForceHttps))
            {
                return;
            }

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

    private void ApplyNavigationBackground(dynamic wv, string? url)
    {
        if (url is null) return;
        var isInternal = url.StartsWith("internal://") || url.StartsWith("https://local.assets/") || url.StartsWith("https://user.assets/");
        if (wv is Microsoft.Web.WebView2.Wpf.WebView2 std3) std3.DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? WebViewFactory.DarkBackground : System.Drawing.Color.White);
        else if (wv is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl comp3) comp3.DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? WebViewFactory.DarkBackground : System.Drawing.Color.White);
    }

    private void WireTitleAndSourceEvents(dynamic wv, BrowserTab tab)
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

    private void WireContextMenuEvents(dynamic wv, BrowserTab tab)
    {
        // wv is either WebView2 or WebView2CompositionControl depending on
        // UseFloatingCommandBar. Both expose CoreWebView2, so no cast is needed.
        CoreWebView2 core = wv.CoreWebView2;
        Handlers.TabContextMenuHandler.Wire(
            core,
            _dispatcher,
            _settings,
            url => { _ = _dispatcher.InvokeAsync(async () => { try { var newTab = CreateTab(url); await ActivateAsync(newTab); } catch (Exception ex) { Trace.WriteLine(ex); } }); },
            () => { _settings.ForceDarkMode = !_settings.ForceDarkMode; }
        );
    }



    /// <summary>Wires process failure handling for a tab WebView and marks crashed tabs hibernated.</summary>
    private void HandleProcessFailure(dynamic wv, BrowserTab tab)
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
                    tab.IsSleeping = false;
                    tab.IsLoading = false;
                    if (!tab.Title.StartsWith("[Crashed] ", StringComparison.Ordinal))
                        tab.Title = $"[Crashed] {tab.Title}";
                }
                catch (Exception ex) { Trace.WriteLine($"ProcessFailed handler error: {ex.Message}"); }
            });
        };
    }

    /// <summary>Handles browser process crash by hibernating all tabs and clearing sleeping state.</summary>
    private void HandleBrowserProcessDeath()
    {
        if (_webViews.Count == 0) return;

        foreach (var t in Tabs)
        {
            t.IsHibernated = true;
            t.IsSleeping = false;
            t.IsLoading = false;
        }
        foreach (var (_, wv) in _webViews)
        {
            _webViewHost!.Children.Remove((FrameworkElement)wv);
            try { wv.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }
        _webViews.Clear();
    }

    private void UpdateTabFromWebView(dynamic wv, BrowserTab tab)
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

    // ──── Link Preview - on demand, sleep only, never hibernate ────

    private Guid? _previewOriginTabId;
    private int _previewGeneration;

    /// <summary>Suspends the origin tab during peek. On demand. Low memory, TrySuspend, mark sleeping, keep WebView alive.</summary>
    public async Task SuspendForPreviewAsync(Guid tabId)
    {
        if (!_webViews.TryGetValue(tabId, out var wv)) return;
        if (wv.CoreWebView2 is null) return;
        var tab = Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is null) return;

        var generation = ++_previewGeneration;
        _previewOriginTabId = tabId;

        try
        {
            await wv.CoreWebView2.ExecuteScriptAsync(
                "(function(){ var s = document.getElementById('__strideDimStyle'); if (!s) { s = document.createElement('style'); s.id = '__strideDimStyle'; s.innerHTML = 'html { filter: brightness(0.25) !important; transition: filter 0.15s ease !important; }'; (document.head || document.documentElement).appendChild(s); } })();");
        }
        catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }

        if (generation != _previewGeneration || _previewOriginTabId != tabId)
        {
            try { _ = wv.CoreWebView2.ExecuteScriptAsync("(function(){ var s = document.getElementById('__strideDimStyle'); if (s) s.remove(); document.documentElement.style.filter = ''; })();"); } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            return;
        }

        if (!_settings.TabSleepEnabled) return;
        bool suspended = false;
        try
        {
            wv.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            var ok = await wv.CoreWebView2.TrySuspendAsync();
            if (ok)
            {
                suspended = true;
                tab.IsSleeping = true;
            }
        }
        catch (Exception ex) { Trace.WriteLine($"SuspendForPreview failed for {tabId}: {ex.Message}"); }

        if (generation != _previewGeneration || _previewOriginTabId != tabId)
        {
            if (suspended)
            {
                try { wv.CoreWebView2.Resume(); } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                tab.IsSleeping = false;
            }
            try { wv.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal; } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            try { _ = wv.CoreWebView2.ExecuteScriptAsync("(function(){ var s = document.getElementById('__strideDimStyle'); if (s) s.remove(); document.documentElement.style.filter = ''; })();"); } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }
    }

    /// <summary>Resumes the origin tab after peek is dismissed. Keeps it hibernated false.</summary>
    public void ResumeFromPreview(Guid tabId)
    {
        _previewGeneration++;
        if (_previewOriginTabId == tabId) _previewOriginTabId = null;
        if (!_webViews.TryGetValue(tabId, out var wv)) return;
        if (wv.CoreWebView2 is null) return;
        var tab = Tabs.FirstOrDefault(t => t.Id == tabId);

        try
        {
            wv.CoreWebView2.Resume();
        }
        catch (Exception ex) { Trace.WriteLine($"Resume failed for {tabId}: {ex.Message}"); }

        try
        {
            wv.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
        }
        catch (Exception ex) { Trace.WriteLine($"Set memory level failed for {tabId}: {ex.Message}"); }

        if (tab is not null) tab.IsSleeping = false;

        try
        {
            _ = wv.CoreWebView2.ExecuteScriptAsync(
                "(function(){ var s = document.getElementById('__strideDimStyle'); if (s) s.remove(); document.documentElement.style.filter = ''; })();");
        }
        catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    internal bool IsPreviewOrigin(Guid tabId) => _previewOriginTabId == tabId;

    /// <summary>Gets the FrameworkElement for the given tab's WebView, if it exists.</summary>
    public FrameworkElement? GetWebViewElement(Guid tabId)
    {
        if (_webViews.TryGetValue(tabId, out var wv))
            return wv as FrameworkElement;
        return null;
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
        _hibernationManager.Detach();
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


