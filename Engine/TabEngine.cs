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
    private readonly Panel _webViewHost;
    private readonly ExtensionManager _extensionManager;
    private readonly YouTubeEnhancer _youtubeEnhancer;
    private readonly YouTubeUnhook _youtubeUnhook;
    private readonly BrowserSettings _settings;
    private readonly FaviconLoader _faviconLoader;
    private readonly InternalPages _pages;
    private readonly IHistoryStore _historyStore;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _hibernationTimer;
    private readonly Dictionary<Guid, WebView2> _webViews = new();
    private readonly LinkedList<(string url, string title)> _closedTabs = new();

    private CoreWebView2Environment? _environment;
    private bool _extensionsLoaded;
    private readonly SemaphoreSlim _activationGate = new(1, 1); // Serialize WebView2 creation
    private CancellationTokenSource? _activationCts;
    private const int MaxClosedTabs = 50;
    private static readonly System.Drawing.Color DarkBackground = System.Drawing.Color.FromArgb(255, 24, 24, 30);
    private bool _disposed;

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

    public TabEngine(Panel webViewHost, EngineDependencies deps)
    {
        _webViewHost = webViewHost;
        _extensionManager = deps.ExtensionManager;
        _youtubeEnhancer = deps.YouTubeEnhancer;
        _youtubeUnhook = deps.YouTubeUnhook;
        _settings = deps.Settings;
        _faviconLoader = deps.FaviconLoader;
        _pages = deps.Pages;
        _historyStore = deps.HistoryStore;
        _dispatcher = webViewHost.Dispatcher;

        // Check every 60s whether any tabs have exceeded the hibernation timeout
        _hibernationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _hibernationTimer.Tick += (_, _) => HibernateInactiveTabs();
        _hibernationTimer.Start();
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
            // Reduce telemetry and background activity
            AdditionalBrowserArguments =
                "--app-user-model-id=Stride " +
                "--renderer-process-limit=4 " +
                "--disable-background-networking " +
                "--disable-breakpad " +
                "--disable-component-update " +
                "--disable-default-apps " +
                "--disable-domain-reliability " +
                "--disable-sync " +
                "--disable-features=msSmartScreenProtection " +
                "--metrics-recording-only " +
                "--no-first-run"
        };
        _environment = await CoreWebView2Environment.CreateAsync(null, dataDir, options);

        // Handle browser process exit at the environment level
        _environment.BrowserProcessExited += (_, e) =>
        {
            Trace.WriteLine($"WebView2 browser process exited: Kind={e.BrowserProcessExitKind}");
            _dispatcher.Invoke(HandleBrowserProcessDeath);
        };
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

        // Immediately hide all WebViews — gives instant visual feedback
        foreach (var (_, wv) in _webViews)
            wv.Visibility = Visibility.Collapsed;

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

    /// <summary>Navigates the active tab to a URL.</summary>
    public void Navigate(BrowserTab tab, string url)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;

        if (url == InternalUrls.NewTab)
            wv.CoreWebView2.NavigateToString(_pages.NewTabPage());
        else
        {
            try { wv.CoreWebView2.Navigate(url); }
            catch (ArgumentException) { /* Invalid URL */ }
        }
    }

    public void NavigateToSettings(BrowserTab tab, BrowserSettings settings)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        wv.CoreWebView2.NavigateToString(_pages.SettingsPage(settings));
    }

    public void NavigateToOneTab(BrowserTab tab, List<OneTabGroup> groups)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        wv.CoreWebView2.NavigateToString(_pages.OneTabPage(groups));
    }

    public void NavigateToHistory(BrowserTab tab, List<Models.HistoryEntry> entries)
    {
        if (_webViews.TryGetValue(tab.Id, out var wv))
            wv.CoreWebView2.NavigateToString(_pages.HistoryPage(entries));
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

    public void ApplyDarkModeToAll(bool enabled)
    {
        var scheme = enabled ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Auto;
        foreach (var (_, wv) in _webViews)
        {
            if (wv.CoreWebView2 is not null)
                wv.CoreWebView2.Profile.PreferredColorScheme = scheme;
        }
    }

    public CoreWebView2? GetCoreWebView2()
    {
        if (ActiveTab is not null && _webViews.TryGetValue(ActiveTab.Id, out var wv))
            return wv.CoreWebView2;
        return null;
    }

    // ──── WebView2 Creation ────

    private async Task CreateWebViewForTab(BrowserTab tab)
    {
        var wv = new WebView2
        {
            DefaultBackgroundColor = DarkBackground,
            Visibility = Visibility.Collapsed
        };

        _webViewHost.Children.Add(wv);
        try
        {
            await wv.EnsureCoreWebView2Async(_environment);

            if (_settings.ForceDarkMode)
                wv.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
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

        // Network-level ad blocking — block known ad URLs before they load
        if (_settings.AdBlockEnabled)
            WireAdBlockFilters(wv);

        if (!_extensionsLoaded)
        {
            try
            {
                await _extensionManager.InitializeAsync(wv.CoreWebView2);
                _extensionsLoaded = true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Extension init failed (will retry): {ex.Message}");
            }
        }

        await InjectContentScriptsAsync(wv);

        if (_settings.DefaultZoom != 100)
            wv.ZoomFactor = _settings.DefaultZoom / 100.0;

        _webViews[tab.Id] = wv;
        tab.IsHibernated = false;

        NavigateInitialUrl(wv, tab);
    }

    private void NavigateInitialUrl(WebView2 wv, BrowserTab tab)
    {
        var callerManagedUrls = new HashSet<string> { "stride://settings", "stride://onetab", "stride://history" };
        if (callerManagedUrls.Contains(tab.Url))
            return;

        if (tab.Url == InternalUrls.NewTab)
            wv.CoreWebView2.NavigateToString(_pages.NewTabPage());
        else
        {
            try { wv.CoreWebView2.Navigate(tab.Url); }
            catch (ArgumentException) { wv.CoreWebView2.NavigateToString(_pages.NewTabPage()); }
        }
    }

    private async Task InjectContentScriptsAsync(WebView2 wv)
    {
        // YouTube ad nuker — injected first so ads are killed before anything else
        if (_settings.AdBlockEnabled)
        {
            await wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                ResourceLoader.Load("Resources.Scripts.youtube-adnuke.js"));
        }

        // YouTube enhancer (quality, speed, loop — self-guards)
        var enhancer = _youtubeEnhancer.GetScript(_settings);
        if (!string.IsNullOrEmpty(enhancer))
            await wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(enhancer);

        // YouTube unhook (hide distractions — self-guards)
        var unhook = _youtubeUnhook.GetScript(_settings);
        if (!string.IsNullOrEmpty(unhook))
            await wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(unhook);

        // Force dark mode via Dark Reader (MIT License — github.com/darkreader/darkreader)
        if (_settings.ForceDarkMode)
        {
            await wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                ResourceLoader.Load("Resources.Scripts.darkreader.min.js"));
            await wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                ResourceLoader.Load("Resources.Scripts.force-dark-mode.js"));
        }
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

            if (e.Uri is string uriForCustom && Uri.TryCreate(uriForCustom, UriKind.Absolute, out var parsedCustomUri))
            {
                var scheme = parsedCustomUri.Scheme.ToLowerInvariant();
                if (scheme != "http" && scheme != "https" && scheme != "file" && scheme != "data" && 
                    scheme != "about" && scheme != "edge" && scheme != "chrome" && scheme != "stride" && scheme != "javascript")
                {
                    e.Cancel = true;
                    _dispatcher.InvokeAsync(() =>
                    {
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
                        try { wv.CoreWebView2.NavigateToString(_pages.ErrorPage(tab.Url, errorMsg)); } catch { }
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
    }

    private void WireMessageAndWindowEvents(WebView2 wv, BrowserTab tab)
    {
        wv.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;
            var msg = e.TryGetWebMessageAsString();
            if (!string.IsNullOrEmpty(msg))
                _dispatcher.Invoke(() => WebMessageReceived?.Invoke(msg));
        };

        wv.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            _ = _dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var newTab = CreateTab(e.Uri);
                    await ActivateAsync(newTab);
                }
                catch (Exception ex) { Trace.WriteLine($"NewWindowRequested error: {ex.Message}"); }
            });
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
        };
    }

    /// <summary>
    /// Blocks known ad-serving URLs at the network level.
    /// Requests matching these patterns are cancelled before loading.
    /// </summary>
    private void WireAdBlockFilters(WebView2 wv)
    {
        // YouTube ad video/tracking URL patterns
        string[] adPatterns =
        [
            "*://*.doubleclick.net/*",
            "*://*.googlesyndication.com/*",
            "*://*.googleadservices.com/*",
            "*://*.google-analytics.com/*",
            "*://www.youtube.com/api/stats/ads*",
            "*://www.youtube.com/pagead/*",
            "*://www.youtube.com/get_midroll_info*",
            "*://*.youtube.com/ptracking*",
            "*://*.youtube.com/api/stats/qoe*",
            "*://yt3.ggpht.com/*/ads/*",
            "*://*.googleads.g.doubleclick.net/*",
            "*://ad.youtube.com/*",
            "*://ads.youtube.com/*",
            "*://*.moatads.com/*",
            "*://*.adsafeprotected.com/*"
        ];

        foreach (var pattern in adPatterns)
        {
            wv.CoreWebView2.AddWebResourceRequestedFilter(
                pattern, CoreWebView2WebResourceContext.All);
        }

        wv.CoreWebView2.WebResourceRequested += (_, e) =>
        {
            // Block the request by returning an empty response
            e.Response = wv.CoreWebView2.Environment.CreateWebResourceResponse(
                null, 200, "OK", "");
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
            _ => tab.Title
        };
    }

    // ──── Memory Management ────

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
            .Where(t => !InternalUrls.IsInternal(t.Url))
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
            if (tab.IsActive || tab.IsHibernated) continue;
            if (tab.LastActiveTime > cutoff) continue;
            if (InternalUrls.IsInternal(tab.Url)) continue;

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
            wv.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;

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
        _activationGate.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shutdown();
    }
}
