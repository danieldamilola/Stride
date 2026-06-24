using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the settings page HTML.</summary>
public sealed class SettingsPage
{
    public string Render(BrowserSettings settings)
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
                ["CHK_HTTPS"] = Chk(settings.ForceHttps),
                ["CHK_CLEAR"] = Chk(settings.ClearDataOnExit),
                ["CHK_DUPES"] = Chk(settings.BlockDuplicateTabs),
                ["SIDEBAR_LEFT"] = settings.IsSidebarOnRight ? "" : " selected",
                ["SIDEBAR_RIGHT"] = settings.IsSidebarOnRight ? " selected" : "",
                ["ACCENT_COLOR"] = settings.AccentColor,
                ["YT_QUALITY_AUTO"] = SelStr(settings.YtDefaultQuality, "auto"),
                ["YT_QUALITY_HIGHEST"] = SelStr(settings.YtDefaultQuality, "highest"),
                ["YT_QUALITY_TINY"] = SelStr(settings.YtDefaultQuality, "tiny"),
                ["YT_QUALITY_SMALL"] = SelStr(settings.YtDefaultQuality, "small"),
                ["YT_QUALITY_MEDIUM"] = SelStr(settings.YtDefaultQuality, "medium"),
                ["YT_QUALITY_LARGE"] = SelStr(settings.YtDefaultQuality, "large"),
                ["YT_QUALITY_720"] = SelStr(settings.YtDefaultQuality, "hd720"),
                ["YT_QUALITY_1080"] = SelStr(settings.YtDefaultQuality, "hd1080"),
                ["YT_QUALITY_1440"] = SelStr(settings.YtDefaultQuality, "hd1440"),
                ["YT_QUALITY_4K"] = SelStr(settings.YtDefaultQuality, "highres"),
                ["YT_SPEED"] = settings.YtDefaultSpeed.ToString(),
                ["CHK_YT_AUTOPLAY"] = Chk(settings.YtDisableAutoplay),
                ["CHK_YT_PAUSE"] = Chk(settings.YtPauseOnTabSwitch),
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
            });
    }
}
