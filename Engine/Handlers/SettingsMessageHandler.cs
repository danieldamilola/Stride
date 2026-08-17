using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.ViewModels;

namespace StrideBrowser.Engine.Handlers;

public class SettingsMessageHandler : IWebMessageHandler, ISettingEmitter
{
    private readonly TabEngine _engine;
    private readonly BrowserViewModel _vm;
    private readonly ISettingsStore _settingsStore;
    private readonly UpdateService _updateService;

    public event Action<string, string>? SettingChanged;

    public SettingsMessageHandler(TabEngine engine, BrowserViewModel vm, ISettingsStore settingsStore, UpdateService updateService)
    {
        _engine = engine;
        _vm = vm;
        _settingsStore = settingsStore;
        _updateService = updateService;
    }

    public IReadOnlyDictionary<string, Func<string, Task>> GetPrefixHandlers()
    {
        return new Dictionary<string, Func<string, Task>>
        {
            [WebMessagePrefix.Setting] = HandleSetting,
            ["install-update:"] = async (_) => { await _updateService.DownloadAndInstallUpdateAsync(); }
        };
    }

    public IReadOnlyDictionary<string, Func<Task>> GetExactHandlers()
    {
        return new Dictionary<string, Func<Task>>
        {
            [WebMessagePrefix.OpenBackgroundsFolder] = HandleOpenBackgroundsFolder,
            ["check-for-update"] = async () => 
            {
                var item = await _updateService.CheckForUpdateCustomAsync();
                var status = item is null ? "false" : "true";
                var version = item?.Version ?? "";
                var url = item?.DownloadLink ?? "";
                _engine.PostMessageToActiveTab($"update-check-result:{status}:{version}:{url}");
            }
        };
    }

    private Task HandleOpenBackgroundsFolder()
    {
        var userBgFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stride", "Backgrounds");
        if (!System.IO.Directory.Exists(userBgFolder))
            System.IO.Directory.CreateDirectory(userBgFolder);

        Process.Start(new ProcessStartInfo { FileName = userBgFolder, UseShellExecute = true });
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

    private static readonly Dictionary<string, Action<BrowserSettings, string>> SettingSetters = new()
    {
        ["appTheme"] = (s, v) => s.AppTheme = Enum.TryParse<AppThemeMode>(v, true, out var mode) ? mode : AppThemeMode.System,
        ["search"] = (s, v) => s.SearchEngine = v,
        ["zoom"] = (s, v) => { if (int.TryParse(v, out var z)) s.DefaultZoom = z; },

        ["restoreSession"] = (s, v) => s.RestoreSessionOnStartup = v == "true",

        ["useFloatingBar"] = (s, v) => s.UseFloatingCommandBar = v == "true",
        ["hwAccel"] = (s, v) => s.HardwareAccelerationEnabled = v == "true",
        ["darkMode"] = (s, v) => s.ForceDarkMode = v == "true",
        ["forceHttps"] = (s, v) => s.ForceHttps = v == "true",
        ["smartScreen"] = (s, v) => s.SmartScreenEnabled = v == "true",
        ["adBlock"] = (s, v) => s.AdBlockEnabled = v == "true",
        ["clearOnExit"] = (s, v) => s.ClearDataOnExit = v == "true",
        ["blockDupes"] = (s, v) => s.BlockDuplicateTabs = v == "true",
        ["sidebarPosition"] = (s, v) => s.IsSidebarOnRight = v == "right",
        ["addressBarOnLeft"] = (s, v) => s.AddressBarOnLeft = v == "true",
        ["showTabNames"] = (s, v) => s.ShowTabNames = v == "true",
        ["showBackArrow"] = (s, v) => s.ShowBackArrow = v == "true",
        ["showForwardArrow"] = (s, v) => s.ShowForwardArrow = v == "true",
        ["showRefreshButton"] = (s, v) => s.ShowRefreshButton = v == "true",
        ["showDownloadsIcon"] = (s, v) => s.ShowDownloadsIcon = v == "true",
        ["showSettingsIcon"] = (s, v) => s.ShowSettingsIcon = v == "true",
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
}
