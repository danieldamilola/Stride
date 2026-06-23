using SpurBrowser.Helpers;
using SpurBrowser.Models;

namespace SpurBrowser.Services;

/// <summary>
/// Generates CSS + JS that gets injected into YouTube pages to hide
/// distracting UI elements (home feed, sidebar, shorts, comments, etc.).
/// The outer wrapper lives in <c>Resources/Scripts/youtube-unhook.js</c>;
/// this service generates the dynamic CSS and JS blocks from settings.
/// </summary>
public sealed class YouTubeUnhook
{
    private const string TemplatePath = "Resources.Scripts.youtube-unhook.js";

    /// <summary>Returns CSS+JS to inject on youtube.com pages.</summary>
    public string GetScript(BrowserSettings settings)
    {
        var css = BuildCss(settings);
        var js = BuildJs(settings);

        return ResourceLoader.LoadTemplate(TemplatePath, new Dictionary<string, string>
        {
            ["CSS"] = css,
            ["JS"]  = js,
        });
    }

    private sealed record CssRule(Func<BrowserSettings, bool> Guard, string Css);

    private static readonly CssRule[] CssRules =
    [
        new(s => s.UnhookHideHomeFeed, "ytd-browse[page-subtype=\"home\"] #contents.ytd-rich-grid-renderer { display:none!important }"),
        new(s => s.UnhookHideVideoSidebar, "#secondary { display:none!important } #primary { max-width:100%!important; width:100%!important }"),
        new(s => s.UnhookHideRecommended, "#related ytd-compact-video-renderer, #related ytd-compact-autoplay-renderer { display:none!important }"),
        new(s => s.UnhookHideLiveChat, "ytd-live-chat-frame, #chat-container { display:none!important }"),
        new(s => s.UnhookHidePlaylist, "ytd-playlist-panel-renderer { display:none!important }"),
        new(s => s.UnhookHideFundraiser, "ytd-donation-shelf-renderer { display:none!important }"),
        new(s => s.UnhookHideEndScreenFeed, ".ytp-endscreen-content { display:none!important }"),
        new(s => s.UnhookHideEndScreenCards, ".ytp-ce-element, .ytp-ce-covering-overlay { display:none!important }"),
        new(s => s.UnhookHideShorts, "ytd-reel-shelf-renderer { display:none!important } ytd-rich-shelf-renderer[is-shorts] { display:none!important } ytd-item-section-renderer:has(ytd-reel-shelf-renderer) { display:none!important } ytd-guide-entry-renderer:has(a[title=\"Shorts\"]) { display:none!important } ytd-mini-guide-entry-renderer[aria-label=\"Shorts\"] { display:none!important } ytd-rich-item-renderer:has(a[href^=\"/shorts/\"]) { display:none!important } ytd-video-renderer:has(a[href^=\"/shorts/\"]) { display:none!important } ytd-grid-video-renderer:has([overlay-style=\"SHORTS\"]) { display:none!important } yt-tab-shape[tab-title=\"Shorts\"] { display:none!important } [is-shorts] { display:none!important }"),
        new(s => s.UnhookHideComments, "#comments, ytd-comments { display:none!important }"),
        new(s => s.UnhookHideMixes, "ytd-radio-renderer { display:none!important }"),
        new(s => s.UnhookHideMerch, "ytd-merch-shelf-renderer, ytd-offer-module-renderer, ytd-ticket-shelf-renderer { display:none!important }"),
        new(s => s.UnhookHideVideoInfo, "#above-the-fold { display:none!important }"),
        new(s => s.UnhookHideTopHeader, "#masthead-container { display:none!important } body { margin-top:0!important }"),
        new(s => s.UnhookHideNotifications, "ytd-notification-topbar-button-renderer { display:none!important }"),
        new(s => s.UnhookHideInaptSearch, "ytd-shelf-renderer[modern-typography]:has(> #dismissible > ytd-rich-shelf-renderer) { display:none!important }"),
        new(s => s.UnhookHideSubscriptions, "ytd-browse[page-subtype=\"subscriptions\"] #contents { display:none!important }"),
        new(s => s.UnhookDisableAnnotations, ".annotation, .iv-branding { display:none!important }"),
    ];

    private static string BuildCss(BrowserSettings s)
    {
        return string.Join(" ", CssRules
            .Where(r => r.Guard(s))
            .Select(r => r.Css));
    }

