using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Interop;

namespace StrideBrowser.Engine;

/// <summary>
/// Manages memory and resource usage by hibernating inactive tabs
/// and suspending background WebViews.
/// </summary>
public sealed class TabHibernationManager
{
    private const int BaseHibernateMinutes = 5;

    private readonly IDownloadStore _downloadStore;
    private readonly BrowserSettings _settings;
    private readonly DispatcherTimer _timer;

    private Func<IReadOnlyCollection<BrowserTab>>? _getTabs;
    private Func<IReadOnlyDictionary<Guid, dynamic>>? _getWebViews;
    private Action<Guid>? _teardownWebView;
    private Func<Guid, bool>? _isPreviewOrigin;
    private int _maxLiveWebViews = 10;
    private int _suspensionGeneration;
    private Func<Guid, bool>? _isDownloadProtected;
    // Weak keys let a closed control go away even if its manager survives. Sharing the
    // table also coalesces a native suspension while a tab moves between windows.
    private static readonly ConditionalWeakTable<object, object> PendingSuspensions = new();
    private static readonly object PendingSuspensionLock = new();

    /// <summary>Initializes a new instance of the TabHibernationManager.</summary>
    public TabHibernationManager(IDownloadStore downloadStore, BrowserSettings settings)
    {
        _downloadStore = downloadStore;
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => HibernateInactiveTabs();
    }

    /// <summary>Attaches the manager to the tab collection and starts the hibernation timer.</summary>
    public void Attach(
        Func<IReadOnlyCollection<BrowserTab>> getTabs,
        Func<IReadOnlyDictionary<Guid, dynamic>> getWebViews,
        Action<Guid> teardownWebView,
        int maxLiveWebViews)
    {
        _suspensionGeneration++;
        _getTabs = getTabs;
        _getWebViews = getWebViews;
        _teardownWebView = teardownWebView;
        _maxLiveWebViews = maxLiveWebViews;
        _timer.Start();
    }

    public void SetPreviewOriginCheck(Func<Guid, bool> isPreviewOrigin) => _isPreviewOrigin = isPreviewOrigin;

    public void SetDownloadProtectionCheck(Func<Guid, bool> isDownloadProtected) => _isDownloadProtected = isDownloadProtected;

    /// <summary>Detaches the manager and stops the timer.</summary>
    public void Detach()
    {
        _suspensionGeneration++;
        _timer.Stop();
        _getTabs = null;
        _getWebViews = null;
        _teardownWebView = null;
        _isPreviewOrigin = null;
        _isDownloadProtected = null;
    }

    /// <summary>Suspends background tabs to save CPU by setting low memory target and calling TrySuspendAsync. Only tabs where suspend succeeds are marked sleeping.</summary>
    public void SuspendBackgroundTabs(BrowserTab activeTab)
    {
        if (!_settings.TabSleepEnabled) return;
        if (_getWebViews is null || _getTabs is null) return;

        // Clear sleeping flag for the active tab on resume.
        activeTab.IsSleeping = false;

        var generation = _suspensionGeneration;
        var tabsById = _getTabs().ToDictionary(t => t.Id);
        foreach (var (id, wv) in _getWebViews())
        {
            if (id == activeTab.Id) continue;
            if (!tabsById.TryGetValue(id, out var tab) || tab.IsActive || tab.IsHibernated) continue;
            if (tab.IsPinned && !_settings.AllowPinnedTabsToSleep) continue;
            if (tab.IsSleeping || IsDownloadProtected(tab.Id)) continue;

            dynamic core;
            try { core = wv.CoreWebView2; }
            catch (Exception ex)
            {
                Trace.WriteLine($"SuspendBackgroundTabs skipped disposed tab {id}: {ex.Message}");
                continue;
            }
            if (core is null) continue;

            var coreIdentity = (object)core;
            var request = new object();
            lock (PendingSuspensionLock)
            {
                if (PendingSuspensions.TryGetValue(coreIdentity, out _)) continue;
                PendingSuspensions.Add(coreIdentity, request);
            }
            try
            {
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SuspendBackgroundTabs MemoryUsageTargetLevel failed for tab {id}: {ex.Message}");
                CompleteSuspension(coreIdentity, request);
                continue;
            }

            _ = TrySuspendSafeAsync(coreIdentity, id, tab, generation, request);
        }
    }

