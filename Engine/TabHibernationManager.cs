using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    private int _maxLiveWebViews = 10;

    public TabHibernationManager(IDownloadStore downloadStore, BrowserSettings settings)
    {
        _downloadStore = downloadStore;
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => HibernateInactiveTabs();
    }

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

    public void Detach()
    {
        _timer.Stop();
        _getTabs = null;
        _getWebViews = null;
        _teardownWebView = null;
    }

    public void SuspendBackgroundTabs(BrowserTab activeTab)
    {
        if (!_settings.TabSleepEnabled) return;
        if (_getWebViews is null) return;
        foreach (var (id, wv) in _getWebViews())
        {
            if (id == activeTab.Id) continue;

            dynamic core;
            try { core = wv.CoreWebView2; }
            catch (Exception ex)
            {
                Trace.WriteLine($"SuspendBackgroundTabs skipped disposed tab {id}: {ex.Message}");
                continue;
            }
            if (core is null) continue;

            try
            {
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SuspendBackgroundTabs MemoryUsageTargetLevel failed for tab {id}: {ex.Message}");
                continue;
            }

            _ = TrySuspendSafeAsync(core, id);
        }
    }

    private static async Task TrySuspendSafeAsync(dynamic core, Guid tabId)
    {
        try
        {
            await core.TrySuspendAsync();
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
    }

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

    public void HibernateTab(BrowserTab tab)
    {
        if (_getWebViews is null) return;
        HibernateTab(tab, _getWebViews());
    }

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
    }

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

    private bool IsTabSafeToHibernate(BrowserTab tab, IReadOnlyDictionary<Guid, dynamic> webViews)
    {
        if (tab.IsActive || tab.IsHibernated) return false;
        if (tab.IsPinned) return false;
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
