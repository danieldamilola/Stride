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
    /// Returns a JS string that writes the unhook config into localStorage, or into
    /// window.__STRIDE_UNHOOK when the page refuses the write, then runs the unhook
    /// script. Returns empty string if unhook is disabled.
    /// <paramref name="forceConfig"/> additionally points the running document at the
    /// new config, so a live settings change takes effect without a navigation.
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
            sidebar = s.UnhookHideVideoSidebar || s.UnhookHideRecommended,
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

        // The old Hide recommended videos toggle is folded into 'sidebar'. It emptied
        // the sidebar's contents but left the column, so the video stayed narrow
        // beside an empty strip. Its stored setting is still honoured above so an
        // existing config keeps working, but the script only sees one key.
        // The sidebar feature is sent again, rebuilt without the '#primary
        // max-width' rule that caused its scroll problems.

        var json = JsonSerializer.Serialize(configObj);

        // Config persists in the page's localStorage so live-reloaded values
        // survive SPA navigations, which never re-run this script. A new document
        // always gets the settings the browser currently holds rather than whatever
        // was left behind: a settings change made while no YouTube tab was open has
        // no page to re-inject into, and seeding only when the key is missing would
        // carry the old preferences into every later visit.
        //
        // The page can refuse the write. Storage is denied outright by blocked third
        // party storage and in private mode, and refused by quota on a full one. The
        // script reads window.__STRIDE_UNHOOK first and localStorage second, so a
        // refused write would leave it with nothing and every setting would read as
        // off with nothing to say why. The refusal therefore falls back to the
        // in-document variable, which costs the page its persistence across
        // navigations but not the user's choices.
        var configLine =
            "try { localStorage.setItem('__stride_unhook', '" + json + "'); }" +
            " catch (e) { window.__STRIDE_UNHOOK = " + json + "; }";

        // Live reload also points the running document at the new config, so the
        // running instance reads the new values without waiting for a navigation.
        if (forceConfig)
            configLine += " window.__STRIDE_UNHOOK = " + json + ";";

        // The unhook script is injected again. Shorts and sidebar are gone from
        // the config; see the notes above.
        return configLine + "\n" + ResourceLoader.Load("Resources.Scripts.youtube-unhook.js");
    }
}
