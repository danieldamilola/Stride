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
using StrideBrowser.Services.ContextMenus;
using StrideBrowser.Services.UI;

namespace StrideBrowser.Engine;

/// <summary>
/// Manages the full tab lifecycle: creation, switching, hibernation, and disposal.
/// Owns all WebView2 instances. The single deepest module in the browser.
/// </summary>
public sealed partial class TabEngine : IDisposable
{
    private Panel? _webViewHost;
    private readonly ExtensionManager _extensionManager;
    private readonly ExtensionStartupCoordinator _extensionStartup;
    private readonly YouTubeUnhook _youtubeUnhook;
    private readonly YouTubeEnhancer _youtubeEnhancer;
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
    /// <summary>The newest navigation request per tab. A commit that is not the newest is dropped.</summary>
    private readonly Dictionary<Guid, long> _navigationIntents = new();
    /// <summary>
    /// Cancelled when the engine is disposed, so a first navigation wait can be cut
    /// short by the window closing. A tab switch is not this: switching tabs must not
    /// abandon a registration wait that another tab or window is still using.
    /// </summary>
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly WebViewFactory _webViewFactory;
    private readonly WebViewIpcBridge _ipcBridge;

    // Closed tabs live in the persistent store, not here. The store keeps the
    // newest closure last and survives exit, so Ctrl+Shift+T reopens the same
    // tab after a restart. Anything removed from the tab list other than by an
    // explicit user close (session restore, SendAllToOneTab) is not a closure
    // and must not land in this history.
    private readonly IClosedTabStore _closedTabStore;

    private readonly SemaphoreSlim _activationGate = new(1, 1); // Serialize WebView2 creation
    private CancellationTokenSource? _activationCts;
    private bool _disposed;

    // Per-session random token embedded in every internal page. IPC messages that
    // don't include this token are rejected - this replaces the origin-based check
    // which broke when internal pages ran on about:blank. The value is process wide,
    // so a tab moved between windows keeps a token the destination window knows.
    private readonly string _ipcToken;
    private readonly string _telemetryToken;
    private readonly TabReadinessCoordinator _readinessCoordinator = new();
    private readonly Dictionary<Guid, WebViewSubscriptions> _subscriptions = new();
    /// <summary>
    /// The window handle each live WebView is bound to. WebView2 binds a control to the
    /// window it is shown in, so a tab that has been torn off depends on that window even
    /// though the engine that owns it now lives elsewhere.
    /// </summary>
    private readonly Dictionary<Guid, IntPtr> _controlHosts = new();
    /// <summary>Tabs whose WebView is being built right now, so a drag cannot move one mid flight.</summary>
    private readonly HashSet<Guid> _creatingTabs = new();
    private Task? _initialization;
    /// <summary>Native downloads in flight. Process wide, so a download survives its tab moving windows.</summary>
    private readonly Dictionary<string, Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation> _activeNativeDownloads;

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

    public Func<Guid, bool>? IsReaderActive { get; set; }
    public Func<Guid, bool>? IsReaderAvailable { get; set; }
    public Func<Guid, Task>? ExitReaderAsync { get; set; }
    public Func<Guid, Task>? ToggleReaderAsync { get; set; }
    public Func<Task>? LaunchTCLensAsync { get; set; }

    private readonly Services.CustomDownloadManager _customDownloadManager;

    /// <summary>Initializes a new instance of the TabEngine.</summary>
    public TabEngine(EngineDependencies deps)
    {
        _ipcToken = deps.Session.IpcToken;
        _telemetryToken = deps.Session.TelemetryToken;
        _activeNativeDownloads = deps.Session.NativeDownloads;
        _extensionManager = deps.ExtensionManager;
        // The coordinator is process wide because the profile is. Every window here
        // talks to the same WebView2 user data folder, so the registration has to be
        // shared rather than counted once per window.
        _extensionStartup = deps.Session.ExtensionStartup;
        _youtubeUnhook = deps.YouTubeUnhook;
        _youtubeEnhancer = deps.YouTubeEnhancer;
        _settings = deps.Settings;
        _faviconLoader = deps.FaviconLoader;
        _pages = deps.Pages;
        _historyStore = deps.HistoryStore;
        _oneTabStore = deps.OneTabStore;
        _downloadStore = deps.DownloadStore;
        Downloads = deps.Downloads ?? new Services.Downloads.DownloadCoordinator(_downloadStore);
        _downloadRequests = deps.DownloadRequests ?? new Services.Downloads.NativeDownloadRequest(_downloadStore, deps.Session, Downloads);
        _closedTabStore = deps.ClosedTabStore;
        _focusBlocklistService = deps.FocusBlocklistService;
        _contentScriptInjector = deps.ContentScriptInjector;
        _customDownloadManager = deps.CustomDownloadManager;
        _hibernationManager = deps.HibernationManager;
        _navigationPolicyEngine = deps.NavigationPolicyEngine;
        _themeManager = deps.ThemeManager;
        _dispatcher = Application.Current.Dispatcher;

        _webViewFactory = new WebViewFactory(_settings, _themeManager, _pages, _extensionManager, _extensionStartup, _ipcToken, deps.Session.WebViewDataDirectory);
        _ipcBridge = new WebViewIpcBridge(
            _dispatcher,
            _settings,
            _downloadStore,
            _ipcToken,
            _telemetryToken,
            _activeNativeDownloads,
            isTabAlive: id => _webViews.ContainsKey(id),
            getWebView: id => _webViews.TryGetValue(id, out var wv) ? wv : null,
            createTab: url => CreateTab(url),
            activateAsync: ActivateAsync,
            closeTab: CloseTab,
            ensureTabReadyAsync: EnsureTabReadyAsync,
            removeSentinelTab: RemoveFailedSentinelTab,
            downloadSuppressor: deps.DownloadSuppressor,
            downloadCoordinator: Downloads);
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

        // BrowserSettings is a process singleton shared by every window, and each window
        // has its own engine and its own live profiles. Subscribing here is what makes a
        // level picked in one window reach the profiles in all of them. It is removed in
        // Dispose, because a torn-off window's engine outlives nothing.
        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BrowserSettings.TabSleepEnabled) or nameof(BrowserSettings.AllowPinnedTabsToSleep))
        {
            _dispatcher.Invoke(_hibernationManager.ApplySleepSettings);
            return;
        }
        if (e.PropertyName != nameof(BrowserSettings.TrackingPreventionMode)) return;
        // Raised on whichever thread assigned the property, which for a settings page is
        // not the one that owns the WebView2 controls.
        _dispatcher.Invoke(ApplyTrackingPreventionToLiveProfiles);
    }

    /// <summary>
    /// Pushes the current setting to every live profile in this window and reports how
    /// many profiles were written. Called when the setting changes and, through
    /// WebViewFactory.ConfigureCoreWebView, as each control is created.
    /// </summary>
    public int ApplyTrackingPreventionToLiveProfiles()
    {
        var level = TrackingPreventionModes.ToWebView2Level(_settings.TrackingPreventionMode);
        var applied = 0;
        // One profile can back several controls in a window. Writing the same profile twice
        // is harmless, but keying on it keeps the count honest about profiles.
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in _webViews.Keys.ToArray())
        {
            if (!TryGetCore(id, out var core) || core is null) continue;

            try
            {
                var path = core.Profile.ProfilePath;
                if (!string.IsNullOrEmpty(path) && !written.Add(path)) continue;
            }
            catch (Exception ex) { Trace.WriteLine($"TabEngine tracking prevention profile path unavailable: {ex.Message}"); }

            try
            {
                core.Profile.PreferredTrackingPreventionLevel = level;
                applied++;
            }
            catch (Exception ex) { Trace.WriteLine($"TabEngine tracking prevention not applied to tab {id}: {ex.Message}"); }
        }

        Trace.WriteLine($"TabEngine tracking prevention {_settings.TrackingPreventionMode} -> {level} on {applied} profile(s)");
        return applied;
    }

    public void AttachHost(Panel webViewHost)
    {
        _webViewHost = webViewHost;
    }