    /// <summary>Resumes live WebViews and clears their sleeping flags.</summary>
    public void ClearSleepingState()
    {
        _suspensionGeneration++;
        if (_getTabs is null) return;
        foreach (var tab in _getTabs())
            ResumeTab(tab);
    }

    /// <summary>Applies sleep preferences to the live tabs in this window.</summary>
    public void ApplySleepSettings()
    {
        if (!_settings.TabSleepEnabled)
        {
            ClearSleepingState();
            return;
        }
        if (_getTabs is null) return;
        var tabs = _getTabs();
        foreach (var tab in tabs.Where(t => t.IsPinned && !_settings.AllowPinnedTabsToSleep))
            ResumeTab(tab);
        var activeTab = tabs.FirstOrDefault(t => t.IsActive);
        if (activeTab is not null) SuspendBackgroundTabs(activeTab);
    }

    private void ResumeTab(BrowserTab tab)
    {
        if (_getWebViews?.Invoke().TryGetValue(tab.Id, out var wv) == true)
        {
            try
            {
                var core = wv.CoreWebView2;
                if (core is not null)
                {
                    core.Resume();
                    core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                }
            }
            catch (Exception ex) { Trace.WriteLine($"Resume failed for tab {tab.Id}: {ex.Message}"); }
        }
        tab.IsSleeping = false;
    }

    private static void CompleteSuspension(object core, object request)
    {
        lock (PendingSuspensionLock)
        {
            if (PendingSuspensions.TryGetValue(core, out var current) && ReferenceEquals(current, request))
                PendingSuspensions.Remove(core);
        }
    }

    private bool StillOwnsCore(Guid tabId, object core)
    {
        try
        {
            return _getWebViews?.Invoke().TryGetValue(tabId, out var view) == true &&
                ReferenceEquals((object?)view.CoreWebView2, core);
        }
        catch { return false; }
    }

    /// <summary>Marks sleep only after native suspension succeeds and ownership still matches.</summary>
    private async Task TrySuspendSafeAsync(object coreIdentity, Guid tabId, BrowserTab tab, int generation, object request)
    {
        dynamic core = coreIdentity;
        try
        {
            bool success;
            try { success = await core.TrySuspendAsync(); }
            catch (Exception ex)
            {
                Trace.WriteLine($"TrySuspendAsync failed for tab {tabId}: {ex.Message}");
                return;
            }
            if (!success) return;
            if (tab.IsActive || tab.IsHibernated || _getTabs?.Invoke().Contains(tab) != true ||
                !StillOwnsCore(tabId, coreIdentity) || generation != _suspensionGeneration ||
                !_settings.TabSleepEnabled || (tab.IsPinned && !_settings.AllowPinnedTabsToSleep) || IsDownloadProtected(tabId))
            {
                // Selection, transfer, teardown and settings can change during native work.
                try
                {
                    core.Resume();
                    core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                }
                catch (Exception ex) { Trace.WriteLine($"Resume after pending suspension failed: {ex.Message}"); }
                tab.IsSleeping = false;
                return;
            }
            tab.IsSleeping = true;
        }
        finally { CompleteSuspension(coreIdentity, request); }
    }

    /// <summary>Evicts least recently used tabs when the live WebView count exceeds the limit.</summary>
    public void EvictExcessWebViews(BrowserTab activeTab)
    {
        if (!_settings.TabHibernationEnabled) return;
        if (_getTabs is null || _getWebViews is null) return;
        
        var webViews = _getWebViews();
        if (webViews.Count <= _maxLiveWebViews) return;

        var candidates = _getTabs()
            .Where(t => t.Id != activeTab.Id && !t.IsHibernated && webViews.ContainsKey(t.Id))
            .Where(t => IsTabSafeToHibernate(t, webViews))
            .OrderBy(t => t.LastActiveTime)
            .ToList();

        var toEvict = webViews.Count - _maxLiveWebViews;
        foreach (var tab in candidates.Take(toEvict))
        {
            Trace.WriteLine($"Evicting WebView for '{tab.Title}' (last active: {tab.LastActiveTime:HH:mm:ss})");
            HibernateTab(tab, webViews);
        }
    }

