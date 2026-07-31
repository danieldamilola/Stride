using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.ViewModels;

namespace StrideBrowser.Services;

/// <summary>
/// Dispatches web messages from internal pages to typed handlers.
/// Replaces the monolithic if/else chain formerly in MainWindow.
/// </summary>
public sealed class WebMessageRouter
{
    private readonly Dictionary<string, Func<string, Task>> _prefixHandlers;
    private readonly Dictionary<string, Func<Task>> _exactHandlers;

    private readonly TabEngine _engine;
    private readonly BrowserViewModel _vm;
    private readonly IOneTabStore _oneTabStore;
    private readonly IHistoryStore _historyStore;
    private readonly ISettingsStore _settingsStore;
    private readonly IDownloadStore _downloadStore;
    private readonly ExtensionManager _extensionManager;

    /// <summary>Fires when settings change so the view can apply live effects.</summary>
    public event Action<string, string>? SettingChanged;

    public WebMessageRouter(
        TabEngine engine,
        BrowserViewModel vm,
        IOneTabStore oneTabStore,
        IHistoryStore historyStore,
        IDownloadStore downloadStore,
        ISettingsStore settingsStore,
        ExtensionManager extensionManager)
    {
        _engine = engine;
        _vm = vm;
        _oneTabStore = oneTabStore;
        _historyStore = historyStore;
        _downloadStore = downloadStore;
        _settingsStore = settingsStore;
        _extensionManager = extensionManager;

        _prefixHandlers = new Dictionary<string, Func<string, Task>>
        {
            [WebMessagePrefix.Open] = HandleOpen,
            [WebMessagePrefix.Search] = HandleSearch,
            [WebMessagePrefix.Setting] = HandleSetting,
            [WebMessagePrefix.OneTabRestore] = HandleOneTabRestore,
            [WebMessagePrefix.OneTabDelete] = HandleOneTabDelete,
            [WebMessagePrefix.OneTabRename] = HandleOneTabRename,
            [WebMessagePrefix.OneTabOpen] = HandleOneTabOpen,
            [WebMessagePrefix.OneTabDeleteTab] = HandleOneTabDeleteTab,
            [WebMessagePrefix.OneTabStar] = HandleOneTabStar,
            [WebMessagePrefix.OneTabReorderTab] = HandleOneTabReorderTab,
            [WebMessagePrefix.OneTabReorderGroup] = HandleOneTabReorderGroup,
            [WebMessagePrefix.HistoryOpen] = HandleOpen,
            [WebMessagePrefix.ShortcutAdd] = HandleShortcutAdd,
            [WebMessagePrefix.ShortcutRemove] = HandleShortcutRemove,
            [WebMessagePrefix.ShortcutClick] = HandleShortcutClick,
            [WebMessagePrefix.DownloadOpen] = HandleDownloadOpen,
            [WebMessagePrefix.DownloadFolder] = HandleDownloadFolder,
            [WebMessagePrefix.DownloadCancel] = HandleDownloadCancel,
            [WebMessagePrefix.DownloadPause] = HandleDownloadPause,
            [WebMessagePrefix.DownloadResume] = HandleDownloadResume,
            [WebMessagePrefix.TCLensGetText] = HandleTCLensGetText,
        };

        _exactHandlers = new Dictionary<string, Func<Task>>
        {
            [WebMessagePrefix.HistoryClear] = HandleHistoryClear,
            [WebMessagePrefix.SetDefaultBrowser] = HandleSetDefaultBrowser,
            [WebMessagePrefix.DownloadClear] = HandleDownloadClear,
            [WebMessagePrefix.DownloadRequestSync] = HandleDownloadSync,
        };
    }

