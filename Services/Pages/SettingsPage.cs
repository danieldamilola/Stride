using System.Globalization;
using System.Text;
using StrideBrowser.Models;
using StrideBrowser.Services.Input;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the settings page HTML.</summary>
public sealed class SettingsPage
{
    /// <summary>Renders the settings HTML with current values and IPC token.</summary>
    public string Render(BrowserSettings settings, string ipcToken)
    {
        static string SelStr(string current, string value) =>
            current == value ? " selected" : "";
        static string SelInt(int current, int value) =>
            current == value ? " selected" : "";
        static string Chk(bool value) =>
            value ? " checked" : "";

        return Helpers.ResourceLoader.LoadTemplate("Resources.Pages.Settings.html",
            new Dictionary<string, string>
            {
                ["THEME"] = settings.AppTheme.ToString().ToLowerInvariant(),
                ["THEME_SYSTEM"] = settings.AppTheme == AppThemeMode.System ? " selected" : "",
                ["THEME_LIGHT"] = settings.AppTheme == AppThemeMode.Light ? " selected" : "",
                ["THEME_DARK"] = settings.AppTheme == AppThemeMode.Dark ? " selected" : "",
                ["SEARCH_DUCKDUCKGO"] = SelStr(settings.SearchEngine, "DuckDuckGo"),
                ["SEARCH_BRAVE"] = SelStr(settings.SearchEngine, "Brave"),
                ["SEARCH_GOOGLE"] = SelStr(settings.SearchEngine, "Google"),
                ["SEARCH_BING"] = SelStr(settings.SearchEngine, "Bing"),
                ["SEARCH_YAHOO"] = SelStr(settings.SearchEngine, "Yahoo"),
                ["SEARCH_STARTPAGE"] = SelStr(settings.SearchEngine, "Startpage"),
                ["ZOOM_75"] = SelInt(settings.DefaultZoom, 75),
                ["ZOOM_80"] = SelInt(settings.DefaultZoom, 80),
                ["ZOOM_90"] = SelInt(settings.DefaultZoom, 90),
                ["ZOOM_100"] = SelInt(settings.DefaultZoom, 100),
                ["ZOOM_110"] = SelInt(settings.DefaultZoom, 110),
                ["ZOOM_125"] = SelInt(settings.DefaultZoom, 125),
                ["ZOOM_150"] = SelInt(settings.DefaultZoom, 150),
                ["ZOOM_175"] = SelInt(settings.DefaultZoom, 175),
                ["ZOOM_200"] = SelInt(settings.DefaultZoom, 200),
                ["CHK_RESTORE"] = Chk(settings.RestoreSessionOnStartup),

                ["CHK_DARK"] = Chk(settings.ForceDarkMode),
                ["CHK_HWACCEL"] = Chk(settings.HardwareAccelerationEnabled),
                ["CHK_HTTPS"] = Chk(settings.ForceHttps),
                ["CHK_SMARTSCREEN"] = Chk(settings.SmartScreenEnabled),
                ["CHK_ADBLOCK"] = Chk(settings.AdBlockEnabled),
                ["CHK_CLEAR"] = Chk(settings.ClearDataOnExit),
                ["CHK_DUPES"] = Chk(settings.BlockDuplicateTabs),
                ["CHK_TAB_HIBERNATION"] = Chk(settings.TabHibernationEnabled),
                ["CHK_TAB_SLEEP"] = Chk(settings.TabSleepEnabled),
                ["TAB_SLEEP_OPACITY"] = settings.TabSleepOpacity.ToString(CultureInfo.InvariantCulture),
                ["TAB_SLEEP_OPACITY_PCT"] = ((int)Math.Round(settings.TabSleepOpacity * 100)).ToString(CultureInfo.InvariantCulture),
                ["TAB_HIBERNATION_OPACITY"] = settings.TabHibernationOpacity.ToString(CultureInfo.InvariantCulture),
                ["TAB_HIBERNATION_OPACITY_PCT"] = ((int)Math.Round(settings.TabHibernationOpacity * 100)).ToString(CultureInfo.InvariantCulture),
                ["CHK_TAB_SLEEP_DIM"] = Chk(settings.TabSleepDimEnabled),
                ["CHK_TAB_HIBERNATION_DIM"] = Chk(settings.TabHibernationDimEnabled),
                ["SIDEBAR_LEFT"] = settings.IsSidebarOnRight ? "" : " selected",
                ["SIDEBAR_RIGHT"] = settings.IsSidebarOnRight ? " selected" : "",
                ["ADDR_LEFT"] = settings.AddressBarOnLeft ? " selected" : "",
                ["ADDR_RIGHT"] = settings.AddressBarOnLeft ? "" : " selected",
                ["TAB_FULL"] = settings.ShowTabNames ? " selected" : "",
                ["TAB_COMPACT"] = settings.ShowTabNames ? "" : " selected",
                ["CHK_BACKARROW"] = Chk(settings.ShowBackArrow),
                ["CHK_FWDARROW"] = Chk(settings.ShowForwardArrow),
                ["CHK_REFRESH"] = Chk(settings.ShowRefreshButton),
                ["CHK_DOWNLOADS"] = Chk(settings.ShowDownloadsIcon),
                ["CHK_SETTINGSICON"] = Chk(settings.ShowSettingsIcon),
                ["CHK_READERICON"] = Chk(settings.ShowReaderIcon),
                ["CHK_USE_FLOATING_BAR"] = Chk(settings.UseFloatingCommandBar),
                ["CHK_AUTO_CHECK_FOR_UPDATES"] = Chk(settings.AutoCheckForUpdates),
                ["CURRENT_VERSION"] = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.3",
                ["WEBVIEW2_VERSION"] = typeof(Microsoft.Web.WebView2.Core.CoreWebView2).Assembly.GetName().Version?.ToString(3) ?? "1.0.4181",
                ["DOTNET_VERSION"] = System.Environment.Version.ToString(2),
                ["ACCENT_COLOR"] = settings.AccentColor,
                ["ACCENT"] = settings.AccentColor,
                ["ACCENT_RGB"] = HexToRgb(settings.AccentColor),
                ["YT_QUALITY_AUTO"] = SelStr(settings.YtDefaultQuality, "auto"),
                ["YT_QUALITY_HIGHEST"] = SelStr(settings.YtDefaultQuality, "highest"),
                ["YT_QUALITY_LOWEST"] = SelStr(settings.YtDefaultQuality, "lowest"),
                ["YT_QUALITY_TINY"] = SelStr(settings.YtDefaultQuality, "tiny"),
                ["YT_QUALITY_SMALL"] = SelStr(settings.YtDefaultQuality, "small"),
                ["YT_QUALITY_MEDIUM"] = SelStr(settings.YtDefaultQuality, "medium"),
                ["YT_QUALITY_LARGE"] = SelStr(settings.YtDefaultQuality, "large"),
                ["YT_QUALITY_720"] = SelStr(settings.YtDefaultQuality, "hd720"),
                ["YT_QUALITY_1080"] = SelStr(settings.YtDefaultQuality, "hd1080"),
                ["YT_QUALITY_1440"] = SelStr(settings.YtDefaultQuality, "hd1440"),
                ["YT_QUALITY_4K"] = SelStr(settings.YtDefaultQuality, "highres"),
                ["YT_SPEED"] = settings.YtDefaultSpeed.ToString(CultureInfo.InvariantCulture),
                ["CHK_YT_ENHANCER"] = Chk(settings.YtEnhancerEnabled),
                ["CHK_YT_AUTOPLAY"] = Chk(settings.YtDisableAutoplay),
                ["CHK_YT_PAUSE"] = Chk(settings.YtPauseOnTabSwitch),
                ["FOCUS_INPUT_DISPLAY"] = settings.FocusLocked ? "none" : "block",
                ["FOCUS_LOCKED_DISPLAY"] = settings.FocusLocked ? "block" : "none",
                ["FOCUS_DOMAINS"] = System.Web.HttpUtility.HtmlEncode(settings.FocusDomains ?? ""),
                ["CHK_YT_LOOP"] = Chk(settings.YtLoopVideo),
                ["CHK_UNHOOK_ENABLED"] = Chk(settings.UnhookEnabled),
                ["CHK_UNHOOK_SIDEBAR"] = Chk(settings.UnhookHideVideoSidebar),
                ["CHK_UNHOOK_RECOMMENDED"] = Chk(settings.UnhookHideRecommended),
                ["CHK_UNHOOK_LIVECHAT"] = Chk(settings.UnhookHideLiveChat),
                ["CHK_UNHOOK_PLAYLIST"] = Chk(settings.UnhookHidePlaylist),
                ["CHK_UNHOOK_FUNDRAISER"] = Chk(settings.UnhookHideFundraiser),
                ["CHK_UNHOOK_ENDFEED"] = Chk(settings.UnhookHideEndScreenFeed),
                ["CHK_UNHOOK_ENDCARDS"] = Chk(settings.UnhookHideEndScreenCards),
                ["CHK_UNHOOK_SHORTS"] = Chk(settings.UnhookHideShorts),
                ["CHK_UNHOOK_COMMENTS"] = Chk(settings.UnhookHideComments),
                ["CHK_UNHOOK_MIXES"] = Chk(settings.UnhookHideMixes),
                ["CHK_UNHOOK_MERCH"] = Chk(settings.UnhookHideMerch),
                ["CHK_UNHOOK_VIDEOINFO"] = Chk(settings.UnhookHideVideoInfo),
                ["CHK_UNHOOK_HOMEFEED"] = Chk(settings.UnhookHideHomeFeed),
                ["CHK_UNHOOK_TOPHEADER"] = Chk(settings.UnhookHideTopHeader),
                ["CHK_UNHOOK_NOTIFICATIONS"] = Chk(settings.UnhookHideNotifications),
                ["CHK_UNHOOK_INAPTSEARCH"] = Chk(settings.UnhookHideInaptSearch),
                ["CHK_UNHOOK_EXPLORE"] = Chk(settings.UnhookHideExplore),
                ["CHK_UNHOOK_MOREYT"] = Chk(settings.UnhookHideMoreYT),
                ["CHK_UNHOOK_SUBSCRIPTIONS"] = Chk(settings.UnhookHideSubscriptions),
                ["CHK_UNHOOK_AUTOPLAY"] = Chk(settings.UnhookDisableAutoplay),
                ["CHK_UNHOOK_ANNOTATIONS"] = Chk(settings.UnhookDisableAnnotations),
                ["KEYBOARD_SHORTCUTS_HTML"] = BuildShortcutsHtml(settings.CustomShortcuts),
                ["IPC_TOKEN"] = ipcToken,
            });
    }