    /// <summary>
    /// Hibernates every tab except the active one after a session restore. Startup is
    /// deliberate: it ignores the hibernation setting and both pinned tab exemptions
    /// so a restored window starts with every background tab unloaded. Tabs that
    /// never received a WebView still get the hibernated state, because the strip
    /// and the wake path both use that flag.
    /// </summary>
    public void HibernateRestoredTabs(BrowserTab activeTab)
    {
        if (_getTabs is null) return;

        var webViews = _getWebViews?.Invoke();
        var hibernated = 0;
        foreach (var tab in _getTabs().ToList())
        {
            if (tab.Id == activeTab.Id || tab.IsHibernated) continue;

            if (webViews is not null && webViews.ContainsKey(tab.Id) && _teardownWebView is not null)
            {
                HibernateTab(tab, webViews);
            }
            else
            {
                tab.IsHibernated = true;
                tab.IsSleeping = false;
            }
            hibernated++;
        }

        Trace.WriteLine(
            $"HibernateRestoredTabs hibernated={hibernated} active='{activeTab.Title}'");
    }

    public static BrowserTab[] GetManualHibernateTargets(IEnumerable<BrowserTab> tabs, BrowserTab contextTab)
    {
        var snapshot = tabs.ToArray();
        if (!snapshot.Contains(contextTab)) return [];
        return snapshot.Where(tab => !tab.IsActive && !tab.IsHibernated &&
            (contextTab.IsActive || tab == contextTab)).ToArray();
    }

    /// <summary>Hibernates a single tab by tearing down its WebView.</summary>
    public void HibernateTab(BrowserTab tab)
    {
        if (_getWebViews is null) return;
        HibernateTab(tab, _getWebViews());
    }

    /// <summary>Tears down the WebView for a tab and marks it hibernated.</summary>
    private void HibernateTab(BrowserTab tab, IReadOnlyDictionary<Guid, dynamic> webViews)
    {
        if (_teardownWebView is null) return;

        if (webViews.TryGetValue(tab.Id, out var wv))
        {
            var src = wv.Source?.ToString();
            if (!string.IsNullOrEmpty(src)) tab.Url = src;
        }
        
        _teardownWebView(tab.Id);
        tab.IsHibernated = true;
        tab.IsSleeping = false;
    }

    /// <summary>Hibernates tabs that have been inactive past the adaptive cutoff.</summary>
    private void HibernateInactiveTabs()
    {
        if (!_settings.TabHibernationEnabled) return;
        if (_getTabs is null || _getWebViews is null) return;

        var tabs = _getTabs();
        var webViews = _getWebViews();

        var hibernateMinutes = tabs.Count switch
        {
            <= 5 => BaseHibernateMinutes,
            <= 10 => 3,
            <= 15 => 2,
            _ => 1
        };

        var cutoff = DateTime.UtcNow.AddMinutes(-hibernateMinutes);

        foreach (var tab in tabs.ToList())
        {
            if (!IsTabSafeToHibernate(tab, webViews)) continue;
            if (tab.LastActiveTime > cutoff) continue;

            HibernateTab(tab, webViews);
        }
    }

    private bool IsDownloadProtected(Guid tabId)
    {
        try
        {
            return _isDownloadProtected?.Invoke(tabId) ?? _downloadStore.Items.Any(d =>
                d.State == DownloadState.InProgress || d.State == DownloadState.Paused && d.CanResumeNative);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Download protection check failed: {ex.Message}");
            return true;
        }
    }

    /// <summary>Checks if a tab can be hibernated without losing important state.</summary>
    private bool IsTabSafeToHibernate(BrowserTab tab, IReadOnlyDictionary<Guid, dynamic> webViews)
    {
        if (tab.IsActive || tab.IsHibernated) return false;
        if (_isPreviewOrigin != null && _isPreviewOrigin(tab.Id)) return false;
        if (tab.IsPinned && !_settings.AllowPinnedTabsToHibernate) return false;
        if (InternalUrls.IsInternal(tab.Url)) return false;

        // Check if tab is playing audio
        if (webViews.TryGetValue(tab.Id, out var wv))
        {
            try
            {
                if (wv.CoreWebView2 != null && wv.CoreWebView2.IsDocumentPlayingAudio)
                    return false;
            }
            catch (Exception ex) { Trace.WriteLine(ex); }
        }

        if (IsDownloadProtected(tab.Id)) return false;

        return true;
    }
}
