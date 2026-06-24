using System.Text.Json;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// YouTube Unhook — hides distracting YouTube UI elements.
/// Injects a config object then the standalone unhook script.
/// </summary>
public sealed class YouTubeUnhook
{
    /// <summary>
    /// Returns a JS string that sets window.__SPUR_UNHOOK config
    /// then immediately runs the unhook script.
    /// Returns empty string if unhook is disabled.
    /// </summary>
    public string GetScript(BrowserSettings s)
    {
        if (!s.UnhookEnabled) return "";

        var configObj = new
        {
            homeFeed = s.UnhookHideHomeFeed,
            shorts = s.UnhookHideShorts,
            mixes = s.UnhookHideMixes,
            explore = s.UnhookHideExplore,
            subscriptions = s.UnhookHideSubscriptions,
            sidebar = s.UnhookHideVideoSidebar,
            recommended = s.UnhookHideRecommended,
            comments = s.UnhookHideComments,
            videoInfo = s.UnhookHideVideoInfo,
            liveChat = s.UnhookHideLiveChat,
            playlist = s.UnhookHidePlaylist,
            merch = s.UnhookHideMerch,
            fundraiser = s.UnhookHideFundraiser,
            endFeed = s.UnhookHideEndScreenFeed,
            endCards = s.UnhookHideEndScreenCards,
            annotations = s.UnhookDisableAnnotations,
            autoplay = s.UnhookDisableAutoplay,
            topHeader = s.UnhookHideTopHeader,
            notifications = s.UnhookHideNotifications,
            moreYT = s.UnhookHideMoreYT,
            inaptSearch = s.UnhookHideInaptSearch
        };

        var config = $"window.__SPUR_UNHOOK = {JsonSerializer.Serialize(configObj)};";
        var script = ResourceLoader.Load("Resources.Scripts.youtube-unhook.js");

        return config + "\n" + script;
    }
}