    /// <summary>Builds the keyboard shortcuts HTML section.</summary>
    private static string BuildShortcutsHtml(Dictionary<string, string> customShortcuts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"section\" id=\"section-shortcuts\">");
        sb.AppendLine("    <div class=\"section-header\">Keyboard Shortcuts</div>");
        sb.AppendLine("    <div class=\"card\">");

        string? lastCategory = null;

        foreach (var def in ShortcutDefaults.All)
        {
            // Escape every user-supplied / template-supplied value before it ever reaches HTML.
            // Text content gets full HTML encoding (so < and > cannot inject tags).
            // Attribute values get the JS-in-attribute encoding (so quotes cannot break out).
            var escName = StrideBrowser.Helpers.JsEncoder.Encode(def.Name);
            var escLabel = StrideBrowser.Helpers.JsEncoder.HtmlEncode(def.Label);
            var escDescription = StrideBrowser.Helpers.JsEncoder.HtmlEncode(def.Description ?? string.Empty);
            var escCategory = StrideBrowser.Helpers.JsEncoder.HtmlEncode(def.Category);
            var escDefaultDisplay = StrideBrowser.Helpers.JsEncoder.Encode(FormatComboDisplay(def.DefaultCombo));

            // Category subsection header
            if (def.Category != lastCategory)
            {
                if (lastCategory is not null)
                    sb.AppendLine(); // spacing between categories
                sb.AppendLine($"        <div class=\"subsection-label\">{escCategory}</div>");
                lastCategory = def.Category;
            }

            var currentCombo = ShortcutDefaults.GetCombo(def.Name, customShortcuts);
            var isCustom = customShortcuts.ContainsKey(def.Name);
            var displayCombo = FormatComboDisplay(currentCombo);
            // Escape user-supplied combo values before embedding in HTML/JS attributes.
            var escCombo = StrideBrowser.Helpers.JsEncoder.Encode(currentCombo);
            var escDisplay = StrideBrowser.Helpers.JsEncoder.Encode(displayCombo);
            var escDefault = StrideBrowser.Helpers.JsEncoder.Encode(def.DefaultCombo);

            sb.AppendLine("        <div class=\"setting-row\">");
            sb.AppendLine("            <div class=\"setting-info\">");
            sb.AppendLine($"                <div class=\"setting-label\">{escLabel}</div>");
            if (!string.IsNullOrEmpty(def.Description))
                sb.AppendLine($"                <div class=\"setting-desc\">{escDescription}</div>");
            sb.AppendLine("            </div>");
            sb.AppendLine("            <div class=\"shortcut-controls\">");

            // Reset button (only shown if custom)
            if (isCustom)
            {
                sb.AppendLine($"                <button class=\"shortcut-reset\" onclick=\"resetShortcut('{escName}', this.nextElementSibling)\" title=\"Reset to {escDefaultDisplay}\">Reset</button>");
            }

            // Clickable combo badge
            sb.AppendLine($"                <span class=\"shortcut-badge\" data-action=\"{escCombo}\" data-combo=\"{escCombo}\" data-default-combo=\"{escDefault}\" onclick=\"startRecording(this)\">{escDisplay}</span>");

            sb.AppendLine("            </div>");
            sb.AppendLine("        </div>");
        }

        sb.AppendLine("    </div>");
        sb.AppendLine("</div>");

        return sb.ToString();
    }

    /// <summary>Formats a shortcut combo for display.</summary>
    private static string FormatComboDisplay(string combo)
    {
        // "Ctrl+Shift+T" → "Ctrl + Shift + T"
        return combo.Replace("+", " + ");
    }

    /// <summary>Converts a hex color to an RGB string.</summary>
    private static string HexToRgb(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return "127,184,154";
        try
        {
            var r = Convert.ToInt32(hex[..2], 16);
            var g = Convert.ToInt32(hex[2..4], 16);
            var b = Convert.ToInt32(hex[4..6], 16);
            return $"{r},{g},{b}";
        }
        catch (FormatException) { return "127,184,154"; }
        catch (OverflowException) { return "127,184,154"; }
    }
}
