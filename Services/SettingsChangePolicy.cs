using System.Globalization;
using StrideBrowser.Models;
using StrideBrowser.Services.Input;

namespace StrideBrowser.Services;

public static class SettingsChangePolicy
{
    private static readonly HashSet<string> BooleanSettings = [ "autoCheckForUpdates", "restoreSession", "hwAccel", "darkMode", "forceHttps", "smartScreen", "adBlock", "clearOnExit", "searchSuggestions", "faviconProxy", "blockDupes", "tabHibernation", "tabSleep", "allowPinnedTabsToSleep", "allowPinnedTabsToHibernate", "tabSleepDim", "tabHibernationDim", "addressBarOnLeft", "showTabNames", "roundedFrame", "adaptiveTint", "showBackArrow", "showForwardArrow", "showRefreshButton", "showDownloadsIcon", "showSettingsIcon", "showReaderIcon", "focusLocked", "ytEnhancerEnabled", "ytAutoplay", "ytPauseTab", "ytLoop", "unhookEnabled", "unhookHomeFeed", "unhookShorts", "unhookSidebar", "unhookLiveChat", "unhookPlaylist", "unhookFundraiser", "unhookEndFeed", "unhookEndCards", "unhookComments", "unhookMixes", "unhookMerch", "unhookVideoInfo", "unhookTopHeader", "unhookNotifications", "unhookInaptSearch", "unhookExplore", "unhookMoreYT", "unhookSubscriptions", "unhookAutoplay", "unhookAnnotations" ];

    public static bool RequiresRestart(string key) => key is "hwAccel" or "darkMode" or "smartScreen";

    public static bool TryApply(BrowserSettings settings, string key, string value, out string error)
    {
        error = "";
        if (BooleanSettings.Contains(key) && value is not ("true" or "false"))
        { error = "Choose on or off."; return false; }
        if (key == "appTheme" && (!Enum.TryParse<AppThemeMode>(value, true, out var theme) || !Enum.IsDefined(theme)))
        { error = "Choose a supported theme."; return false; }
        if (key == "zoom" && (!int.TryParse(value, out var zoom) || zoom < 25 || zoom > 500))
        { error = "Choose a zoom between 25 and 500 percent."; return false; }
        if (key == "accentColor" && !System.Text.RegularExpressions.Regex.IsMatch(value, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$"))
        { error = "Choose a valid color."; return false; }
        if (key is "tabSleepOpacity" or "tabHibernationOpacity" or "ytSpeed" &&
            (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)))
        { error = "Choose a valid number."; return false; }
        if (key is "focusLock")
        {
            if (settings.FocusLocked) { error = "Focus is already locked."; return false; }
            settings.FocusDomains = System.Web.HttpUtility.UrlDecode(value).Trim();
            settings.FocusLocked = true;
            return true;
        }
        if (key is "focusDomains" or "focusLocked" && settings.FocusLocked)
        { error = "Locked Focus rules cannot be changed."; return false; }
        if (key is "shortcut" or "shortcutReset")
        {
            var parts = value.Split(':', 2);
            var action = parts[0];
            if (!ShortcutDefaults.All.Any(d => d.Name == action))
            { error = "Unknown shortcut action."; return false; }
            if (key == "shortcutReset")
            {
                var defaultCombo = ShortcutDefaults.GetDefault(action);
                if (ShortcutDefaults.All.Any(d => d.Name != action && string.Equals(ShortcutDefaults.GetCombo(d.Name, settings.CustomShortcuts), defaultCombo, StringComparison.OrdinalIgnoreCase)))
                { error = "The default shortcut is assigned to another action."; return false; }
                settings.CustomShortcuts.Remove(action);
                return true;
            }
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
            { error = "Choose a keyboard shortcut."; return false; }
            if (ShortcutDefaults.All.Any(d => d.Name != action &&
                string.Equals(ShortcutDefaults.GetCombo(d.Name, settings.CustomShortcuts), parts[1], StringComparison.OrdinalIgnoreCase)))
            { error = "That shortcut is already assigned to another action."; return false; }
            if (!KeyboardShortcutMap.TryParseCombo(parts[1], out _, out _))
            { error = "Choose a supported key combination."; return false; }
            settings.CustomShortcuts[action] = parts[1];
            return true;
        }
        if (!SettingSetters.TryGetValue(key, out var setter))
        { error = "This setting is not supported."; return false; }
        if (key == "newTabBackground" && !string.IsNullOrEmpty(value))
        {
            var background = Uri.UnescapeDataString(value);
            if (!Uri.TryCreate(background, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                uri.Host is not ("local.assets" or "user.assets"))
            { error = "Choose a wallpaper from the available images."; return false; }
        }
        setter(settings, value);
        return true;
    }

    private static readonly Dictionary<string, Action<BrowserSettings, string>> SettingSetters = new()
    {
        ["appTheme"] = (s, v) => s.AppTheme = Enum.TryParse<AppThemeMode>(v, true, out var mode) ? mode : AppThemeMode.System,
        ["search"] = (s, v) => s.SearchEngine = v,
        ["zoom"] = (s, v) => { if (int.TryParse(v, out var z)) s.DefaultZoom = z; },

        ["newTabBackground"] = (s, v) => s.NewTabBackground = Uri.UnescapeDataString(v),
        ["autoCheckForUpdates"] = (s, v) => s.AutoCheckForUpdates = v == "true",
        ["restoreSession"] = (s, v) => s.RestoreSessionOnStartup = v == "true",

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
        ["allowPinnedTabsToSleep"] = (s, v) => s.AllowPinnedTabsToSleep = v == "true",
        ["allowPinnedTabsToHibernate"] = (s, v) => s.AllowPinnedTabsToHibernate = v == "true",
        ["tabSleepOpacity"] = (s, v) => { if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var o)) s.TabSleepOpacity = Math.Clamp(o, 0.1, 1.0); },
        ["tabHibernationOpacity"] = (s, v) => { if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var o)) s.TabHibernationOpacity = Math.Clamp(o, 0.1, 1.0); },
        ["tabSleepDim"] = (s, v) => s.TabSleepDimEnabled = v == "true",
        ["tabHibernationDim"] = (s, v) => s.TabHibernationDimEnabled = v == "true",
        ["sidebarPosition"] = (s, v) => s.IsSidebarOnRight = v == "right",
        ["addressBarOnLeft"] = (s, v) => s.AddressBarOnLeft = v == "true",
        ["showTabNames"] = (s, v) => s.ShowTabNames = v == "true",
        ["roundedFrame"] = (s, v) => s.RoundedWebContentFrameEnabled = v == "true",
        ["trackingPrevention"] = (s, v) => s.TrackingPreventionMode = TrackingPreventionModes.Parse(v),
        ["adaptiveTint"] = (s, v) => s.AdaptiveToolbarTint = v == "true",
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
        ["unhookShorts"] = (s, v) => s.UnhookHideShorts = v == "true",
        ["unhookSidebar"] = (s, v) => { s.UnhookHideVideoSidebar = v == "true"; s.UnhookHideRecommended = s.UnhookHideVideoSidebar; },
        ["unhookLiveChat"] = (s, v) => s.UnhookHideLiveChat = v == "true",
        ["unhookPlaylist"] = (s, v) => s.UnhookHidePlaylist = v == "true",
        ["unhookFundraiser"] = (s, v) => s.UnhookHideFundraiser = v == "true",
        ["unhookEndFeed"] = (s, v) => s.UnhookHideEndScreenFeed = v == "true",
        ["unhookEndCards"] = (s, v) => s.UnhookHideEndScreenCards = v == "true",
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