    public async Task RouteAsync(string message)
    {
        try
        {
            if (_exactHandlers.TryGetValue(message, out var exactHandler))
            {
                await exactHandler();
                return;
            }

            foreach (var (prefix, handler) in _prefixHandlers)
            {
                if (message.StartsWith(prefix, StringComparison.Ordinal))
                {
                    await handler(message[prefix.Length..]);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"WebMessageRouter.RouteAsync failed for '{message}': {ex.Message}");
        }
    }

    private async Task HandleOpen(string url)
    {
        var tab = _engine.CreateTab(url);
        await _engine.ActivateAsync(tab);
    }

    private Task HandleSearch(string query)
    {
        var url = _vm.ResolveInput(query);
        if (_engine.ActiveTab is not null)
        {
            _engine.ActiveTab.Url = url;
            _vm.AddressText = url;
            _engine.Navigate(_engine.ActiveTab, url);
        }
        return Task.CompletedTask;
    }

    private async Task HandleSetting(string payload)
    {
        var parts = payload.Split(':', 2);
        if (parts.Length < 2) return;

        var key = parts[0];
        var value = parts[1];

        // Special handling for shortcut rebinding: "shortcut:ActionName:Combo"
        if (key == "shortcut")
        {
            var subParts = value.Split(':', 2);
            if (subParts.Length == 2)
            {
                _vm.Settings.CustomShortcuts[subParts[0]] = subParts[1];
                _settingsStore.Save(_vm.Settings);
                SettingChanged?.Invoke("shortcut", subParts[0]);
            }
            return;
        }

        // Special handling for shortcut reset: "shortcutReset:ActionName"
        if (key == "shortcutReset")
        {
            _vm.Settings.CustomShortcuts.Remove(value);
            _settingsStore.Save(_vm.Settings);
            SettingChanged?.Invoke("shortcutReset", value);
            return;
        }

        if (SettingSetters.TryGetValue(key, out var setter))
            setter(_vm.Settings, value);
        else
            Trace.WriteLine($"WebMessageRouter: unknown setting key '{key}'");

        _settingsStore.Save(_vm.Settings);
        SettingChanged?.Invoke(key, value);

        // Live-reload: re-inject unhook script into YouTube tabs when settings change
        if (key.StartsWith("unhook", StringComparison.Ordinal))
            await _engine.ReInjectUnhookAsync();
    }

    private async Task HandleOneTabRestore(string groupId)
    {
        var groups = _oneTabStore.Load();
        var group = groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null) return;

        BrowserTab? lastTab = null;
        foreach (var entry in group.Tabs)
            lastTab = _engine.CreateTab(entry.Url);

        _oneTabStore.RemoveGroup(groupId);

        if (lastTab is not null)
            await _engine.ActivateAsync(lastTab);

        RefreshOneTabPages();
    }

    private Task HandleOneTabDelete(string groupId)
    {
        _oneTabStore.RemoveGroup(groupId);
        RefreshOneTabPages();
        return Task.CompletedTask;
    }

    private Task HandleOneTabRename(string payload)
    {
        if (!TrySplitPayload(payload, out var groupId, out var newName)) return Task.CompletedTask;
        var groups = _oneTabStore.Load();
        var group = groups.FirstOrDefault(g => g.Id == groupId);
        if (group is not null)
        {
            group.Name = newName;
            _oneTabStore.Save(groups);
            RefreshOneTabPages();
        }
        return Task.CompletedTask;
    }

    private async Task HandleOneTabOpen(string payload)
    {
        if (!TrySplitPayload(payload, out var groupId, out var url)) return;

        _oneTabStore.RemoveTab(groupId, url);
        RefreshOneTabPages();

        var tab = _engine.CreateTab(url);
        await _engine.ActivateAsync(tab);
    }

    private Task HandleOneTabDeleteTab(string payload)
    {
        if (!TrySplitPayload(payload, out var groupId, out var url)) return Task.CompletedTask;

        _oneTabStore.RemoveTab(groupId, url);
        RefreshOneTabPages();
        return Task.CompletedTask;
    }

    private Task HandleOneTabStar(string payload)
    {
        if (!TrySplitPayload(payload, out var groupId, out var indexStr)) return Task.CompletedTask;
        if (!int.TryParse(indexStr, out var tabIndex)) return Task.CompletedTask;

        _oneTabStore.ToggleStar(groupId, tabIndex);
        RefreshOneTabPages();
        return Task.CompletedTask;
    }

    private Task HandleOneTabReorderTab(string payload)
    {
        var parts = payload.Split(':');
        if (parts.Length < 3) return Task.CompletedTask;
        var groupId = parts[0];
        if (!int.TryParse(parts[1], out var oldIdx) || !int.TryParse(parts[2], out var newIdx))
            return Task.CompletedTask;

        _oneTabStore.ReorderTab(groupId, oldIdx, newIdx);
        RefreshOneTabPages();
        return Task.CompletedTask;
    }

    private Task HandleOneTabReorderGroup(string payload)
    {
        var parts = payload.Split(':');
        if (parts.Length < 2) return Task.CompletedTask;
        if (!int.TryParse(parts[0], out var oldIdx) || !int.TryParse(parts[1], out var newIdx))
            return Task.CompletedTask;

        _oneTabStore.ReorderGroup(oldIdx, newIdx);
        RefreshOneTabPages();
        return Task.CompletedTask;
    }

    private Task HandleHistoryClear()
    {
        _historyStore.Clear();
        foreach (var t in _engine.Tabs)
        {
            if (t.Url == InternalUrls.History)
                _engine.NavigateToHistory(t, []);
        }
        return Task.CompletedTask;
    }