    private static string BuildJs(BrowserSettings s)
    {
        var parts = new List<string>();

        if (s.UnhookHideExplore)
        {
            parts.Add("""
                (function hideGuideSection(keywords) {
                    var sections = document.querySelectorAll('ytd-guide-section-renderer');
                    sections.forEach(function(sec) {
                        var entries = sec.querySelectorAll('ytd-guide-entry-renderer');
                        entries.forEach(function(e) {
                            var title = (e.textContent || '').trim();
                            for (var i = 0; i < keywords.length; i++) {
                                if (title === keywords[i]) {
                                    e.style.display = 'none';
                                }
                            }
                        });
                    });
                })(['Explore', 'Trending']);
            """);
        }

        if (s.UnhookHideMoreYT)
        {
            parts.Add("""
                (function hideMoreFromYouTube() {
                    var sections = document.querySelectorAll('ytd-guide-section-renderer');
                    sections.forEach(function(sec) {
                        var header = sec.querySelector('#guide-section-title');
                        if (header && (header.textContent || '').trim() === 'More from YouTube') {
                            sec.style.display = 'none';
                        }
                    });
                })();
            """);
        }

        if (s.UnhookDisableAutoplay)
        {
            parts.Add("""
                (function disableAutoplay() {
                    var btn = document.querySelector('.ytp-autonav-toggle-button');
                    if (btn && btn.getAttribute('aria-checked') === 'true') {
                        btn.click();
                    }
                })();
            """);
        }

        if (s.UnhookHideShorts)
        {
            parts.Add("""
                (function hideShorts() {
                    function doHide() {
                        document.querySelectorAll('ytd-reel-shelf-renderer').forEach(function(el) { el.style.display = 'none'; });
                        document.querySelectorAll('ytd-rich-shelf-renderer[is-shorts]').forEach(function(el) { el.style.display = 'none'; });
                        document.querySelectorAll('[is-shorts]').forEach(function(el) { el.style.display = 'none'; });
                        document.querySelectorAll('ytd-guide-entry-renderer').forEach(function(el) {
                            var a = el.querySelector('a[title="Shorts"]');
                            if (a) el.style.display = 'none';
                        });
                        document.querySelectorAll('ytd-mini-guide-entry-renderer[aria-label="Shorts"]').forEach(function(el) { el.style.display = 'none'; });
                        document.querySelectorAll('ytd-rich-item-renderer, ytd-video-renderer, ytd-grid-video-renderer').forEach(function(el) {
                            if (el.querySelector('a[href*="/shorts/"]')) el.style.display = 'none';
                        });
                        document.querySelectorAll('ytd-item-section-renderer, ytd-shelf-renderer').forEach(function(el) {
                            if (el.querySelector('ytd-reel-shelf-renderer')) el.style.display = 'none';
                        });
                    }
                    doHide();
                    if (!window.__spurShortsObserver) {
                        var _t;
                        window.__spurShortsObserver = new MutationObserver(function() {
                            clearTimeout(_t); _t = setTimeout(doHide, 300);
                        });
                        var target = document.querySelector('ytd-app') || document.body;
                        window.__spurShortsObserver.observe(target, { childList: true, subtree: true });
                    }
                })();
            """);
        }

        if (s.UnhookHideExplore || s.UnhookHideMoreYT)
        {
            var observerBody = new List<string>();

            if (s.UnhookHideExplore)
            {
                observerBody.Add("""
                    var entries = document.querySelectorAll('ytd-guide-entry-renderer');
                    entries.forEach(function(e) {
                        var t = (e.textContent || '').trim();
                        if (t === 'Explore' || t === 'Trending') e.style.display = 'none';
                    });
                """);
            }

            if (s.UnhookHideMoreYT)
            {
                observerBody.Add("""
                    var secs = document.querySelectorAll('ytd-guide-section-renderer');
                    secs.forEach(function(sec) {
                        var h = sec.querySelector('#guide-section-title');
                        if (h && (h.textContent || '').trim() === 'More from YouTube') sec.style.display = 'none';
                    });
                """);
            }

            var joined = string.Join("\n", observerBody);

            parts.Add(
                "(function observeGuide() {" +
                "if (window.__spurUnhookGuideObserver) return;" +
                "var guide = document.querySelector('tp-yt-app-drawer, ytd-app');" +
                "if (!guide) return;" +
                "window.__spurUnhookGuideObserver = new MutationObserver(function() {" +
                joined +
                "});" +
                "window.__spurUnhookGuideObserver.observe(guide, { childList: true, subtree: true });" +
                "})();");
        }

        return string.Join("\n", parts);
    }
}
