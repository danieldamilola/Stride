using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine.Handlers;

public class SettingsMessageHandler : IWebMessageHandler, ISettingEmitter
{
    private readonly TabEngine _engine;
    private readonly BrowserSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly UpdateService _updateService;
    private readonly TabHibernationManager _hibernationManager;

    public event Action<string, string>? SettingChanged;

    /// <summary>Initializes a new instance of the SettingsMessageHandler.</summary>
    public SettingsMessageHandler(TabEngine engine, BrowserSettings settings, ISettingsStore settingsStore, UpdateService updateService, TabHibernationManager hibernationManager)
    {
        _engine = engine;
        _settings = settings;
        _settingsStore = settingsStore;
        _updateService = updateService;
        _hibernationManager = hibernationManager;
    }

    /// <summary>Returns the message routes handled by this handler.</summary>
    public IEnumerable<MessageRoute> GetRoutes()
    {
        yield return MessageRoute.Prefix(WebMessagePrefix.Setting, HandleSetting);
        yield return MessageRoute.Exact(WebMessagePrefix.ResetSettings, HandleResetSettings);
        yield return MessageRoute.Prefix("install-update:", async (_) => 
        { 
            bool downloaded = await _updateService.DownloadUpdateAsync();
            if (downloaded)
            {
                _updateService.InstallUpdate();
            }
        });
        yield return MessageRoute.Exact(WebMessagePrefix.OpenBackgroundsFolder, HandleOpenBackgroundsFolder);
        yield return MessageRoute.Exact("check-for-update", async () =>
        {
            var status = await _updateService.CheckForUpdatesQuietlyAsync();
            string statusStr = status.HasValue ? status.Value.ToString().ToLowerInvariant() : "error";
            _engine.PostMessageToActiveTab($"update-check-result:{statusStr}:{_updateService.LatestVersion ?? ""}:");
        });
        yield return MessageRoute.Exact(WebMessagePrefix.OpenReleaseNotes, async () =>
        {
            var tab = _engine.CreateTab(InternalUrls.ReleaseNotes);
            await _engine.ActivateAsync(tab);
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.2.0";
            _engine.NavigateToReleaseNotes(tab, version);
        });
        yield return MessageRoute.Exact(WebMessagePrefix.OpenOnboarding, async () =>
        {
            var tab = _engine.CreateTab(InternalUrls.Onboarding);
            await _engine.ActivateAsync(tab);
            _engine.NavigateToOnboarding(tab);
        });
    }

    /// <summary>Resets all settings to defaults and reloads the settings page.</summary>
    private Task HandleResetSettings()
    {
        var keepOnboarding = _settings.HasCompletedOnboarding;
        _settings.ResetToDefaults();
        _settings.HasCompletedOnboarding = keepOnboarding;
        _settingsStore.Save(_settings);
        SettingChanged?.Invoke("reset", "true");

        if (_engine.ActiveTab?.Url == InternalUrls.Settings)
            _engine.NavigateToSettings(_engine.ActiveTab, _settings);

        return Task.CompletedTask;
    }

    /// <summary>Opens the user backgrounds folder in the file explorer.</summary>
    private Task HandleOpenBackgroundsFolder()
    {
        var userBgFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stride", "Backgrounds");
        if (!System.IO.Directory.Exists(userBgFolder))
            System.IO.Directory.CreateDirectory(userBgFolder);

        Process.Start(new ProcessStartInfo { FileName = userBgFolder, UseShellExecute = true });
        return Task.CompletedTask;
    }

    /// <summary>Handles a setting change message from the settings page.</summary>
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
                _settings.CustomShortcuts[subParts[0]] = subParts[1];
                _settingsStore.Save(_settings);
                SettingChanged?.Invoke("shortcut", subParts[0]);
            }
            return;
        }

        // Special handling for shortcut reset: "shortcutReset:ActionName"
        if (key == "shortcutReset")
        {
            _settings.CustomShortcuts.Remove(value);
            _settingsStore.Save(_settings);
            SettingChanged?.Invoke("shortcutReset", value);
            return;
        }

        if (SettingSetters.TryGetValue(key, out var setter))
            setter(_settings, value);
        else
            Trace.WriteLine($"WebMessageRouter: unknown setting key '{key}'");

        _settingsStore.Save(_settings);
        SettingChanged?.Invoke(key, value);

        if (key == "tabSleep" && value == "false")
            _hibernationManager.ClearSleepingState();

        // Live-reload: re-inject unhook script into YouTube tabs when settings change
        if (key.StartsWith("unhook", StringComparison.Ordinal))
            await _engine.ReInjectUnhookAsync();

        // Live-reload: apply enhancer settings to open YouTube tabs too
        if (key.StartsWith("yt", StringComparison.Ordinal))
            await _engine.ReInjectEnhancerAsync();
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
        ["searchSuggestions"] = (s, v) => s.SearchSuggestionsEnabled = v == "true",
        ["faviconProxy"] = (s, v) => s.FaviconProxyEnabled = v == "true",
        ["blockDupes"] = (s, v) => s.BlockDuplicateTabs = v == "true",
        ["tabHibernation"] = (s, v) => s.TabHibernationEnabled = v == "true",
        ["tabSleep"] = (s, v) => s.TabSleepEnabled = v == "true",
        ["tabSleepOpacity"] = (s, v) => { if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var o)) s.TabSleepOpacity = Math.Clamp(o, 0.1, 1.0); },
        ["tabHibernationOpacity"] = (s, v) => { if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var o)) s.TabHibernationOpacity = Math.Clamp(o, 0.1, 1.0); },
        ["tabSleepDim"] = (s, v) => s.TabSleepDimEnabled = v == "true",
        ["tabHibernationDim"] = (s, v) => s.TabHibernationDimEnabled = v == "true",
        ["sidebarPosition"] = (s, v) => s.IsSidebarOnRight = v == "right",
        ["addressBarOnLeft"] = (s, v) => s.AddressBarOnLeft = v == "true",
        ["showTabNames"] = (s, v) => s.ShowTabNames = v == "true",
        ["showBackArrow"] = (s, v) => s.ShowBackArrow = v == "true",
        ["showForwardArrow"] = (s, v) => s.ShowForwardArrow = v == "true",
        ["showRefreshButton"] = (s, v) => s.ShowRefreshButton = v == "true",
        ["showDownloadsIcon"] = (s, v) => s.ShowDownloadsIcon = v == "true",
        ["showSettingsIcon"] = (s, v) => s.ShowSettingsIcon = v == "true",
        ["showReaderIcon"] = (s, v) => s.ShowReaderIcon = v == "true",
        ["accentColor"] = (s, v) => { if (v.StartsWith('#') && (v.Length == 7 || v.Length == 4)) s.AccentColor = v; },
        ["focusDomains"] = (s, v) => s.FocusDomains = System.Web.HttpUtility.UrlDecode(v),
        ["focusLocked"] = (s, v) => s.FocusLocked = v == "true",

        ["ytEnhancerEnabled"] = (s, v) => s.YtEnhancerEnabled = v == "true",
        ["ytQuality"] = (s, v) => s.YtDefaultQuality = v,
        ["ytAutoplay"] = (s, v) => s.YtDisableAutoplay = v == "true",
        ["ytPauseTab"] = (s, v) => s.YtPauseOnTabSwitch = v == "true",
        ["ytSpeed"] = (s, v) => { if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var spd) && double.IsFinite(spd)) s.YtDefaultSpeed = spd; },
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