    private Task HandleSetDefaultBrowser()
    {
        DefaultBrowserRegistrar.Register();
        DefaultBrowserRegistrar.OpenDefaultAppsSettings();
        return Task.CompletedTask;
    }

    private Task HandleShortcutAdd(string payload)
    {
        try
        {
            var item = JsonSerializer.Deserialize<ShortcutItem>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (item is null || string.IsNullOrWhiteSpace(item.Url)) return Task.CompletedTask;

            _vm.Settings.NewTabShortcuts.Add(item);
            _settingsStore.Save(_vm.Settings);
            SettingChanged?.Invoke("shortcuts", "");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"HandleShortcutAdd failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task HandleShortcutRemove(string payload)
    {
        if (!int.TryParse(payload, out var index)) return Task.CompletedTask;
        if (index < 0 || index >= _vm.Settings.NewTabShortcuts.Count) return Task.CompletedTask;

        _vm.Settings.NewTabShortcuts.RemoveAt(index);
        _settingsStore.Save(_vm.Settings);
        SettingChanged?.Invoke("shortcuts", "");
        return Task.CompletedTask;
    }

    private Task HandleShortcutClick(string url)
    {
        if (_engine.ActiveTab is not null)
        {
            _engine.ActiveTab.Url = url;
            _vm.AddressText = url;
            _engine.Navigate(_engine.ActiveTab, url);
        }
        return Task.CompletedTask;
    }

    private Task HandleDownloadOpen(string id)
    {
        var item = _downloadStore.Get(id);
        if (item?.State == DownloadState.Completed && System.IO.File.Exists(item.FilePath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.FilePath) { UseShellExecute = true });
            }
            catch
            {
                // If there's no default application associated with the file or it fails to launch, fallback to opening the folder
                _ = HandleDownloadFolder(id);
            }
        }
        return Task.CompletedTask;
    }

