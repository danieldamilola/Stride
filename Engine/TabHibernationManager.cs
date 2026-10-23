using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using UmberBrowser.Models;
using UmberBrowser.Services;
using UmberBrowser.Interop;

namespace UmberBrowser.Engine;

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

    /// <summary>
    /// Guards the two suspension bookkeeping maps. The COM calls themselves are
    /// made on the UI thread; the maps are locked because a completion can arrive
    /// on a pool thread when the manager is driven without a dispatcher context,
    /// which is how the controlled probes and tests use it.
    /// </summary>
    private readonly object _stateLock = new();
    private readonly Dictionary<Guid, PendingSuspension> _pendingSuspensions = new();
    private readonly Dictionary<Guid, WeakReference<object>> _confirmedSleepingCores = new();

    /// <summary>One in-flight suspend per tab and core, so repeated activations do not queue more.</summary>
    private sealed record PendingSuspension(object Core, Task Task);

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
        _getTabs = getTabs;
        _getWebViews = getWebViews;
        _teardownWebView = teardownWebView;
        _maxLiveWebViews = maxLiveWebViews;
        _timer.Start();
    }

    public void SetPreviewOriginCheck(Func<Guid, bool> isPreviewOrigin) => _isPreviewOrigin = isPreviewOrigin;

    /// <summary>Detaches the manager and stops the timer.</summary>
    public void Detach()
    {
        _suspensionGeneration++;
        _timer.Stop();
        _getTabs = null;
        _getWebViews = null;
        _teardownWebView = null;
        _isPreviewOrigin = null;
        ForgetAllSuspensionState();
    }

    /// <summary>
    /// Suspends background tabs to save CPU by setting low memory target and calling TrySuspendAsync.
    /// Only tabs where suspend succeeds are marked sleeping.
    ///
    /// Repeated activation of the same active tab reaches this method for every
    /// background tab again. A tab already confirmed sleeping on the same core is
    /// skipped, and a tab with a suspend still in flight is coalesced, so switching
    /// back and forth does not repeat the same native work. If the tab's control was
    /// replaced, the recorded core no longer matches and the new core is suspended.
    /// </summary>
    public void SuspendBackgroundTabs(BrowserTab activeTab)
    {
        if (!_settings.TabSleepEnabled) return;
        if (_getWebViews is null || _getTabs is null) return;

        // Clear sleeping flag for the active tab on resume. Its confirmed record is
        // dropped as well, so a later switch away suspends the core again.
        activeTab.IsSleeping = false;
        ForgetConfirmedSleeping(activeTab.Id);

        var generation = _suspensionGeneration;
        var tabsById = _getTabs().ToDictionary(t => t.Id);
        foreach (var (id, wv) in _getWebViews())
        {
            if (id == activeTab.Id) continue;
            if (!tabsById.TryGetValue(id, out var tab) || tab.IsActive || tab.IsHibernated) continue;
            if (tab.IsPinned && !_settings.AllowPinnedTabsToSleep) continue;

            dynamic core;
            try { core = wv.CoreWebView2; }
            catch (Exception ex)
            {
                Trace.WriteLine($"SuspendBackgroundTabs skipped disposed tab {id}: {ex.Message}");
                continue;
            }
            if (core is null) continue;

            if (IsConfirmedSleeping(id, (object)core, tab))
            {
                Trace.WriteLine($"SuspendBackgroundTabs skipped sleeping tab {id}");
                continue;
            }

            if (HasPendingSuspension(id, (object)core))
            {
                Trace.WriteLine($"SuspendBackgroundTabs coalesced pending suspension for tab {id}");
                continue;
            }

            try
            {
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SuspendBackgroundTabs MemoryUsageTargetLevel failed for tab {id}: {ex.Message}");
                continue;
            }

            if (tab is not null && !tab.IsHibernated && !tab.IsActive)
                StartSuspension((object)core, id, tab, generation);
            else
                StartSuspension((object)core, id, null, generation);
        }
    }

    /// <summary>Resumes live WebViews and clears their sleeping flags.</summary>
    public void ClearSleepingState()
    {
        _suspensionGeneration++;
        ForgetAllSuspensionState();
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

    private bool IsConfirmedSleeping(Guid tabId, object core, BrowserTab? tab)
    {
        lock (_stateLock)
        {
            if (!_confirmedSleepingCores.TryGetValue(tabId, out var recorded))
                return false;

            if (!recorded.TryGetTarget(out var recordedCore)
                || !ReferenceEquals(recordedCore, core)
                || tab is null
                || tab.IsActive
                || tab.IsHibernated
                || !tab.IsSleeping)
            {
                _confirmedSleepingCores.Remove(tabId);
                return false;
            }

            return true;
        }
    }

    private bool HasPendingSuspension(Guid tabId, object core)
    {
        lock (_stateLock)
        {
            if (!_pendingSuspensions.TryGetValue(tabId, out var pending))
                return false;

            if (pending.Task.IsCompleted || !ReferenceEquals(pending.Core, core))
            {
                _pendingSuspensions.Remove(tabId);
                return false;
            }

            return true;
        }
    }

    private void StartSuspension(object core, Guid tabId, BrowserTab? tab, int generation)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_stateLock)
        {
            if (_pendingSuspensions.TryGetValue(tabId, out var existing)
                && !existing.Task.IsCompleted
                && ReferenceEquals(existing.Core, core))
            {
                return;
            }

            // A new attempt on this tab replaces any confirmed record from an older
            // core. The record is written again only if this attempt succeeds on the
            // core the tab still owns.
            _confirmedSleepingCores.Remove(tabId);
            _pendingSuspensions[tabId] = new PendingSuspension(core, completion.Task);
        }

        _ = RunSuspensionAsync(core, tabId, tab, generation, completion);
    }

    private async Task RunSuspensionAsync(
        object core,
        Guid tabId,
        BrowserTab? tab,
        int generation,
        TaskCompletionSource completion)
    {
        try
        {
            await TrySuspendSafeAsync(core, tabId, tab, generation);
        }
        catch (Exception ex)
        {
            // TrySuspendSafeAsync handles the expected COM and disposal failures.
            // This catches anything unexpected so the fire and forget call cannot
            // finish as an unobserved task.
            Trace.WriteLine($"SuspendBackgroundTabs work failed for tab {tabId}: {ex.Message}");
        }
        finally
        {
            lock (_stateLock)
            {
                if (_pendingSuspensions.TryGetValue(tabId, out var pending)
                    && ReferenceEquals(pending.Task, completion.Task))
                {
                    _pendingSuspensions.Remove(tabId);
                }
            }
            completion.TrySetResult();
        }
    }

    /// <summary>Calls TrySuspendAsync and marks the tab sleeping only when suspend returns true and sleep is still enabled.</summary>
    private async Task TrySuspendSafeAsync(dynamic core, Guid tabId, BrowserTab? tab, int generation)
    {
        bool success = false;
        try
        {
            success = await core.TrySuspendAsync();
        }
        catch (InvalidOperationException ex)
        {
            Trace.WriteLine($"TrySuspendAsync skipped for disposed tab {tabId}: {ex.Message}");
        }
        catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x8007139F)
        {
            Trace.WriteLine($"TrySuspendAsync not in correct state for tab {tabId}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"TrySuspendAsync failed for tab {tabId}: {ex.Message}");
        }

        if (success && tab is not null && (tab.IsActive || _getTabs?.Invoke().Contains(tab) != true ||
            generation != _suspensionGeneration || !_settings.TabSleepEnabled ||
            (tab.IsPinned && !_settings.AllowPinnedTabsToSleep)))
        {
            // Selection, transfer and settings can change while suspension is pending.
            // Undo the completed suspension when the page must now stay awake.
            try
            {
                core.Resume();
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            }
            catch (Exception ex) { Trace.WriteLine($"Resume after pending suspension failed: {ex.Message}"); }
            tab.IsSleeping = false;
            ForgetConfirmedSleeping(tabId);
            return;
        }

        if (success && tab != null && !tab.IsActive && !tab.IsHibernated && _settings.TabSleepEnabled
            && generation == _suspensionGeneration && IsCurrentCore(tabId, (object)core))
        {
            tab.IsSleeping = true;
            RememberConfirmedSleeping(tabId, (object)core);
        }
    }

    /// <summary>
    /// True when the core this attempt suspended is still the control the tab owns.
    /// A teardown, a hibernation wake or a transfer can replace it while the native
    /// call is in flight, and marking the tab sleeping for a core it no longer has
    /// would make the next activation believe the replacement is asleep.
    /// </summary>
    private bool IsCurrentCore(Guid tabId, object core)
    {
        var getWebViews = _getWebViews;
        if (getWebViews is null) return false;

        try
        {
            return getWebViews().TryGetValue(tabId, out var wv) && ReferenceEquals(wv.CoreWebView2, core);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SuspendBackgroundTabs could not read the current core for tab {tabId}: {ex.Message}");
            return false;
        }
    }

    private void RememberConfirmedSleeping(Guid tabId, object core)
    {
        lock (_stateLock) _confirmedSleepingCores[tabId] = new WeakReference<object>(core);
    }

    private void ForgetConfirmedSleeping(Guid tabId)
    {
        lock (_stateLock) _confirmedSleepingCores.Remove(tabId);
    }

    private void ForgetSuspensionState(Guid tabId)
    {
        lock (_stateLock)
        {
            _pendingSuspensions.Remove(tabId);
            _confirmedSleepingCores.Remove(tabId);
        }
    }

    private void ForgetAllSuspensionState()
    {
        lock (_stateLock)
        {
            _pendingSuspensions.Clear();
            _confirmedSleepingCores.Clear();
        }
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
                ForgetSuspensionState(tab.Id);
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
        ForgetSuspensionState(tab.Id);
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

        // If there are ANY active downloads, disable all hibernation to be safe,
        // because destroying a WebView might abort downloads originating from it.
        if (_downloadStore.Items.Any(d => d.State == DownloadState.InProgress))
            return false;

        return true;
    }
}