/// <summary>Must be called once at startup to create the WebView2 environment.</summary>
    public Task InitializeAsync() => _initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        await _webViewFactory.InitializeAsync();
        _webViewFactory.BrowserProcessExited += OnBrowserProcessExited;
    }

    // ──── Tab Lifecycle ────

    public BrowserTab CreateTab(string? url = null, bool blockDuplicates = true, bool switchTo = true)
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
                if (switchTo) SwitchTo(existing);
                return existing;
            }
        }

        var title = resolvedUrl switch
        {
            InternalUrls.NewTab => "New Tab",
            InternalUrls.Settings => "Settings",
            InternalUrls.OneTab => "OneTab",
            InternalUrls.History => "History",
            InternalUrls.Extensions => "Extensions",
            InternalUrls.Downloads => "Downloads",
            InternalUrls.Onboarding => "Welcome to Stride",
            InternalUrls.ReleaseNotes => "What's New in Stride",
            InternalUrls.Focus => "Focus Locked",
            InternalUrls.Health => "Health",
            _ => "New Tab"
        };

        var tab = new BrowserTab { Url = resolvedUrl, Title = title };
        Tabs.Add(tab);
        if (switchTo) SwitchTo(tab);
        TabCreated?.Invoke(tab);
        return tab;
    }

    public void CloseTab(BrowserTab tab)
    {
        if (Tabs.Count <= 1) return;

        var index = Tabs.IndexOf(tab);
        if (index < 0) return; // Tab already removed

        // A real user close. This is the only path that feeds the reopen history,
        // and the tab's own URL is captured before the WebView goes away because
        // an internal page has no navigable source.
        _closedTabStore.Record(tab.Url, tab.Title, tab.IsPinned);

        tab.ActiveInternalDocument = null;
        Tabs.Remove(tab);
        DisposeWebView(tab);
        TabClosed?.Invoke(tab.Id);

        if (tab == ActiveTab && Tabs.Count > 0)
            SwitchTo(Tabs[Math.Min(index, Tabs.Count - 1)]);
    }

    /// <summary>
    /// Removes a sentinel tab whose native popup adoption or initialization failed.
    /// This cleans up the control and tab entry without recording it in the closed tab history.
    /// </summary>
    public void RemoveFailedSentinelTab(BrowserTab tab)
    {
        _dispatcher.VerifyAccess();
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        Tabs.Remove(tab);
        DisposeWebView(tab);
        TabClosed?.Invoke(tab.Id);

        if (tab == ActiveTab && Tabs.Count > 0)
            SwitchTo(Tabs[Math.Min(index, Tabs.Count - 1)]);
    }

    /// <summary>
    /// Reopens the most recently closed tab. Reads the newest entry out of the
    /// persistent history, so it still works after a restart. Only that entry is
    /// consumed; the rest of the history stays in order for the next press.
    /// </summary>
    public BrowserTab? RestoreClosedTab()
    {
        var newest = _closedTabStore.TakeNewest();
        if (newest is null) return null;

        // Never the duplicate check: reopening a tab whose URL is already open is
        // the whole point.
        var tab = CreateTab(newest.Url, blockDuplicates: false);
        tab.Title = newest.Title;
        tab.IsPinned = newest.WasPinned;
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

        var survivor = Tabs.Count > 0 ? Tabs[0] : CreateTab();

        // Whether the survivor needs an activation is decided by what it was
        // showing, not by guessing. The loop above disposes only non internal
        // tabs, so a survivor that was already the active tab still owns a
        // visible control and must not be activated again. A survivor that was
        // not active is left in one of two states, both blank: its control was
        // collapsed when another tab took over, or it never had one at all.
        var survivorWasShowing = ActiveTab?.Id == survivor.Id;

        // SwitchTo raises TabStateChanged, which is where a window activates a tab
        // that has just become the active one, and it wakes a sleeping or hibernated
        // survivor through the same call. Activating here as well would cancel the
        // activation the window just started: ActivateAsync cancels the in flight
        // call on entry, so the pair would race and the loser would return before
        // showing anything.
        Trace.WriteLine($"SendAllToOneTab survivor={survivor.Id} wasShowing={survivorWasShowing}");
        SwitchTo(survivor);

        return entries;
    }

    public void SwitchTo(BrowserTab tab)
    {
        if (_disposed || !Tabs.Contains(tab)) return;
        foreach (var t in Tabs) t.IsActive = false;
        tab.IsActive = true;
        ActiveTab = tab;
        ShowOnlyActiveWebView(tab);
        TabStateChanged?.Invoke(tab);
        ActiveTabChanged?.Invoke(tab);
    }

    /// <summary>Activates a tab's WebView2 - creates one if needed, shows it, hides others.</summary>
    public async Task ActivateAsync(BrowserTab tab)
    {
        if (_disposed || !Tabs.Contains(tab) || _webViewFactory.Environment is null) return;

        // Background readiness may create a control, but it does not own the display
        // and must not cancel the selected tab's activation.
        CancellationTokenSource? cts = null;
        if (ActiveTab == tab)
        {
            _activationCts?.Cancel();
            _activationCts?.Dispose();
            cts = _activationCts = new CancellationTokenSource();
        }

        // Check if creation or setup is already in flight for this tab.
        // Setup completion takes precedence over mere control or CoreWebView2 existence.
        if (_readinessCoordinator.TryGet(tab.Id, out var inFlightTask))
        {
            try
            {
                await inFlightTask;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ActivateAsync in-flight readiness wait failed for tab {tab.Title}: {ex.Message}");
                return;
            }

            if (cts is null || cts.IsCancellationRequested || _disposed || ActiveTab != tab || !Tabs.Contains(tab)) return;

            ShowOnlyActiveWebView(tab);
            tab.LastActiveTime = DateTime.UtcNow;
            _hibernationManager.SuspendBackgroundTabs(tab);
            _hibernationManager.EvictExcessWebViews(tab);
            return;
        }

        // If the tab already has a live, fully set up WebView, show it immediately
        if (_webViews.TryGetValue(tab.Id, out var existing) && existing.CoreWebView2 is not null && !tab.IsHibernated)
        {
            if (cts is null || ActiveTab != tab) return;
            existing.CoreWebView2.Resume();
            tab.IsSleeping = false;
            ShowOnlyActiveWebView(tab);
            tab.LastActiveTime = DateTime.UtcNow;
            _hibernationManager.SuspendBackgroundTabs(tab);
            return;
        }

        // Atomically publish readiness entry BEFORE any asynchronous wait
        var (sharedTask, isCreator) = _readinessCoordinator.GetOrAdd(tab.Id);

        if (!isCreator)
        {
            try
            {
                await sharedTask;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ActivateAsync shared readiness wait failed for tab {tab.Title}: {ex.Message}");
                return;
            }

            if (cts is null || cts.IsCancellationRequested || _disposed || ActiveTab != tab || !Tabs.Contains(tab)) return;

            ShowOnlyActiveWebView(tab);
            tab.LastActiveTime = DateTime.UtcNow;
            _hibernationManager.SuspendBackgroundTabs(tab);
            _hibernationManager.EvictExcessWebViews(tab);
            return;
        }

        // WebView needs creation - creator acquires gate to serialize runtime creation.
        // Gate wait is owned by engine lifecycle (_shutdownCts), NOT display activation (cts).
        // A tab switch cancels display continuation (cts) but must not abort shared creation that other waiters depend on.
        try
        {
            await _activationGate.WaitAsync(_shutdownCts.Token);
        }
        catch (OperationCanceledException)
        {
            _readinessCoordinator.TryCancel(tab.Id);
            return;
        }
        catch (Exception ex)
        {
            _readinessCoordinator.TrySetException(tab.Id, ex);
            throw;
        }

        try
        {
            if (_disposed || !Tabs.Contains(tab))
            {
                _readinessCoordinator.TryCancel(tab.Id);
                return;
            }

            if (!_webViews.ContainsKey(tab.Id) || tab.IsHibernated)
            {
                await CreateWebViewForTab(tab);
            }
            else
            {
                _readinessCoordinator.TrySetResult(tab.Id, _webViews[tab.Id]);
            }
        }
        catch (OperationCanceledException)
        {
            _readinessCoordinator.TryCancel(tab.Id);
            return;
        }
        catch (Exception ex)
        {
            _readinessCoordinator.TrySetException(tab.Id, ex);
            InitializationFailed?.Invoke(ex);
            Trace.WriteLine($"ActivateAsync failed for tab {tab.Title}: {ex.Message}");
            return;
        }
        finally
        {
            _activationGate.Release();
        }

        if (cts is null || cts.IsCancellationRequested || _disposed || ActiveTab != tab || !Tabs.Contains(tab)) return;

        ShowOnlyActiveWebView(tab);
        tab.LastActiveTime = DateTime.UtcNow;

        _hibernationManager.SuspendBackgroundTabs(tab);
        _hibernationManager.EvictExcessWebViews(tab);
    }

    /// <summary>
    /// Awaits readiness of a tab's CoreWebView2 without altering active tab display.
    /// Used by popup adoption to ensure the target tab has an initialized core.
    /// Cancellation remains local to the caller and does not cancel shared initialization.
    /// </summary>
    public async Task<CoreWebView2?> EnsureTabReadyAsync(BrowserTab tab, CancellationToken ct = default)
    {
        if (_disposed || !Tabs.Contains(tab) || _webViewFactory.Environment is null) return null;

        // If an in-flight readiness operation exists, await it first (setup completion takes precedence)
        if (!_readinessCoordinator.TryGet(tab.Id, out var readyTask))
        {
            // If already fully ready and no operation in flight
            if (_webViews.TryGetValue(tab.Id, out var existing) && existing?.CoreWebView2 is not null && !tab.IsHibernated)
                return existing.CoreWebView2 as CoreWebView2;

            // Trigger activation to start creation if not started
            _ = ActivateAsync(tab);

            // Re-fetch the registered readiness task
            _readinessCoordinator.TryGet(tab.Id, out readyTask);
        }

        if (readyTask is not null)
        {
            try
            {
                var result = await TabReadinessCoordinator.WaitWithCancellationAsync(readyTask, ct);
                dynamic? wv = result;
                if (wv?.CoreWebView2 is CoreWebView2 core)
                    return core;
            }
            catch (OperationCanceledException)
            {
                // Local cancellation only
                return null;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"EnsureTabReadyAsync failed for tab {tab.Title}: {ex.Message}");
                return null;
            }
        }

        if (_webViews.TryGetValue(tab.Id, out var finalWv) && finalWv?.CoreWebView2 is not null)
            return finalWv.CoreWebView2 as CoreWebView2;

        return null;
    }

    /// <summary>Which built in page a tab URL names, if any.</summary>
    public enum InternalPageKind
    {
        None,
        NewTab,
        Settings,
        OneTab,
        History,
        Downloads,
        Extensions,
        Onboarding,
        ReleaseNotes,
        Focus,
        Health,
        PendingNative,
    }

    /// <summary>
    /// Maps a tab URL to its built in page. Pure logic, covered by unit tests.
    /// PendingNative is the popup handoff sentinel, not a page: its core must
    /// stay clean so the requesting renderer can adopt it.
    /// </summary>
    public static InternalPageKind GetInternalPageKind(string? url)
    {
        if (string.IsNullOrEmpty(url)) return InternalPageKind.NewTab;
        if (url == InternalUrls.NewTab) return InternalPageKind.NewTab;
        if (url == InternalUrls.Settings) return InternalPageKind.Settings;
        if (url == InternalUrls.OneTab) return InternalPageKind.OneTab;
        if (url == InternalUrls.History) return InternalPageKind.History;
        if (url == InternalUrls.Extensions) return InternalPageKind.Extensions;
        if (url == InternalUrls.Downloads) return InternalPageKind.Downloads;
        if (url == InternalUrls.Onboarding) return InternalPageKind.Onboarding;
        if (url == InternalUrls.ReleaseNotes) return InternalPageKind.ReleaseNotes;
        if (url == InternalUrls.Focus) return InternalPageKind.Focus;
        if (url == InternalUrls.Health) return InternalPageKind.Health;
        if (url == "internal://pending-native") return InternalPageKind.PendingNative;
        return InternalPageKind.None;
    }

    /// <summary>
    /// True when the URL names a built in page that routing can re-serve.
    /// Pure logic, covered by unit tests.
    /// </summary>
    public static bool IsServableBuiltInPage(string? url)
    {
        var kind = GetInternalPageKind(url);
        return kind is not InternalPageKind.None && kind is not InternalPageKind.PendingNative;
    }

    /// <summary>
    /// Best effort application of one optional core tuning call. A moved or
    /// stale core can reject it, and that must never escape: the caller loops
    /// over every view, and one rejection must not strand the rest.
    /// Pure logic, covered by unit tests.
    /// </summary>
    public static void TryApplyOptionalCoreSetting(Action apply, string what, Guid tabId)
    {
        try { apply(); }
        catch (Exception ex) { Trace.WriteLine($"{what} skipped for tab {tabId}: {ex.Message}"); }
    }

    /// <summary>Navigates the active tab to a URL. Internal pages are served by ServeBuiltInPage, not by a host request filter.</summary>
    public void Navigate(BrowserTab tab, string url)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;

        // Every document this engine puts into a core takes a fresh intent, so a
        // request that had to wait behind the registration gate cannot land after a
        // newer request already did.
        var intent = BeginNavigationIntent(tab.Id);

        tab.Url = url;

        if (ServeBuiltInPage(tab)) return;

        tab.ActiveInternalDocument = null;

        if (IsExternalRequestUrl(url))
        {
            // A URL typed into a fresh new tab is the first external navigation on
            // this control, so it waits for the same registration the startup path
            // waits for. The wait does not leak into the caller: once the profile is
            // reconciled the gate completes synchronously and the navigation is
            // issued inline, exactly as before.
            var gate = AwaitExtensionGateAsync(wv, url, CancellationToken.None);
            if (!gate.IsCompletedSuccessfully)
            {
                CoreWebView2 core = wv.CoreWebView2;
                _ = NavigateWhenGateClosesAsync(gate, tab, core, url, intent);
                return;
            }
        }

        CommitExternalNavigation(wv, url);
    }

    /// <summary>
    /// Finishes a navigation that had to wait for the extension gate. The tab, the
    /// core and the intent are all checked again: the tab may have been closed, its
    /// control rebuilt, or a newer request issued while the gate was open. A newer
    /// request owns the tab, including a generated page, so a superseded url is
    /// dropped rather than written over it.
    /// </summary>
    private async Task NavigateWhenGateClosesAsync(
        Task gate,
        BrowserTab tab,
        CoreWebView2 core,
        string url,
        long intent)
    {
        try { await gate; }
        catch (Exception ex) { Trace.WriteLine($"TabEngine navigation gate failed: {ex.Message}"); }

        if (_disposed || !Tabs.Contains(tab)) return;
        if (!_webViews.TryGetValue(tab.Id, out var current) || current is null) return;
        if (!ReferenceEquals(current.CoreWebView2, core)) return;
        if (!IsLatestNavigationIntent(tab.Id, intent))
        {
            Trace.WriteLine($"TabEngine dropped a superseded deferred navigation for tab {tab.Id}: {url}");
            return;
        }
        if (!string.Equals(tab.Url, url, StringComparison.Ordinal))
        {
            Trace.WriteLine($"TabEngine dropped a deferred navigation for tab {tab.Id} because the tab moved on to {tab.Url}");
            return;
        }

        CommitExternalNavigation(current, url);
    }

    /// <summary>
    /// Numbers the navigation requests per tab. The newest one owns the tab, and every
    /// commit path checks that before writing a document into a core.
    /// </summary>
    private long BeginNavigationIntent(Guid tabId)
    {
        lock (_navigationIntents)
        {
            var next = _navigationIntents.TryGetValue(tabId, out var current) ? current + 1 : 1;
            _navigationIntents[tabId] = next;
            return next;
        }
    }

    private bool IsLatestNavigationIntent(Guid tabId, long intent)
    {
        lock (_navigationIntents) return _navigationIntents.TryGetValue(tabId, out var current) && current == intent;
    }

    private void ForgetNavigationIntent(Guid tabId)
    {
        lock (_navigationIntents) _navigationIntents.Remove(tabId);
    }

    private static void CommitExternalNavigation(dynamic wv, string url)
    {
        try { wv.CoreWebView2.Navigate(url); }
        catch (ArgumentException) { /* Invalid URL */ }
    }

    /// <summary>
    /// Serves the built in page named by the tab URL, if it names one.
    /// Returns true when the URL was claimed, including the pending native
    /// sentinel which is claimed by doing nothing so its core stays clean.
    /// </summary>
    private bool ServeBuiltInPage(BrowserTab tab)
    {
        switch (GetInternalPageKind(tab.Url))
        {
            case InternalPageKind.NewTab:
                NavigateToNewTab(tab);
                return true;
            case InternalPageKind.Settings:
                NavigateToSettings(tab, _settings);
                return true;
            case InternalPageKind.OneTab:
                NavigateToOneTab(tab, _oneTabStore.Load());
                return true;
            case InternalPageKind.History:
                NavigateToHistory(tab, _historyStore.Load());
                return true;
            case InternalPageKind.Extensions:
                NavigateToExtensions(tab);
                return true;
            case InternalPageKind.Downloads:
                NavigateToDownloads(tab);
                return true;
            case InternalPageKind.Onboarding:
                NavigateToOnboarding(tab);
                return true;
            case InternalPageKind.ReleaseNotes:
                NavigateToReleaseNotes(tab, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0");
                return true;
            case InternalPageKind.Focus:
                NavigateToFocus(tab);
                return true;
            case InternalPageKind.Health:
                NavigateToHealth(tab);
                return true;
            case InternalPageKind.PendingNative:
                return true;
            case InternalPageKind.None:
            default:
                return false;
        }
    }

    /// <summary>
    /// Delivers one generated HTML document to a tab and traces the delivery.
    /// The byte count plus the NavigationCompleted DOM probe below are what
    /// separate a missing document from a hidden one.
    /// </summary>
    private void NavigateToHtmlString(BrowserTab tab, string html, string pageName)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null)
        {
            Trace.WriteLine($"ServeBuiltIn page={pageName} skipped: no live core");
            return;
        }

        // A generated page is a document in its own right, so it takes a fresh intent.
        // That is what makes it supersede an external request still waiting behind the
        // registration gate instead of being overwritten by it. The intent is claimed
        // after the check above, because an intent that delivered nothing would leave a
        // startup request believing it had been superseded when it had not.
        var intent = BeginNavigationIntent(tab.Id);
        var kind = GetInternalPageKind(tab.Url);
        tab.ActiveInternalDocument = new InternalDocumentIdentity(tab.Id, kind, _ipcToken, intent, html);
        try
        {
            Trace.WriteLine($"ServeBuiltIn page={pageName} bytes={html.Length}");
            wv.CoreWebView2.NavigateToString(html);
        }
        catch (Exception ex)
        {
            tab.ActiveInternalDocument = null;
            Trace.WriteLine($"ServeBuiltIn page={pageName} failed: {ex}");
        }
    }

    public void NavigateToNewTab(BrowserTab tab)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { NavigateToHtmlString(tab, _pages.NewTabPage(_settings.NewTabShortcuts, _ipcToken, _settings.DefaultZoom, _settings.NewTabBackground, _settings.FaviconProxyEnabled), "newtab"); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToSettings(BrowserTab tab, BrowserSettings settings)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { NavigateToHtmlString(tab, _pages.SettingsPage(settings, _ipcToken), "settings"); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToOneTab(BrowserTab tab, List<OneTabGroup> groups)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { NavigateToHtmlString(tab, _pages.OneTabPage(_oneTabStore.Load(), _ipcToken), "onetab"); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToHistory(BrowserTab tab, List<Models.HistoryEntry> entries)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { NavigateToHtmlString(tab, _pages.HistoryPage(_historyStore.Load(), _ipcToken, _settings.FaviconProxyEnabled), "history"); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToDownloads(BrowserTab tab)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { NavigateToHtmlString(tab, _pages.DownloadsPage(_downloadStore.Items.ToList(), _ipcToken), "downloads"); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToOnboarding(BrowserTab tab)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { NavigateToHtmlString(tab, _pages.OnboardingPage(_settings, _ipcToken), "onboarding"); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToReleaseNotes(BrowserTab tab, string version)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try
        {
            var releases = ReleaseNotesProvider.GetAllReleases();
            var html = _pages.ReleaseNotesPage(version, releases, _ipcToken);
            NavigateToHtmlString(tab, html, "releasenotes");
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToFocus(BrowserTab tab)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try { NavigateToHtmlString(tab, _pages.FocusPage(tab.BlockedHost, _ipcToken), "focus"); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void NavigateToHealth(BrowserTab tab)
    {
        if (!_webViews.TryGetValue(tab.Id, out var wv) || wv.CoreWebView2 is null) return;
        try
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
            var health = _extensionManager.GetHealth();
            NavigateToHtmlString(tab, _pages.HealthPage(version, health, _ipcToken), "health");
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
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
        // A NavigateToString document has no URL to reload: asking the core to
        // reload re-requests about:blank and erases the page. Built in pages
        // re-serve through routing instead, which is also what rebuilds them
        // from the current stores.
        var tab = ActiveTab;
        if (tab is not null && IsServableBuiltInPage(tab.Url))
        {
            ServeBuiltInPage(tab);
            return;
        }
        GetActiveCore()?.Reload();
    }

    public void StopLoading() => GetActiveCore()?.Stop();

    public async Task HardReloadAsync()
    {
        var tab = ActiveTab;
        if (tab is not null && IsServableBuiltInPage(tab.Url))
        {
            ServeBuiltInPage(tab);
            return;
        }
        var core = GetActiveCore();
        if (core != null) await core.CallDevToolsProtocolMethodAsync("Page.reload", "{\"ignoreCache\":true}");
    }

    public void SetTabMuted(BrowserTab tab, bool muted)
    {
        tab.IsMuted = muted;
        var core = GetCoreForTab(tab.Id);
        if (core != null) core.IsMuted = muted;
    }

    public void HibernateTab(BrowserTab tab)
    {
        if (tab.IsActive || tab.IsHibernated) return;
        var urlBefore = tab.Url;
        _hibernationManager.HibernateTab(tab);
        // A built in page has no navigable source: the capture inside the
        // manager reads back about:blank, and the wake rebuild would then come
        // up as a dead blank document. Keep the internal URL so the wake
        // rebuild re-serves the page through routing.
        if (InternalUrls.IsInternal(urlBefore) && InternalUrls.IsDataOrBlank(tab.Url))
            tab.Url = urlBefore;
    }

    /// <summary>
    /// Hibernates every restored background tab at startup. This is deliberate and
    /// does not consult the hibernation setting or the pinned tab exemptions. The
    /// active tab stays loaded so the window opens with a usable page.
    /// </summary>
    public void HibernateRestoredTabs(BrowserTab activeTab)
    {
        if (_disposed) return;
        _hibernationManager.HibernateRestoredTabs(activeTab);
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

    public void PostMessageToSettingsTabs(string message)
    {
        foreach (var tab in Tabs.Where(t => t.Url == InternalUrls.Settings))
            _ipcBridge.PostMessageToActiveTab(tab.Id, message);
    }

    public void ApplyDefaultZoom()
    {
        foreach (var (id, view) in _webViews)
        {
            if (!IsServableBuiltInPage(GetTabUrl(id))) view.ZoomFactor = _settings.DefaultZoom / 100.0;
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

    /// <summary>
    /// Creates a WebView2 control for a tab and wires its events.
    ///
    /// Every step here runs on the dispatcher, so each one is timed and the total
    /// is written to the trace. This is what identifies which step owns a stall,
    /// rather than inferring it from a timer gap alone.
    /// </summary>
    private async Task CreateWebViewForTab(BrowserTab tab)
    {
        var overall = System.Diagnostics.Stopwatch.StartNew();
        var isInternal = tab.Url?.StartsWith("internal://") ?? true;

        dynamic wv = _webViewFactory.CreateWebViewControl(isInternal);
        _webViews[tab.Id] = wv;
        _controlHosts[tab.Id] = WebView2WindowBinding.HandleOf(_webViewHost is null ? null : Window.GetWindow(_webViewHost));
        tab.IsHibernated = false;
        tab.IsSleeping = false;
        // A drag that starts while the control is still being built cannot move it, so
        // the tab is marked in flight until the build settles one way or the other.
        _creatingTabs.Add(tab.Id);
        try
        {
            // Claimed here, before the control is reachable from Navigate, because
            // anything that arrives after this point is by definition a newer request for
            // this tab. Both places below that deliver a document check it again, so a
            // request that lands while this control is still being built supersedes the
            // startup work instead of being overwritten by it.
            var intent = BeginNavigationIntent(tab.Id);
            var ensureSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _webViewHost!.Children.Add((FrameworkElement)wv);
                await wv.EnsureCoreWebView2Async(_webViewFactory.Environment);
                var ensureMs = ensureSw.ElapsedMilliseconds;

                var applySw = System.Diagnostics.Stopwatch.StartNew();
                // A tab closed or moved to another window while the runtime was starting
                // must not be left behind holding a control nobody owns.
                if (_disposed || !Tabs.Contains(tab)) throw new OperationCanceledException();
                _webViewFactory.ConfigureCoreWebView(wv.CoreWebView2);
                var applyMs = applySw.ElapsedMilliseconds;

                var wireSw = System.Diagnostics.Stopwatch.StartNew();
                WireTabEvents(wv, tab);
                // Downloads and page dialogs live for the life of the control, not of
                // the window, so they are wired once and never rewired on a transfer.
                _ipcBridge.WireLifetimeEvents(wv, tab);
                var wireMs = wireSw.ElapsedMilliseconds;

                // Ad blocking belongs to the uBlock Origin extension, and nothing on
                // the browser side registers a request filter any more. The AdBlockFilter
                // that used to sit here added a WebResourceRequested handler for every
                // request in every context and answered hardcoded YouTube ad paths with
                // an empty 200, so it ran alongside uBlock and could empty a real
                // response before the extension or the page ever saw it.
                //
                // The registration itself is not kicked off here any more. It is awaited
                // below, immediately before the first external navigation this control
                // makes, so an internal page never waits for it.

                var injectSw = System.Diagnostics.Stopwatch.StartNew();
                await _contentScriptInjector.InjectAsync(wv.CoreWebView2, _settings, _telemetryToken);
                var injectMs = injectSw.ElapsedMilliseconds;

                _readinessCoordinator.TrySetResult(tab.Id, wv);

                System.Diagnostics.Trace.WriteLine(
                    $"TabEngine.CreateWebViewForTab total={overall.ElapsedMilliseconds}ms " +
                    $"ensure={ensureMs}ms configure={applyMs}ms wire={wireMs}ms " +
                    $"inject={injectMs}ms");
            }
            catch
            {
                // The control was registered above, so the failure path has to undo
                // that registration as well as removing and disposing the control.
                // Leaving the entry behind would make the tab look live with a
                // disposed WebView.
                AbandonControl(tab, wv);
                throw;
            }

            if (_disposed || !Tabs.Contains(tab))
            {
                AbandonControl(tab, wv);
                return;
            }

            // Registered above, before the awaits, so a failure part way through cannot
            // leave a live WebView that the engine does not know about.
            // Built in pages are served here, not by the caller: restore, the
            // hibernation wake and the transfer recycle all create through this
            // path, and none of them remembers a second navigation step.
            // Warm activations reuse the live control and never reach this
            // path, so a mere switch never reloads live page state.
            // PendingNative is claimed inside ServeBuiltInPage by doing
            // nothing, which keeps the core clean for the popup handoff.
            if (!IsLatestNavigationIntent(tab.Id, intent))
            {
                // A newer request already put its document into this control while the
                // runtime was starting. Serving the startup page again would replace it,
                // so nothing is delivered here. The control stays live because that
                // newer request is using it.
                Trace.WriteLine($"TabEngine first internal page dropped as superseded for tab {tab.Id}");
                return;
            }

            if (!ServeBuiltInPage(tab))
            {
                // First external navigation for this control. uBlock is registered and
                // reconciled to the current preference before the request is issued,
                // instead of racing it. An internal page and the pending native
                // sentinel never reach this line.
                //
                // The wait belongs to the engine, not to the activation that started
                // it. A tab switch cancels the activation so rapid clicks do not queue
                // up, and if that also cancelled this wait the request would go out
                // with the registration unproven, or the control would sit live and
                // never navigated while warm activation treated it as ready.
                var gateSw = System.Diagnostics.Stopwatch.StartNew();
                var gate = await AwaitExtensionGateAsync(wv, tab.Url, _shutdownCts.Token);
                System.Diagnostics.Trace.WriteLine(
                    $"TabEngine first external navigation tab={tab.Id} url={tab.Url} " +
                    $"gate={gateSw.ElapsedMilliseconds}ms {gate}");

                if (gate != ExtensionGateOutcome.Proceed)
                {
                    // Cancelled by the engine going away, so the control is dropped
                    // rather than left live with no document in it. A later activation
                    // builds a fresh one and waits again.
                    Trace.WriteLine($"TabEngine first external navigation suppressed for tab {tab.Id}");
                    AbandonControl(tab, wv);
                    return;
                }

                if (_disposed || !Tabs.Contains(tab))
                {
                    AbandonControl(tab, wv);
                    return;
                }

                if (!IsLatestNavigationIntent(tab.Id, intent))
                {
                    // A newer request owns this tab now. It may already have delivered a
                    // document into this same control, or its own continuation may still
                    // be waiting on the same gate. Issuing this one as well would either
                    // double navigate the newer url or re-serve a page that is already
                    // loaded, and an internal page that arrived in the meantime would be
                    // overwritten by the startup url. The control stays live because the
                    // newer request needs it. There is no newer intent in the pass above,
                    // so a control is never left with no document because of this.
                    Trace.WriteLine(
                        $"TabEngine first external navigation dropped as superseded for tab {tab.Id}: {tab.Url}");
                    return;
                }

                _webViewFactory.NavigateInitialUrl(wv, tab);
            }
        }
        finally { _creatingTabs.Remove(tab.Id); }
    }

    /// <summary>What the extension gate decided the caller should do next.</summary>
    public enum ExtensionGateOutcome
    {
        /// <summary>
        /// The profile is in a known state, or the pass failed or ran out of time. The
        /// page is loaded anyway: a page with ads beats no page, and the failure is
        /// traced.
        /// </summary>
        Proceed,

        /// <summary>
        /// The wait was cancelled, which is the engine going away. The requested
        /// navigation is not issued.
        /// </summary>
        Suppress
    }

    /// <summary>
    /// Waits for the profile to hold the extension state the user asked for, but only
    /// for a URL the page will really fetch.
    ///
    /// Cancellation is not a failure and not a success: it is the engine shutting
    /// down, and the caller must not treat it as permission to request the page with
    /// the registration unproven.
    /// </summary>
    private async Task<ExtensionGateOutcome> AwaitExtensionGateAsync(
        dynamic wv,
        string? url,
        CancellationToken cancellationToken)
    {
        if (!IsExternalRequestUrl(url)) return ExtensionGateOutcome.Proceed;

        try
        {
            var result = await _webViewFactory.EnsureExtensionsReadyAsync(wv, null, cancellationToken);
            if (!result.Succeeded)
                System.Diagnostics.Trace.WriteLine($"TabEngine extension gate unproven: {result}");
            if (result.Status == ExtensionStartupStatus.Cancelled)
            {
                System.Diagnostics.Trace.WriteLine("TabEngine extension gate cancelled; the navigation is not issued.");
                return ExtensionGateOutcome.Suppress;
            }
            return ExtensionGateOutcome.Proceed;
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Trace.WriteLine("TabEngine extension gate cancelled by the caller.");
            return ExtensionGateOutcome.Suppress;
        }
        catch (Exception ex)
        {
            // An unexpected failure is an explicit failure, which is the proceed path.
            System.Diagnostics.Trace.WriteLine($"TabEngine extension gate failed: {ex.Message}");
            return ExtensionGateOutcome.Proceed;
        }
    }

    /// <summary>
    /// True for a URL that leaves the machine, which is what the registration gate is
    /// for. Internal pages, the pending native sentinel, about:blank and the two host
    /// mapped asset folders are served from disk and are not gated.
    /// Pure logic, covered by unit tests.
    /// </summary>
    public static bool IsExternalRequestUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (InternalUrls.IsInternal(url)) return false;
        return !url.StartsWith("https://local.assets/", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://user.assets/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies the ad blocking preference to the live profile now, through the same
    /// coordinator the startup gate uses. Returns what actually happened, including
    /// NoCore when this window has no live WebView2 to run the profile calls on.
    ///
    /// The preference is recorded either way, so a window with nothing to apply it to
    /// still passes it to the next run.
    /// </summary>
    public async Task<ExtensionStartupResult> ApplyAdBlockPreferenceAsync(bool enabled)
    {
        var profileKey = _webViewFactory.ExtensionProfileKey;
        _extensionStartup.RecordPreference(profileKey, enabled);

        var core = FirstLiveCore();
        if (core is null)
        {
            var recorded = ExtensionStartupResult.NoCore(
                profileKey, enabled, "no live WebView2 in this window to apply the preference on");
            System.Diagnostics.Trace.WriteLine($"TabEngine ad block preference recorded only: {recorded}");
            return recorded;
        }

        var result = await _webViewFactory.ApplyPreferenceAsync(core, enabled);
        System.Diagnostics.Trace.WriteLine($"TabEngine ad block preference applied: {result}");
        return result;
    }

    /// <summary>The active tab's core when it has one, otherwise any live core in this window.</summary>
    private CoreWebView2? FirstLiveCore()
    {
        var active = ActiveTab;
        if (active is not null && TryGetCore(active.Id, out var activeCore)) return activeCore;

        foreach (var id in _webViews.Keys)
            if (TryGetCore(id, out var core)) return core;

        return null;
    }

    private bool TryGetCore(Guid tabId, out CoreWebView2? core)
    {
        core = null;
        if (!_webViews.TryGetValue(tabId, out var view) || view is null) return false;
        try { core = view.CoreWebView2 as CoreWebView2; }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"TabEngine core unavailable for {tabId}: {ex.Message}"); }
        return core is not null;
    }

    /// <summary>
    /// Drops a control the engine no longer owns: its handlers, its registry entries,
    /// its place in the host panel and the control itself.
    /// </summary>
    private void AbandonControl(BrowserTab tab, dynamic wv)
    {
        _readinessCoordinator.TryCancel(tab.Id);
        UnwireTabEvents(tab.Id);
        _webViews.Remove(tab.Id);
        _controlHosts.Remove(tab.Id);
        ForgetNavigationIntent(tab.Id);
        _webViewHost?.Children.Remove((FrameworkElement)wv);
        try { wv.Dispose(); } catch (Exception ex) { Trace.WriteLine(ex); }
    }

    private void WireInlinedContextMenu(CoreWebView2 core, BrowserTab tab, WebViewSubscriptions subscriptions)
    {
        EventHandler<CoreWebView2ContextMenuRequestedEventArgs> onContextMenuRequested = (object? sender, CoreWebView2ContextMenuRequestedEventArgs args) =>
        {
            // LinkUri, SourceUri and SelectionText throw COMException when read
            // without their Has* flag being set, so guard every string read.
            ContextMenuContext context;
            try
            {
                var target = args.ContextMenuTarget;
                var hasLink = target.HasLinkUri;
                var hasSelection = target.HasSelection;
                context = new ContextMenuContext
                {
                    CanGoBack = core.CanGoBack,
                    CanGoForward = core.CanGoForward,
                    HasLink = hasLink,
                    LinkUri = hasLink ? target.LinkUri ?? "" : "",
                    MediaKind = MapMediaKind(target.Kind),
                    SourceUri = target.HasSourceUri ? target.SourceUri ?? "" : "",
                    IsEditable = target.IsEditable,
                    HasSelection = hasSelection,
                    SelectionText = hasSelection ? target.SelectionText ?? "" : "",
                    ForceDarkMode = _settings.ForceDarkMode,
                    IsReaderAvailable = IsReaderAvailable?.Invoke(tab.Id) ?? false,
                    IsInReader = IsReaderActive?.Invoke(tab.Id) ?? false,
                };
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ContextMenu target extraction failed: {ex}");
                return;
            }

            var plan = ContextMenuPlanBuilder.Build(context, _settings.SearchEngine);

            var deferral = args.GetDeferral();
            args.Handled = true;

            _webViewHost!.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var cm = new ContextMenu { PlacementTarget = _webViewHost };

                    if (plan.NavigationRow != null)
                    {
                        var navPanel = new System.Windows.Controls.Primitives.UniformGrid
                        {
                            Rows = 1,
                            Columns = 3,
                            Margin = new Thickness(4)
                        };

                        var backBtn = new Button { Style = (Style)Application.Current.Resources["ContextMenuNavButton"], Content = "\uE72B", IsEnabled = plan.NavigationRow.CanGoBack };
                        backBtn.Click += (s, e) => { cm.IsOpen = false; GoBack(); };
                        navPanel.Children.Add(backBtn);

                        var fwdBtn = new Button { Style = (Style)Application.Current.Resources["ContextMenuNavButton"], Content = "\uE72A", IsEnabled = plan.NavigationRow.CanGoForward };
                        fwdBtn.Click += (s, e) => { cm.IsOpen = false; GoForward(); };
                        navPanel.Children.Add(fwdBtn);

                        var refBtn = new Button { Style = (Style)Application.Current.Resources["ContextMenuNavButton"], Content = "\uE72C" };
                        refBtn.Click += (s, e) => { cm.IsOpen = false; Reload(); };
                        navPanel.Children.Add(refBtn);

                        var navItem = new MenuItem 
                        { 
                            Header = navPanel,
                            Template = (System.Windows.Controls.ControlTemplate)System.Windows.Application.Current.Resources["ContextMenuNavRowTemplate"]
                        };
                        cm.Items.Add(navItem);
                        cm.Items.Add(MenuSeparator.Create());
                    }

                    foreach (var item in plan.Items)
                    {
                        switch (item)
                        {
                            case ContextMenuItemSpec.Separator:
                                cm.Items.Add(MenuSeparator.Create());
                                break;
                            case ContextMenuItemSpec.Command command:
                            {
                                var menuItem = new MenuItem
                                {
                                    Header = command.Header,
                                    InputGestureText = command.GestureText,
                                    IsEnabled = command.IsEnabled,
                                    Tag = command,
                                };
                                menuItem.Click += (s, e) =>
                                {
                                    cm.IsOpen = false;
                                    ExecuteContextMenuCommand(core, (ContextMenuItemSpec.Command)((MenuItem)s!).Tag!);
                                };
                                cm.Items.Add(menuItem);
                                break;
                            }
                        }
                    }

                    cm.IsOpen = true;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Context menu failed to open: {ex}");
                }
                finally
                {
                    deferral.Complete();
                }
            });
        };
    }

    private void ExecuteContextMenuCommand(CoreWebView2 core, ContextMenuItemSpec.Command command)
    {
        switch (command.Id)
        {
            case "open-link-new-tab":
            case "open-image-new-tab":
            case "search-selection":
                _ = _dispatcher.InvokeAsync(async () =>
                {
                    try { var newTab = CreateTab(command.Payload); await ActivateAsync(newTab); }
                    catch (Exception ex) { Trace.WriteLine(ex); }
                });
                break;

            case "copy-link":
            case "copy-image-url":
                try { System.Windows.Clipboard.SetText(command.Payload); }
                catch (Exception ex) { Trace.WriteLine(ex); }
                break;

            case "save-image":
                SaveImage(command.Payload);
                break;

            case "back":
                if (core.CanGoBack) core.GoBack();
                break;

            case "forward":
                if (core.CanGoForward) core.GoForward();
                break;

            case "reload":
                Reload();
                break;

            case "toggle-reader":
                if (ActiveTab != null)
                {
                    _ = ToggleReaderAsync?.Invoke(ActiveTab.Id);
                }
                break;

            case "launch-tc-lens":
                _ = LaunchTCLensAsync?.Invoke();
                break;

            case "find-in-page":
                _ = FindInPageAsync();
                break;

            case "select-all":
                core.ExecuteScriptAsync("document.execCommand('selectAll')");
                break;

            case "undo":
                core.ExecuteScriptAsync("document.execCommand('undo')");
                break;

            case "redo":
                core.ExecuteScriptAsync("document.execCommand('redo')");
                break;

            case "cut":
                core.ExecuteScriptAsync("document.execCommand('cut')");
                break;

            case "copy":
                core.ExecuteScriptAsync("document.execCommand('copy')");
                break;

            case "paste":
                core.ExecuteScriptAsync("navigator.clipboard.readText().then(t => document.execCommand('insertText', false, t))");
                break;

            case "print":
                try { core.ShowPrintUI(); }
                catch (Exception ex) { Trace.WriteLine(ex); }
                break;

            case "view-source":
                try { core.Navigate("view-source:" + core.Source); }
                catch (Exception ex) { Trace.WriteLine(ex); }
                break;

            case "inspect":
                core.OpenDevToolsWindow();
                break;

            case "toggle-dark-mode":
                _settings.ForceDarkMode = !_settings.ForceDarkMode;
                break;
        }
    }

    private void SaveImage(string sourceUri)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = GuessImageFileName(sourceUri),
                Filter = "Image|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp;*.avif;*.svg|All files|*.*",
            };
            if (dialog.ShowDialog() != true) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var http = new System.Net.Http.HttpClient();
                    var bytes = await http.GetByteArrayAsync(sourceUri);
                    await System.IO.File.WriteAllBytesAsync(dialog.FileName, bytes);
                }
                catch (Exception ex) { Trace.WriteLine($"Image save failed: {ex}"); }
            });
        }
        catch (Exception ex) { Trace.WriteLine($"Image save dialog failed: {ex}"); }
    }

    private static string GuessImageFileName(string sourceUri)
    {
        try
        {
            var path = new Uri(sourceUri).AbsolutePath;
            var name = System.IO.Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(name) ? "image" : name;
        }
        catch { return "image"; }
    }

    private static ContextMenuMediaKind MapMediaKind(CoreWebView2ContextMenuTargetKind kind) => kind switch
    {
        CoreWebView2ContextMenuTargetKind.Image => ContextMenuMediaKind.Image,
        CoreWebView2ContextMenuTargetKind.Video => ContextMenuMediaKind.Video,
        CoreWebView2ContextMenuTargetKind.Audio => ContextMenuMediaKind.Audio,
        _ => ContextMenuMediaKind.Page,
    };

    /// <summary>
    /// Re-injects the YouTube Unhook script into all active YouTube tabs.
    /// Called when unhook settings change for live-reload without page refresh.
    /// The script detects an already-loaded instance and updates its config in
    /// place instead of stacking new observers. When unhook is disabled, clears
    /// the config and removes the applied classes instead.
    ///
    /// The registered document-start bootstrap is replaced on every live core, not
    /// only the YouTube ones. That registration is frozen when the control is built,
    /// so a core that has not reached YouTube yet, or was built before the setting
    /// changed, would otherwise run the old text on the next document it creates.
    /// </summary>
    public async Task ReInjectUnhookAsync()
    {
        const string cleanup =
            "window.__STRIDE_UNHOOK = null; " +
            "try { localStorage.removeItem('__stride_unhook'); } catch (e) {} " +
            "var rc = document.documentElement.classList; " +
            "for (var i = rc.length - 1; i >= 0; i--) { " +
            "if (rc.item(i).indexOf('stride-unhook-') === 0) rc.remove(rc.item(i)); }";

        var script = _youtubeUnhook.GetScript(_settings, forceConfig: true);

        foreach (var (_, wv) in _webViews)
        {
            if (wv.CoreWebView2 is null) continue;

            // Every core, on any host. The registration is what a future document runs.
            await _contentScriptInjector.UpdateYouTubeScriptAsync(wv.CoreWebView2, _settings);

            if (!IsYouTubeHost(wv.Source?.Host)) continue;

            try
            {
                if (!string.IsNullOrEmpty(script))
                    await wv.CoreWebView2.ExecuteScriptAsync(script);
                else
                    await wv.CoreWebView2.ExecuteScriptAsync(cleanup);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Unhook re-injection failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Re-injects the YouTube Enhancer script into all active YouTube tabs.
    /// Called when enhancer settings change for live-reload without page refresh.
    /// The script detects an already-loaded instance and re-applies with the new
    /// config instead of stacking listeners. When disabled, clears the config
    /// and undoes applied state.
    /// </summary>
    public async Task ReInjectEnhancerAsync()
    {
        const string cleanup =
            "window.__STRIDE_YT_CONFIG = { enabled: false }; " +
            "try { localStorage.removeItem('__stride_yt_enhancer'); } catch (e) {} " +
            "document.querySelectorAll('video').forEach(function(v) { v.loop = false; });";

        var script = _youtubeEnhancer.GetScript(_settings, forceConfig: true);

        foreach (var (_, wv) in _webViews)
        {
            if (wv.CoreWebView2 is null) continue;
            if (!IsYouTubeHost(wv.Source?.Host)) continue;

            try
            {
                if (!string.IsNullOrEmpty(script))
                    await wv.CoreWebView2.ExecuteScriptAsync(script);
                else
                    await wv.CoreWebView2.ExecuteScriptAsync(cleanup);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Enhancer re-injection failed: {ex.Message}");
            }
        }
    }

    /// <summary>Exact-host YouTube check; substring matching would also hit lookalike domains.</summary>
    private static bool IsYouTubeHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        return host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
    }


    // ──── WebView Event Wiring ────

    private void WireNavigationEvents(dynamic wv, BrowserTab tab, WebViewSubscriptions subscriptions)
    {
        CoreWebView2 core = wv.CoreWebView2;
        EventHandler<object> onHistoryChanged = (_, _) =>
        {
            if (_webViews.ContainsKey(tab.Id)) UpdateTabFromWebView(wv, tab, recordHistory: false);
        };
        core.HistoryChanged += onHistoryChanged;
        subscriptions.Add(() => core.HistoryChanged -= onHistoryChanged);
        EventHandler<CoreWebView2NavigationStartingEventArgs> onNavigationStarting = (_, e) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;

            if (tab.ActiveInternalDocument is { } document && !document.TryStartNavigation(e.Uri))
            {
                tab.ActiveInternalDocument = null;
            }

            // Intercept native PDF navigations and route to PDF.js
            try
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && 
                    uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && 
                    !e.Uri.StartsWith("http://local.assets/pdfjs", StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                    var encodedUrl = Uri.EscapeDataString(e.Uri);
                    core.Navigate($"http://local.assets/pdfjs/web/viewer.html?file={encodedUrl}");
                    return;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"PDF interception failed: {ex.Message}");
            }

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

        core.NavigationStarting += onNavigationStarting;
        subscriptions.Add(() => core.NavigationStarting -= onNavigationStarting);

        EventHandler<CoreWebView2NavigationCompletedEventArgs> onNavigationCompleted = (_, e) =>
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
                        try { wv.CoreWebView2.NavigateToString(_pages.ErrorPage(tab.Url, errorMsg)); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                        return;
                    }

                    tab.IsLoading = false;
                    LoadingStateChanged?.Invoke(tab, false);
                    UpdateTabFromWebView(wv, tab);
                    TraceInternalDocument(tab, wv);
                }
                catch (Exception ex) { Trace.WriteLine($"NavigationCompleted error: {ex.Message}"); }
            });
        };
        core.NavigationCompleted += onNavigationCompleted;
        subscriptions.Add(() => core.NavigationCompleted -= onNavigationCompleted);
    }

    private void ApplyNavigationBackground(dynamic wv, string? url)
    {
        if (url is null) return;
        var isInternal = url.StartsWith("internal://") || url.StartsWith("https://local.assets/") || url.StartsWith("https://user.assets/");
        if (wv is Microsoft.Web.WebView2.Wpf.WebView2 std3) std3.DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? WebViewFactory.DarkBackground : System.Drawing.Color.White);
        else if (wv is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl comp3) comp3.DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? WebViewFactory.DarkBackground : System.Drawing.Color.White);
    }

    private void WireTitleAndSourceEvents(dynamic wv, BrowserTab tab, WebViewSubscriptions subscriptions)
    {
        CoreWebView2 core = wv.CoreWebView2;
        EventHandler<object> onDocumentTitleChanged = (_, _) =>
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
        core.DocumentTitleChanged += onDocumentTitleChanged;
        subscriptions.Add(() => core.DocumentTitleChanged -= onDocumentTitleChanged);

        EventHandler<CoreWebView2SourceChangedEventArgs> onSourceChanged = (_, _) =>
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
        core.SourceChanged += onSourceChanged;
        subscriptions.Add(() => core.SourceChanged -= onSourceChanged);

        // Use WebView2's native favicon API (like Chrome/Firefox)
        // instead of relying solely on a 3rd-party service
        EventHandler<object> onFaviconChanged = (_, _) =>
        {
            if (!_webViews.ContainsKey(tab.Id)) return;
            string source = (string)wv.Source?.ToString() ?? "";
            _ = _faviconLoader.HandleFaviconChangedAsync(core, source).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result is not null)
                    _dispatcher.Invoke(() => { tab.Favicon = t.Result; TabStateChanged?.Invoke(tab); });
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        };
        core.FaviconChanged += onFaviconChanged;
        subscriptions.Add(() => core.FaviconChanged -= onFaviconChanged);

        core.IsMuted = tab.IsMuted;
        EventHandler<object> onPlayingAudioChanged = (_, _) =>
        {
            if (_webViews.ContainsKey(tab.Id)) tab.IsAudioPlaying = core.IsDocumentPlayingAudio;
        };
        core.IsDocumentPlayingAudioChanged += onPlayingAudioChanged;
        subscriptions.Add(() => core.IsDocumentPlayingAudioChanged -= onPlayingAudioChanged);
        EventHandler<object> onMutedChanged = (_, _) =>
        {
            if (_webViews.ContainsKey(tab.Id)) tab.IsMuted = core.IsMuted;
        };
        core.IsMutedChanged += onMutedChanged;
        subscriptions.Add(() => core.IsMutedChanged -= onMutedChanged);
    }

    public event Action<bool>? FullScreenChanged;



    /// <summary>Wires process failure handling for a tab WebView and marks crashed tabs hibernated.</summary>
    private void HandleProcessFailure(dynamic wv, BrowserTab tab, WebViewSubscriptions subscriptions)
    {
        CoreWebView2 core = wv.CoreWebView2;
        EventHandler<CoreWebView2ProcessFailedEventArgs> onProcessFailed = (_, e) =>
        {
            try { Trace.WriteLine($"ProcessFailed kind={e.ProcessFailedKind} tab={tab.Title}"); Trace.Flush(); }
            catch { }
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
        core.ProcessFailed += onProcessFailed;
        subscriptions.Add(() => core.ProcessFailed -= onProcessFailed);
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
        foreach (var id in _webViews.Keys.ToArray()) TeardownWebView(id);
    }

    private void OnBrowserProcessExited() => _dispatcher.Invoke(HandleBrowserProcessDeath);

    /// <summary>
    /// Traces what a completed navigation actually left in the renderer for a
    /// built in page: the core source plus the live DOM length. Delivery
    /// tracing in NavigateToHtmlString records the input bytes; this records
    /// the output document, which is what separates a missing document from
    /// a correctly loaded page hidden by activation or layering.
    /// </summary>
    private void TraceInternalDocument(BrowserTab tab, dynamic wv)
    {
        if (!IsServableBuiltInPage(tab.Url)) return;
        try
        {
            string source = (string)wv.Source?.ToString() ?? "";
            Trace.WriteLine($"InternalNavCompleted page={tab.Url} source={source}");
            var core = wv.CoreWebView2;
            if (core is null) return;
            _ = ((Task<string>)core.ExecuteScriptAsync(
                @"(function(){try{var d=document;var b=d.body;return JSON.stringify({len:d.documentElement?d.documentElement.outerHTML.length:-1,bodyKids:b?b.childElementCount:-1,state:d.visibilityState,ready:d.readyState,w:window.innerWidth,h:window.innerHeight,focus:d.hasFocus(),title:d.title,host:!!(window.chrome&&window.chrome.webview)});}catch(e){return 'probe-throw:'+e;}})()"))
                .ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                        Trace.WriteLine($"InternalDom page={tab.Url} doc={t.Result}");
                    else
                        Trace.WriteLine($"InternalDom page={tab.Url} failed: {t.Exception?.GetBaseException().Message}");
                }, TaskContinuationOptions.ExecuteSynchronously);
        }
        catch (Exception ex) { Trace.WriteLine($"InternalDom probe failed page={tab.Url}: {ex.Message}"); }
    }

    private void UpdateTabFromWebView(dynamic wv, BrowserTab tab, bool recordHistory = true)
    {        string source = (string)wv.Source?.ToString() ?? "";
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

        if (recordHistory) _historyStore.Add(source, tab.Title);
    }

    private static void HandleInternalPageTitles(BrowserTab tab)
    {
        tab.Title = tab.Url switch
        {
            InternalUrls.NewTab => "New Tab",
            InternalUrls.Settings => "Settings",
            InternalUrls.OneTab => "OneTab",
            InternalUrls.Extensions => "Extensions",
            InternalUrls.Downloads => "Downloads",
            InternalUrls.Focus => "Focus Locked",
            InternalUrls.Health => "Health",
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
        if (activeTab != ActiveTab) return;
        foreach (var (id, wv) in _webViews)
        {
            var isActive = id == activeTab.Id;

            // Visibility first, unconditional, for every control. A failure
            // below must never leave a later view in a stale state.
            // Use Hidden for CompositionControl to prevent flicker.
            // But HwndHost (standard WebView2) ignores Hidden and keeps rendering the HWND, so we MUST use Collapsed.
            // Check the actual type of the control, because the setting might have been toggled at runtime before a restart.
            bool isHwndHost = wv is Microsoft.Web.WebView2.Wpf.WebView2;
            ((FrameworkElement)wv).Visibility = isActive
                ? Visibility.Visible
                : (isHwndHost ? Visibility.Collapsed : Visibility.Hidden);
            Panel.SetZIndex((FrameworkElement)wv, isActive ? 1 : 0);

            if (wv.CoreWebView2 is null) continue;

            // Best effort tuning only. A moved or stale core can reject this
            // optional call, and the loop still positions every view.
            var level = isActive
                ? CoreWebView2MemoryUsageTargetLevel.Normal
                : CoreWebView2MemoryUsageTargetLevel.Low;
            CoreWebView2 core = wv.CoreWebView2;
            TryApplyOptionalCoreSetting(() => core.MemoryUsageTargetLevel = level, "MemoryUsageTargetLevel", id);
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

        if (!_settings.TabSleepEnabled || (tab.IsPinned && !_settings.AllowPinnedTabsToSleep)) return;
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

        if (generation != _previewGeneration || _previewOriginTabId != tabId ||
            !_settings.TabSleepEnabled || (tab.IsPinned && !_settings.AllowPinnedTabsToSleep))
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
        _readinessCoordinator.TryCancel(tabId);
        if (!_webViews.TryGetValue(tabId, out var wv)) return;
        UnwireTabEvents(tabId);
        _webViewHost?.Children.Remove((FrameworkElement)wv);
        try { wv.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        _webViews.Remove(tabId);
        _controlHosts.Remove(tabId);
        // A tab with no control has no pending navigation to finish either.
        ForgetNavigationIntent(tabId);
        var tab = Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab != null) tab.IsAudioPlaying = false;
    }

    /// <summary>
    /// Rebuilds the WebView of every tab whose control is still bound to a window that is
    /// about to disappear. A torn-off tab keeps its page across a window close only when
    /// the SDK could follow the control to its new window; when it could not, the control
    /// dies with the old window, so the tab is rebuilt and reloads instead of going blank.
    /// </summary>
    public async Task RecycleTabsHostedBy(IntPtr deadWindow)
    {
        if (_disposed || deadWindow == IntPtr.Zero) return;
        var affected = _webViews.Keys
            .Where(id => _controlHosts.GetValueOrDefault(id) == deadWindow && Tabs.Any(t => t.Id == id))
            .ToArray();
        foreach (var id in affected)
        {
            var tab = Tabs.FirstOrDefault(t => t.Id == id);
            if (tab is null) continue;
            TeardownWebView(id);
            tab.IsHibernated = true;
            tab.IsSleeping = false;
        }
        if (affected.Length == 0) return;
        if (ActiveTab is { } active && affected.Contains(active.Id))
            await ActivateAsync(active);
    }

    public void Shutdown() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        _webViewFactory.BrowserProcessExited -= OnBrowserProcessExited;
        _hibernationManager.Detach();
        _activationCts?.Cancel();
        // The engine is going away, so any first navigation wait in flight is cut
        // short. That wait then suppresses its navigation and drops its control
        // instead of issuing a request with the registration unproven.
        try { _shutdownCts.Cancel(); } catch (ObjectDisposedException) { /* already gone */ }
        foreach (var id in _webViews.Keys.ToArray()) TeardownWebView(id);
    }
}