    private Task HandleDownloadFolder(string id)
    {
        var item = _downloadStore.Get(id);
        if (item?.State == DownloadState.Completed && System.IO.File.Exists(item.FilePath))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
        }
        return Task.CompletedTask;
    }

    private Task HandleDownloadCancel(string id)
    {
        var item = _downloadStore.Get(id);
        if (item?.State == DownloadState.InProgress || item?.State == DownloadState.Paused)
        {
            item.State = DownloadState.Cancelled;
        }
        return Task.CompletedTask;
    }

    private Task HandleDownloadPause(string id)
    {
        var item = _downloadStore.Get(id);
        if (item?.State == DownloadState.InProgress)
        {
            item.State = DownloadState.Paused;
        }
        return Task.CompletedTask;
    }

    private Task HandleDownloadResume(string id)
    {
        var item = _downloadStore.Get(id);
        if (item?.State == DownloadState.Paused)
        {
            item.State = DownloadState.InProgress;
        }
        return Task.CompletedTask;
    }

    private Task HandleDownloadClear()
    {
        _downloadStore.ClearCompleted();
        if (_engine.ActiveTab?.Url == InternalUrls.Downloads) _engine.Reload();
        return Task.CompletedTask;
    }

    private async Task HandleDownloadSync()
    {
        if (_engine.ActiveTab?.Url != InternalUrls.Downloads) return;
        var wv = _engine.GetCoreWebView2();
        if (wv == null) return;
        
        var items = _downloadStore.Items.ToList();
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { PropertyNamingPolicy = null });
        json = json.Replace("\\", "\\\\").Replace("'", "\\'");
        
        await wv.ExecuteScriptAsync($"if (typeof updateDownloads === 'function') updateDownloads('{json}');");
    }

    private void RefreshOneTabPages()
    {
        var groups = _oneTabStore.Load();
        foreach (var t in _engine.Tabs)
        {
            if (t.Url == InternalUrls.OneTab)
                _engine.NavigateToOneTab(t, groups);
        }
    }

    private static readonly Dictionary<string, Action<BrowserSettings, string>> SettingSetters = new()
    {
        ["appTheme"] = (s, v) => s.AppTheme = Enum.TryParse<AppThemeMode>(v, true, out var mode) ? mode : AppThemeMode.System,
        ["search"] = (s, v) => s.SearchEngine = v,
        ["zoom"] = (s, v) => { if (int.TryParse(v, out var z)) s.DefaultZoom = z; },

        ["restoreSession"] = (s, v) => s.RestoreSessionOnStartup = v == "true",
        ["idm"] = (s, v) => s.UseIDMForDownloads = v == "true",
        ["hwAccel"] = (s, v) => s.HardwareAccelerationEnabled = v == "true",
        ["darkMode"] = (s, v) => s.ForceDarkMode = v == "true",
        ["forceHttps"] = (s, v) => s.ForceHttps = v == "true",
        ["smartScreen"] = (s, v) => s.SmartScreenEnabled = v == "true",
        ["adBlock"] = (s, v) => s.AdBlockEnabled = v == "true",
        ["clearOnExit"] = (s, v) => s.ClearDataOnExit = v == "true",
        ["blockDupes"] = (s, v) => s.BlockDuplicateTabs = v == "true",
        ["sidebarPosition"] = (s, v) => s.IsSidebarOnRight = v == "right",
        ["accentColor"] = (s, v) => { if (v.StartsWith('#') && (v.Length == 7 || v.Length == 4)) s.AccentColor = v; },
        ["focusDomains"] = (s, v) => s.FocusDomains = System.Web.HttpUtility.UrlDecode(v),
        ["focusLocked"] = (s, v) => s.FocusLocked = v == "true",

        ["ytEnhancerEnabled"] = (s, v) => s.YtEnhancerEnabled = v == "true",
        ["ytQuality"] = (s, v) => s.YtDefaultQuality = v,
        ["ytAutoplay"] = (s, v) => s.YtDisableAutoplay = v == "true",
        ["ytPauseTab"] = (s, v) => s.YtPauseOnTabSwitch = v == "true",
        ["ytSpeed"] = (s, v) => { if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var spd)) s.YtDefaultSpeed = spd; },
        ["ytLoop"] = (s, v) => s.YtLoopVideo = v == "true",

        ["unhookEnabled"] = (s, v) => s.UnhookEnabled = v == "true",
        ["unhookHomeFeed"] = (s, v) => s.UnhookHideHomeFeed = v == "true",
        ["unhookSidebar"] = (s, v) => s.UnhookHideVideoSidebar = v == "true",
        ["unhookRecommended"] = (s, v) => s.UnhookHideRecommended = v == "true",
        ["unhookLiveChat"] = (s, v) => s.UnhookHideLiveChat = v == "true",
        ["unhookPlaylist"] = (s, v) => s.UnhookHidePlaylist = v == "true",
        ["unhookFundraiser"] = (s, v) => s.UnhookHideFundraiser = v == "true",
        ["unhookEndFeed"] = (s, v) => s.UnhookHideEndScreenFeed = v == "true",
        ["unhookEndCards"] = (s, v) => s.UnhookHideEndScreenCards = v == "true",
        ["unhookShorts"] = (s, v) => s.UnhookHideShorts = v == "true",
        ["unhookComments"] = (s, v) => s.UnhookHideComments = v == "true",
        ["unhookMixes"] = (s, v) => s.UnhookHideMixes = v == "true",
        ["unhookMerch"] = (s, v) => s.UnhookHideMerch = v == "true",
        ["unhookVideoInfo"] = (s, v) => s.UnhookHideVideoInfo = v == "true",
        ["unhookTopHeader"] = (s, v) => s.UnhookHideTopHeader = v == "true",
        ["unhookNotifications"] = (s, v) => s.UnhookHideNotifications = v == "true",
        ["unhookInaptSearch"] = (s, v) => s.UnhookHideInaptSearch = v == "true",
        ["unhookExplore"] = (s, v) => s.UnhookHideExplore = v == "true",
        ["unhookMoreYT"] = (s, v) => s.UnhookHideMoreYT = v == "true",
        ["unhookSubscriptions"] = (s, v) => s.UnhookHideSubscriptions = v == "true",
        ["unhookAutoplay"] = (s, v) => s.UnhookDisableAutoplay = v == "true",
        ["unhookAnnotations"] = (s, v) => s.UnhookDisableAnnotations = v == "true",
    };

    private static bool TrySplitPayload(string payload, out string id, out string value)
    {
        var sep = payload.IndexOf(':');
        if (sep < 0) { id = value = ""; return false; }
        id = payload[..sep];
        value = payload[(sep + 1)..];
        return true;
    }

    private async Task HandleTCLensGetText(string _)
    {
        var activeTab = _engine.ActiveTab;
        if (activeTab == null) return;
        var wv = _engine.GetCoreWebView2();
        if (wv == null) return;

        var payload = new Dictionary<string, string>
        {
            ["type"] = "tclens-text",
            ["text"] = MainWindow.PendingTCLensText ?? "",
            ["url"] = MainWindow.PendingTCLensUrl ?? "",
            ["title"] = MainWindow.PendingTCLensTitle ?? ""
        };
        
        var jsonPayload = JsonSerializer.Serialize(payload);
        await wv.ExecuteScriptAsync($"if (window.tclensProcessInjection) window.tclensProcessInjection({jsonPayload});");
    }
}
