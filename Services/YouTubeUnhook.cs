using System.Text.Json;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// YouTube Unhook - hides distracting YouTube UI elements.
/// Injects a config object then the standalone unhook script.
/// </summary>
public sealed class YouTubeUnhook
{
    /// <summary>
    /// Returns a JS string that seeds the unhook config into localStorage,
    /// sets window.__STRIDE_UNHOOK for the running document, then runs the
    /// unhook script. Returns empty string if unhook is disabled.
    /// <paramref name="forceConfig"/> overwrites the persisted config for live
    /// reload; initial injection only seeds it so live changes survive
    /// navigation instead of being reverted by the registered bootstrap.
    /// </summary>
    public string GetScript(BrowserSettings s, bool forceConfig = false)
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

        var json = JsonSerializer.Serialize(configObj);

        // Config persists in the page's localStorage so live-reloaded values
        // survive SPA navigations. Initial injection only seeds it.
        var seed = forceConfig ? "" : "if (!localStorage.getItem('__stride_unhook')) ";
        var configLine =
            "try { " + seed + "localStorage.setItem('__stride_unhook', '" + json + "'); } catch (e) {}";

        // Live reload also points the running document at the new config.
        if (forceConfig)
            configLine += " window.__STRIDE_UNHOOK = " + json + ";";

        var script = ResourceLoader.Load("Resources.Scripts.youtube-unhook.js");

        return configLine + "\n" + script;
    }
}
