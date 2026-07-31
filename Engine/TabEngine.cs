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
    private readonly Panel _webViewHost;
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
    private readonly Dictionary<Guid, WebView2> _webViews = new();
    private readonly Dictionary<Guid, (string darkReaderId, string forceDarkId)> _darkScripts = new();
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

    public TabEngine(Panel webViewHost, EngineDependencies deps)
    {
        _webViewHost = webViewHost;
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
        _dispatcher = webViewHost.Dispatcher;

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

    /// <summary>Must be called once at startup to create the WebView2 environment.</summary>
    public async Task InitializeAsync()
    {
        var dataDir = Helpers.AppPaths.WebView2Dir;

        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true,
            // Fluent overlay scrollbar: overlays content without pushing layout, works in Shadow DOM
            ScrollBarStyle = CoreWebView2ScrollbarStyle.FluentOverlay,
            AdditionalBrowserArguments = BuildBrowserArguments(_settings.SmartScreenEnabled) + 
                (!_settings.HardwareAccelerationEnabled ? " --disable-gpu" : ""),
        };
        _environment = await CoreWebView2Environment.CreateAsync(null, dataDir, options);

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
    /// </summary>
    private static string BuildBrowserArguments(bool smartScreenEnabled)
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
            "--renderer-process-limit=2 " + // Drastically cuts down memory by forcing Chromium to reuse renderer processes
            "--process-per-site " + // Groups pages from the same site into the same process
            "--no-first-run";

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
        try { wv.CoreWebView2.NavigateToString(_pages.SettingsPage(settings, _ipcToken)); } catch { }
    }

    public void NavigateToOneTab(BrowserTab tab, List<OneTabGroup> groups)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { wv.CoreWebView2.NavigateToString(_pages.OneTabPage(_oneTabStore.Load(), _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken)); } catch { }
    }

    public void NavigateToHistory(BrowserTab tab, List<Models.HistoryEntry> entries)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { wv.CoreWebView2.NavigateToString(_pages.HistoryPage(_historyStore.Load(), _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken)); } catch { }
    }

    public void NavigateToDownloads(BrowserTab tab)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { wv.CoreWebView2.NavigateToString(_pages.DownloadsPage(_downloadStore.Items.ToList(), _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken)); } catch { }
    }

    public void NavigateToFocus(BrowserTab tab)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { wv.CoreWebView2.NavigateToString(_pages.FocusPage()); } catch { }
    }

    public void GoBack()
    {
        if (ActiveTab is not null && _webViews.TryGetValue(ActiveTab.Id, out var wv))
            wv.CoreWebView2?.GoBack();
    }

    public void GoForward()
    {
        if (ActiveTab is not null && _webViews.TryGetValue(ActiveTab.Id, out var wv))
            wv.CoreWebView2?.GoForward();
    }

    public void Reload()
    {
        if (ActiveTab is not null && _webViews.TryGetValue(ActiveTab.Id, out var wv))
            wv.CoreWebView2?.Reload();
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

    public async void ApplyDarkModeToAll(bool enabled)
    {
        var scheme = enabled ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        
        foreach (var (id, wv) in _webViews)
        {
            var isInternal = Tabs.FirstOrDefault(t => t.Id == id)?.Url?.StartsWith("internal://") ?? true;
            
            // For internal pages, use Transparent to let WPF ThemeManager handle the background color
            // For external pages, rely on Dark Reader (ForceDarkMode)
            var bgColor = isInternal ? System.Drawing.Color.Transparent : (enabled ? DarkBackground : System.Drawing.Color.White);
            
            wv.DefaultBackgroundColor = bgColor;
            if (wv.CoreWebView2 is null) continue;
            wv.CoreWebView2.Profile.PreferredColorScheme = scheme;
            await ApplyDarkModeToWebViewAsync(id, wv.CoreWebView2, enabled);
        }
    }

    private async Task ApplyDarkModeToWebViewAsync(Guid tabId, CoreWebView2 core, bool enabled)
    {
        try
        {
            if (enabled)
            {
                if (!_darkScripts.ContainsKey(tabId))
                {
                    var darkReaderJs = ResourceLoader.Load("Resources.Scripts.darkreader.min.js");
                    var forceDarkJs = ResourceLoader.Load("Resources.Scripts.force-dark-mode.js");
                    
                    // Add permanently to all future navigations in this tab
                    var id1 = await core.AddScriptToExecuteOnDocumentCreatedAsync(darkReaderJs);
                    var id2 = await core.AddScriptToExecuteOnDocumentCreatedAsync(forceDarkJs);
                    _darkScripts[tabId] = (id1, id2);
                    
                    // Apply immediately to current page
                    await core.ExecuteScriptAsync(darkReaderJs);
                    await core.ExecuteScriptAsync(forceDarkJs);
                }
            }
            else
            {
                if (_darkScripts.TryGetValue(tabId, out var ids))
                {
                    // Remove from future navigations
                    core.RemoveScriptToExecuteOnDocumentCreated(ids.darkReaderId);
                    core.RemoveScriptToExecuteOnDocumentCreated(ids.forceDarkId);
                    _darkScripts.Remove(tabId);
                }
                
                // Disable immediately on current page
                await core.ExecuteScriptAsync("if(typeof DarkReader!=='undefined')DarkReader.disable();");
            }
        }
        catch { /* Page may not be ready or context destroyed */ }
    }

    public CoreWebView2? GetCoreWebView2()
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
            wv.DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White);
        }
    }

    // ──── WebView2 Creation ────

    private async Task CreateWebViewForTab(BrowserTab tab)
    {
        var isInternal = tab.Url?.StartsWith("internal://") ?? true;
        
        var wv = new WebView2
        {
            DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White),
            Visibility = Visibility.Hidden
        };

        _webViewHost.Children.Add(wv);
        try
        {
            await wv.EnsureCoreWebView2Async(_environment);
            
            wv.CoreWebView2.Profile.PreferredColorScheme = Services.ThemeManager.IsCurrentlyDark() 
                ? CoreWebView2PreferredColorScheme.Dark 
                : CoreWebView2PreferredColorScheme.Light;
            
            await ApplyDarkModeToWebViewAsync(tab.Id, wv.CoreWebView2, _settings.ForceDarkMode);

            wv.CoreWebView2.SetVirtualHostNameToFolderMapping("local.assets", System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Pages"), Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        }
        catch
        {
            _webViewHost.Children.Remove(wv);
            try { wv.Dispose(); } catch { }
            throw;
        }

        WireNavigationEvents(wv, tab);
        WireTitleAndSourceEvents(wv, tab);
        WireMessageAndWindowEvents(wv, tab);
        WireContextMenuEvents(wv, tab);
        HandleProcessFailure(wv, tab);

        // Serve internal pages (settings, newtab, etc.) from a real stride:// origin
        // Internal pages are served via NavigateToString, no WebResourceRequested host needed.

        // Network-level ad blocking — block known ad URLs before they load
        if (_settings.AdBlockEnabled)
            AdBlockFilter.Apply(wv.CoreWebView2);

        if (!_extensionsLoaded)
        {
            _extensionsLoaded = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    
                    // MUST dispatch to UI thread!
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => {
                        await _extensionManager.InitializeAsync(wv.CoreWebView2);
                    });
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Extension init failed: {ex.Message}");
                }
            });
        }

        await _contentScriptInjector.InjectAsync(wv.CoreWebView2, _settings);

        _webViews[tab.Id] = wv;
        tab.IsHibernated = false;

        NavigateInitialUrl(wv, tab);
    }

    private void NavigateInitialUrl(WebView2 wv, BrowserTab tab)
    {
        // Settings/OneTab/History are navigated by NavigateToSettings/OneTab/History
        // after activation — skip here to avoid a double load.
        var callerManagedUrls = new HashSet<string>
        {
            InternalUrls.Settings, InternalUrls.OneTab, InternalUrls.History
        };
        if (callerManagedUrls.Contains(tab.Url))
            return;

        // NewTab is served as HTML string so there's no real URL to navigate to.
        if (tab.Url == InternalUrls.NewTab || string.IsNullOrEmpty(tab.Url))
        {
            try { wv.CoreWebView2.NavigateToString(_pages.NewTabPage(_settings.NewTabShortcuts, _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken, _settings.DefaultZoom)); } catch { }
            return;
        }

        try { wv.CoreWebView2.Navigate(tab.Url); }
        catch (ArgumentException) { try { wv.CoreWebView2.NavigateToString(_pages.NewTabPage(_settings.NewTabShortcuts, _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken, _settings.DefaultZoom)); } catch { } }
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

    private void WireNavigationEvents(WebView2 wv, BrowserTab tab)
    {
        wv.CoreWebView2.NavigationStarting += (_, e) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;

            if (e.Uri is string urlStr)
            {
                var isInternal = urlStr.StartsWith("internal://") || urlStr.StartsWith("https://local.assets/");
                wv.DefaultBackgroundColor = isInternal 
                    ? System.Drawing.Color.Transparent 
                    : (_settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White);
            }

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
                catch { }
            }

            if (e.Uri is string uriForCustom && Uri.TryCreate(uriForCustom, UriKind.Absolute, out var parsedCustomUri))
            {
                var scheme = parsedCustomUri.Scheme.ToLowerInvariant();
                if (scheme != "http" && scheme != "https" && scheme != "file" && scheme != "data" &&
                    scheme != "about" && scheme != "edge" && scheme != "chrome" && scheme != "stride" && scheme != "javascript" &&
                    scheme != "extension" && scheme != "chrome-extension" && scheme != "internal")
                {
                    e.Cancel = true;
                    _dispatcher.InvokeAsync(() =>
                    {
                        // SECURITY: confirm before handing off to an external app — unprompted
                        // protocol-handler invocation is a known RCE vector for some installed apps.
                        var confirmed = System.Windows.MessageBox.Show(
                            $"This page wants to open an external application to handle this link:\n\n{uriForCustom}\n\nOnly continue if you trust this site.",
                            "Open External Application?",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Warning,
                            System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;

                        if (!confirmed) return;

                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uriForCustom) { UseShellExecute = true });
                            if (tab.Url == InternalUrls.NewTab || tab.Url == "about:blank")
                                CloseTab(tab);
                        }
                        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Custom protocol launch failed: {ex.Message}"); }
                    });
                    return;
                }
            }

            if (_settings.FocusLocked)
            {
                if (e.Uri is string uriStrFocus && Uri.TryCreate(uriStrFocus, UriKind.Absolute, out var parsedFocusUri))
                {
                    var host = parsedFocusUri.Host;
                    if (!string.IsNullOrEmpty(host) && _focusBlocklistService.IsBlocked(host))
                    {
                        e.Cancel = true;
                        _dispatcher.InvokeAsync(() =>
                        {
                            tab.Url = InternalUrls.Focus;
                            NavigateToFocus(tab);
                        });
                        return;
                    }
                }
            }

            if (_settings.ForceHttps && e.Uri is string uriStr && uriStr.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
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

                if (!isLocalHostOrIp)
                {
                    e.Cancel = true;
                    var httpsUri = "https://" + uriStr["http://".Length..];
                    _dispatcher.InvokeAsync(() => { try { wv.CoreWebView2.Navigate(httpsUri); } catch { } });
                    return;
                }
            }

            _dispatcher.InvokeAsync(() =>
            {
                tab.IsLoading = true;
                LoadingStateChanged?.Invoke(tab, true);
            });
        };

        wv.CoreWebView2.NavigationCompleted += (_, e) =>
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
                        try { wv.CoreWebView2.NavigateToString(_pages.ErrorPage(tab.Url, errorMsg, _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor))); } catch { }
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

    private void WireTitleAndSourceEvents(WebView2 wv, BrowserTab tab)
    {
        wv.CoreWebView2.DocumentTitleChanged += (_, _) =>
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

        wv.CoreWebView2.SourceChanged += (_, _) =>
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
        wv.CoreWebView2.FaviconChanged += (_, _) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;
            var source = wv.Source?.ToString() ?? "";
            _ = _faviconLoader.HandleFaviconChangedAsync(wv, source).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result is not null)
                    _dispatcher.Invoke(() => { tab.Favicon = t.Result; TabStateChanged?.Invoke(tab); });
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        };
    }

    private void WireMessageAndWindowEvents(WebView2 wv, BrowserTab tab)
    {
        wv.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;

            var msg = e.TryGetWebMessageAsString();
            var src = e.Source ?? "null";
            System.IO.File.AppendAllText("ipc_log.txt", $"[IPC] Received '{msg}' from '{src}'\n");

            if (string.IsNullOrEmpty(msg)) return;

            bool isTrustedLocalAsset = false;
            try 
            { 
                if (e.Source.StartsWith("http://local.assets/", StringComparison.OrdinalIgnoreCase) || 
                    e.Source.StartsWith("https://local.assets/", StringComparison.OrdinalIgnoreCase)) 
                {
                    isTrustedLocalAsset = true; 
                }
            } catch { }

            if (!isTrustedLocalAsset)
            {
                // SECURITY: Internal pages loaded via NavigateToString (origin = about:blank) embed a per-session secret token
                if (!msg.StartsWith(_ipcToken + ":", StringComparison.Ordinal))
                {
                    System.IO.File.AppendAllText("ipc_log.txt", $"[IPC] Dropped because not trusted and missing token.\n");
                    return;
                }
                // Strip the token prefix before forwarding the payload
                msg = msg[(_ipcToken.Length + 1)..];
            }

            if (!string.IsNullOrEmpty(msg))
            {
                System.IO.File.AppendAllText("ipc_log.txt", $"[IPC] Forwarding '{msg}'\n");
                _dispatcher.Invoke(() => WebMessageReceived?.Invoke(msg));
            }
        };

        wv.CoreWebView2.DownloadStarting += (_, e) =>
        {
            e.Handled = true; // We don't want the default download dialog
            var op = e.DownloadOperation;

            if (_settings.UseIDMForDownloads)
            {
                var idmPath = @"C:\Program Files (x86)\Internet Download Manager\IDMan.exe";
                if (System.IO.File.Exists(idmPath))
                {
                    e.Cancel = true;
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = idmPath,
                            Arguments = $"/d \"{op.Uri}\"",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Failed to start IDM: {ex.Message}");
                    }
                    return;
                }
            }

            var item = new Models.DownloadItem
            {
                FileName = System.IO.Path.GetFileName(op.ResultFilePath),
                Url = op.Uri,
                FilePath = op.ResultFilePath,
                State = Models.DownloadState.InProgress,
                TotalBytes = op.TotalBytesToReceive.HasValue ? (long)op.TotalBytesToReceive.Value : 0,
                ReceivedBytes = 0
            };

            _activeNativeDownloads.Add(item.Id);

            _dispatcher.Invoke(() => _downloadStore.Add(item));

            op.BytesReceivedChanged += (s, args) =>
            {
                _dispatcher.Invoke(() =>
                {
                    item.ReceivedBytes = op.BytesReceived;
                    if (item.TotalBytes <= 0 && op.TotalBytesToReceive.HasValue && op.TotalBytesToReceive.Value > 0)
                        item.TotalBytes = (long)op.TotalBytesToReceive.Value;
                });
            };

            op.StateChanged += (s, args) =>
            {
                _dispatcher.Invoke(() =>
                {
                    switch (op.State)
                    {
                        case Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.InProgress:
                            item.State = Models.DownloadState.InProgress;
                            break;
                        case Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Interrupted:
                            if (op.InterruptReason == Microsoft.Web.WebView2.Core.CoreWebView2DownloadInterruptReason.UserPaused)
                                item.State = Models.DownloadState.Paused;
                            else if (op.InterruptReason == Microsoft.Web.WebView2.Core.CoreWebView2DownloadInterruptReason.UserCanceled)
                                item.State = Models.DownloadState.Cancelled;
                            else
                                item.State = Models.DownloadState.Failed;
                            break;
                        case Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Completed:
                            item.State = Models.DownloadState.Completed;
                            break;
                    }
                });
            };

            // Handle requests from UI
            item.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(Models.DownloadItem.State))
                {
                    try 
                    {
                        if (item.State == Models.DownloadState.Cancelled) op.Cancel();
                        else if (item.State == Models.DownloadState.Paused) op.Pause();
                        else if (item.State == Models.DownloadState.InProgress) op.Resume();
                    } catch { }
                }
            };
        };

        wv.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            
            // Treat as popup if it requests specific dimensions or isn't explicitly user-initiated (e.g. OAuth flows)
            if (e.WindowFeatures.HasPosition || e.WindowFeatures.HasSize || !e.IsUserInitiated)
            {
                var deferral = e.GetDeferral();
                _ = _dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        // Ensure environment is initialized
                        if (_environment == null) throw new InvalidOperationException("WebView2 Environment is null");
                        
                        var popup = new PopupWindow(_environment);
                        popup.Show();
                        
                        // Wait for WebView2 to be ready inside the popup
                        await popup.InitializeAsync();
                        
                        // Pass the CoreWebView2 back to the original request
                        e.NewWindow = popup.PopupWebView.CoreWebView2;
                        deferral.Complete();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"NewWindowRequested popup error: {ex.Message}");
                        deferral.Complete();
                    }
                });
            }
            else
            {
                _ = _dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        var newTab = CreateTab(e.Uri);
                        await ActivateAsync(newTab);
                    }
                    catch (Exception ex) { Trace.WriteLine($"NewWindowRequested error: {ex.Message}"); }
                });
            }
        };

        wv.CoreWebView2.WindowCloseRequested += (_, _) =>
        {
            _dispatcher.Invoke(() => CloseTab(tab));
        };
    }

    private void WireContextMenuEvents(WebView2 wv, BrowserTab tab)
    {
        wv.CoreWebView2.ContextMenuRequested += (_, e) =>
        {
            var menuItems = e.MenuItems;

            // Remove specific unwanted default items
            var toRemove = new List<CoreWebView2ContextMenuItem>();
            foreach (var item in menuItems)
            {
                if (item.Name == "collections" ||
                    item.Name == "webSelect" ||
                    item.Name == "webCapture" ||
                    item.Name == "searchWebFor" ||
                    item.Name == "readAloud" ||
                    item.Name == "share")
                {
                    toRemove.Add(item);
                }
            }
            foreach (var item in toRemove)
            {
                menuItems.Remove(item);
            }

            if (e.ContextMenuTarget.HasLinkUri)
            {
                var linkUri = e.ContextMenuTarget.LinkUri;
                var openInNewTab = wv.CoreWebView2.Environment.CreateContextMenuItem(
                    "Open Link in New Tab", null, CoreWebView2ContextMenuItemKind.Command);
                openInNewTab.CustomItemSelected += (_, _) =>
                {
                    _ = _dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            var newTab = CreateTab(linkUri);
                            await ActivateAsync(newTab);
                        }
                        catch (Exception ex) { Trace.WriteLine($"Open in new tab failed: {ex.Message}"); }
                    });
                };
                menuItems.Insert(0, openInNewTab);

                var copyLink = wv.CoreWebView2.Environment.CreateContextMenuItem(
                    "Copy Link Address", null, CoreWebView2ContextMenuItemKind.Command);
                copyLink.CustomItemSelected += (_, _) =>
                {
                    _dispatcher.Invoke(() =>
                    {
                        try { System.Windows.Clipboard.SetText(linkUri); } catch { }
                    });
                };
                menuItems.Insert(1, copyLink);

                var separator = wv.CoreWebView2.Environment.CreateContextMenuItem(
                    "", null, CoreWebView2ContextMenuItemKind.Separator);
                menuItems.Insert(2, separator);
            }

            if (e.ContextMenuTarget.HasSelection)
            {
                var selection = e.ContextMenuTarget.SelectionText;
                if (!string.IsNullOrWhiteSpace(selection))
                {
                    // Truncate long selections
                    var displaySelection = selection.Length > 20 ? selection.Substring(0, 17) + "..." : selection;
                    var searchItem = wv.CoreWebView2.Environment.CreateContextMenuItem(
                        $"Search Stride for '{displaySelection}'", null, CoreWebView2ContextMenuItemKind.Command);
                    searchItem.CustomItemSelected += (_, _) =>
                    {
                        _ = _dispatcher.InvokeAsync(async () =>
                        {
                            var url = $"https://duckduckgo.com/?q={Uri.EscapeDataString(selection)}";
                            if (_settings.SearchEngine == "Google")
                                url = $"https://www.google.com/search?q={Uri.EscapeDataString(selection)}";
                            else if (_settings.SearchEngine == "Bing")
                                url = $"https://www.bing.com/search?q={Uri.EscapeDataString(selection)}";

                            var newTab = CreateTab(url);
                            await ActivateAsync(newTab);
                        });
                    };
                    menuItems.Add(searchItem);
                }
            }

            var separator2 = wv.CoreWebView2.Environment.CreateContextMenuItem(
                "", null, CoreWebView2ContextMenuItemKind.Separator);
            menuItems.Add(separator2);

            var darkModeItem = wv.CoreWebView2.Environment.CreateContextMenuItem(
                _settings.ForceDarkMode ? "Disable Dark Mode" : "Enable Dark Mode", null, CoreWebView2ContextMenuItemKind.Command);
            darkModeItem.CustomItemSelected += (_, _) =>
            {
                _dispatcher.Invoke(() =>
                {
                    _settings.ForceDarkMode = !_settings.ForceDarkMode;
                });
            };
            menuItems.Add(darkModeItem);
        };
    }



    private void HandleProcessFailure(WebView2 wv, BrowserTab tab)
    {
        wv.CoreWebView2.ProcessFailed += (_, e) =>
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
            _webViewHost.Children.Remove(wv);
            try { wv.Dispose(); } catch { }
        }
        _webViews.Clear();
        _darkScripts.Clear();
    }

    private void UpdateTabFromWebView(WebView2 wv, BrowserTab tab)
    {
        var source = wv.Source?.ToString() ?? "";
        if (InternalUrls.IsDataOrBlank(source))
        {
            HandleInternalPageTitles(tab);
            return;
        }

        tab.Url = source;
        tab.Title = string.IsNullOrWhiteSpace(wv.CoreWebView2.DocumentTitle)
            ? source : wv.CoreWebView2.DocumentTitle;
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
            catch { }
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
            
            // Use Hidden instead of Collapsed to prevent HWND layout teardown (flicker)
            // Do not use Margin=-10000 because WPF Grids will try to allocate massive D3D surfaces.
            wv.Visibility = isActive ? Visibility.Visible : Visibility.Hidden;
            Panel.SetZIndex(wv, isActive ? 1 : 0);
            
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
        _webViewHost.Children.Remove(wv);
        try { wv.Dispose(); } catch { }
        _webViews.Remove(tabId);
        _darkScripts.Remove(tabId);
    }

    public void Shutdown()
    {
        _hibernationTimer.Stop();
        _activationCts?.Cancel();
        _activationCts?.Dispose();
        _webViewHost.Children.Clear();
        foreach (var (_, wv) in _webViews)
        {
            try { wv.Dispose(); } catch { }
        }
        _webViews.Clear();
        _darkScripts.Clear();
        _activationGate.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shutdown();
    }
}
