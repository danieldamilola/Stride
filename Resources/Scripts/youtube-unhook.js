// Stride Browser - YouTube Unhook
// CSS class-toggle pattern inspired by unhookng (github.com/TheArchons/unhookng).
// Reads window.__STRIDE_UNHOOK config injected by C#.
// Toggles classes on <html> to gate pre-written CSS rules.
(function() {
    'use strict';

    var host = location.hostname;
    if (host !== 'youtube.com' && host !== 'www.youtube.com' && !host.endsWith('.youtube.com')) return;

    // Config source: live re-injection sets window.__STRIDE_UNHOOK and wins;
    // otherwise the config seeded in localStorage at document creation applies.
    function readConfig() {
        if (window.__STRIDE_UNHOOK) return window.__STRIDE_UNHOOK;
        try {
            var raw = localStorage.getItem('__stride_unhook');
            if (raw) return JSON.parse(raw);
        } catch (e) {}
        return {};
    }

    // Shared config object, mutated in place on live reload so listeners that
    // closed over it always see current values.
    var cfg = {};
    function refreshConfig() {
        var latest = readConfig();
        for (var k in cfg) delete cfg[k];
        for (var k2 in latest) cfg[k2] = latest[k2];
        return cfg;
    }
    refreshConfig();

    // Live reload: re-read config and re-apply without re-registering
    // observers or listeners, which would stack on every settings change.
    if (window.__STRIDE_UNHOOK_LOADED) {
        refreshConfig();
        applyClasses();
        runJsActions();
        return;
    }
    window.__STRIDE_UNHOOK_LOADED = true;

    // ── Static CSS - all rules pre-written, gated by html.stride-unhook-* classes ──
    var CSS = [
        // ─ Home Feed ─
        'html.stride-unhook-homeFeed ytd-browse[page-subtype="home"] ytd-rich-grid-renderer { display:none!important }',
        'html.stride-unhook-homeFeed ytd-browse[page-subtype="home"] #contents.ytd-rich-grid-renderer { display:none!important }',
        'html.stride-unhook-homeFeed ytd-rich-section-renderer { display:none!important }',

        // ─ Shorts ─
        'html.stride-unhook-shorts ytd-reel-shelf-renderer { display:none!important }',
        'html.stride-unhook-shorts ytd-rich-shelf-renderer[is-shorts] { display:none!important }',
        'html.stride-unhook-shorts ytd-item-section-renderer:has(ytd-reel-shelf-renderer) { display:none!important }',
        'html.stride-unhook-shorts ytd-guide-entry-renderer:has(a[title="Shorts"]) { display:none!important }',
        'html.stride-unhook-shorts ytd-mini-guide-entry-renderer[aria-label="Shorts"] { display:none!important }',
        'html.stride-unhook-shorts ytd-rich-item-renderer:has(a[href*="/shorts/"]) { display:none!important }',
        'html.stride-unhook-shorts ytd-video-renderer:has(a[href*="/shorts/"]) { display:none!important }',
        'html.stride-unhook-shorts ytd-grid-video-renderer:has(a[href*="/shorts/"]) { display:none!important }',
        'html.stride-unhook-shorts ytd-grid-video-renderer:has([overlay-style="SHORTS"]) { display:none!important }',
        'html.stride-unhook-shorts yt-tab-shape[tab-title="Shorts"] { display:none!important }',
        'html.stride-unhook-shorts yt-chip-cloud-chip-renderer:has([title="Shorts"]) { display:none!important }',
        'html.stride-unhook-shorts ytd-reel-shelf-renderer.ytd-item-section-renderer { display:none!important }',

        // ─ Mixes ─
        'html.stride-unhook-mixes ytd-radio-renderer { display:none!important }',
        'html.stride-unhook-mixes ytd-compact-radio-renderer { display:none!important }',

        // ─ Explore / Trending ─
        'html.stride-unhook-explore ytd-guide-entry-renderer:has(a[href*="/feed/explore"]) { display:none!important }',
        'html.stride-unhook-explore ytd-guide-entry-renderer:has(a[href*="/feed/trending"]) { display:none!important }',

        // ─ Subscriptions ─
        'html.stride-unhook-subscriptions ytd-browse[page-subtype="subscriptions"] #contents { display:none!important }',

        // ─ Video Sidebar & Related ─
        'html.stride-unhook-sidebar ytd-watch-flexy #secondary { display:none!important }',
        'html.stride-unhook-sidebar ytd-watch-flexy #secondary-inner { display:none!important }',
        'html.stride-unhook-sidebar ytd-watch-flexy #primary { max-width:100%!important }',
        'html.stride-unhook-sidebar ytd-watch-flexy[flexy] #primary { max-width:none!important }',
        'html.stride-unhook-sidebar ytd-watch-flexy { --ytd-watch-flexy-sidebar-width:0px!important }',

        'html.stride-unhook-recommended #related { display:none!important }',
        'html.stride-unhook-recommended ytd-watch-next-secondary-results-renderer { display:none!important }',

        // ─ Comments ─
        'html.stride-unhook-comments #comments { display:none!important }',
        'html.stride-unhook-comments ytd-comments { display:none!important }',
        'html.stride-unhook-comments ytd-engagement-panel-section-list-renderer[target-id="engagement-panel-comments-section"] { display:none!important }',

        // ─ Video Info ─
        'html.stride-unhook-videoInfo #above-the-fold { display:none!important }',
        'html.stride-unhook-videoInfo ytd-watch-metadata { display:none!important }',
        'html.stride-unhook-videoInfo #below { display:none!important }',

        // ─ Live Chat ─
        'html.stride-unhook-liveChat ytd-live-chat-frame { display:none!important }',
        'html.stride-unhook-liveChat #chat-container { display:none!important }',
        'html.stride-unhook-liveChat #chat { display:none!important }',

        // ─ Playlist ─
        'html.stride-unhook-playlist ytd-playlist-panel-renderer { display:none!important }',
        'html.stride-unhook-playlist #playlist { display:none!important }',

        // ─ Merch / Offers ─
        'html.stride-unhook-merch ytd-merch-shelf-renderer { display:none!important }',
        'html.stride-unhook-merch ytd-offer-module-renderer { display:none!important }',
        'html.stride-unhook-merch ytd-ticket-shelf-renderer { display:none!important }',
        'html.stride-unhook-merch ytd-brand-video-shelf-renderer { display:none!important }',

        // ─ Fundraiser ─
        'html.stride-unhook-fundraiser ytd-donation-shelf-renderer { display:none!important }',
        'html.stride-unhook-fundraiser ytd-donation-unavailable-renderer { display:none!important }',

        // ─ End Screen ─
        'html.stride-unhook-endFeed .ytp-endscreen-content { display:none!important }',
        'html.stride-unhook-endFeed .ytp-endscreen-previous { display:none!important }',
        'html.stride-unhook-endFeed .ytp-endscreen-next { display:none!important }',

        // ─ End Cards / Annotations ─
        'html.stride-unhook-endCards .ytp-ce-element { display:none!important }',
        'html.stride-unhook-endCards .ytp-ce-covering-overlay { display:none!important }',
        'html.stride-unhook-endCards .ytp-cards-teaser { display:none!important }',
        'html.stride-unhook-endCards .ytp-ce-covering-image { display:none!important }',

        'html.stride-unhook-annotations .annotation { display:none!important }',
        'html.stride-unhook-annotations .iv-branding { display:none!important }',
        'html.stride-unhook-annotations .ytp-cards-button { display:none!important }',

        // ─ Top Header ─
        'html.stride-unhook-topHeader #masthead-container { display:none!important }',
        'html.stride-unhook-topHeader #masthead { display:none!important }',
        'html.stride-unhook-topHeader ytd-masthead { display:none!important }',
        'html.stride-unhook-topHeader { --ytd-masthead-height:0px!important }',
        'html.stride-unhook-topHeader body { margin-top:0!important }',

        // ─ Notifications ─
        'html.stride-unhook-notifications ytd-notification-topbar-button-renderer { display:none!important }',

        // ─ Inapt Search Results ─
        'html.stride-unhook-inaptSearch ytd-shelf-renderer[modern-typography]:has(> #dismissible > ytd-rich-shelf-renderer) { display:none!important }',
        'html.stride-unhook-inaptSearch ytd-horizontal-card-list-renderer { display:none!important }',

        // ─ Channel Watermark (new) ─
        'html.stride-unhook-endCards .ytp-ce-channel-watermark { display:none!important }',

        // ─ Voice Search (new) ─
        'html.stride-unhook-notifications #voice-search-button { display:none!important }',

        // ─ Thanks / Clip buttons (new) ─
        'html.stride-unhook-merch yt-button-shape[id="super-thanks-button"] { display:none!important }',
        'html.stride-unhook-merch yt-button-shape[id="clip-button"] { display:none!important }',

        // ─ Video Wall / Autoplay overlay at end of video ─
        'html.stride-unhook-endFeed .ytp-suggestion-set { display:none!important }',
        'html.stride-unhook-endFeed .html5-endscreen { display:none!important }',
        'html.stride-unhook-endFeed .videowall-endscreen { display:none!important }',

        // ─ Shorts in search results (additional) ─
        'html.stride-unhook-shorts ytd-search ytd-video-renderer:has([overlay-style="SHORTS"]) { display:none!important }',
        'html.stride-unhook-shorts ytd-reel-shelf-renderer.ytd-structured-description-content-renderer { display:none!important }',

        // ─ Promoted / In-feed ads (additional) ─
        'html.stride-unhook-merch ytd-promoted-sparkles-web-renderer { display:none!important }',
        'html.stride-unhook-merch ytd-display-ad-renderer { display:none!important }',
        'html.stride-unhook-merch ytd-promoted-video-renderer { display:none!important }',
        'html.stride-unhook-merch ytd-ad-slot-renderer { display:none!important }'
    ].join('\n');

    // ── Inject static CSS once ───────────────────────────────────────
    function injectStyleSheet() {
        if (document.getElementById('__stride_unhook')) return;
        var target = document.head || document.documentElement;
        if (!target) return; // safety - will retry via DOMContentLoaded
        var style = document.createElement('style');
        style.id = '__stride_unhook';
        style.textContent = CSS;
        target.appendChild(style);
    }

    // ── Toggle classes on <html> based on config ─────────────────────
    var FEATURES = [
        'homeFeed', 'shorts', 'mixes', 'explore', 'subscriptions',
        'sidebar', 'recommended', 'comments', 'videoInfo', 'liveChat',
        'playlist', 'merch', 'fundraiser', 'endFeed', 'endCards',
        'annotations', 'topHeader', 'notifications', 'inaptSearch'
    ];

    function applyClasses() {
        var html = document.documentElement;
        if (!html) return;
        for (var i = 0; i < FEATURES.length; i++) {
            var key = FEATURES[i];
            var className = 'stride-unhook-' + key;
            if (cfg[key]) {
                html.classList.add(className);
            } else {
                html.classList.remove(className);
            }
        }
    }

    // ── Shorts Redirect ──────────────────────────────────────────────
    function checkShortsRedirect() {
        if (cfg.shorts && location.pathname.startsWith('/shorts/')) {
            var videoId = location.pathname.split('/shorts/')[1];
            if (videoId) {
                location.replace('/watch?v=' + videoId);
                return true;
            }
        }
        return false;
    }

    // ── JS-only actions (can't be done with CSS) ─────────────────────
    function runJsActions() {
        // "More from YouTube" section - match by section links first, since the
        // header text is locale-dependent, with the English text as fallback.
        if (cfg.moreYT) {
            var sections = document.querySelectorAll('ytd-guide-section-renderer');
            for (var i = 0; i < sections.length; i++) {
                var header = sections[i].querySelector('#guide-section-title');
                var text = header ? (header.textContent || '').trim().toLowerCase() : '';
                var isMoreFromYt = text === 'more from youtube' ||
                    !!sections[i].querySelector('a[href*="/premium"], a[href*="/creators"]');
                if (isMoreFromYt) {
                    sections[i].style.setProperty('display', 'none', 'important');
                }
            }
        }

        // Disable autoplay toggle. Skip when the YouTube Enhancer is enabled
        // with autoplay off, since it already owns this toggle.
        if (cfg.autoplay) {
            var enhancerHandles = window.__STRIDE_YT_CONFIG &&
                window.__STRIDE_YT_CONFIG.enabled &&
                window.__STRIDE_YT_CONFIG.disableAuto;
            if (!enhancerHandles) {
                var btn = document.querySelector('.ytp-autonav-toggle-button');
                if (btn && btn.getAttribute('aria-checked') === 'true') {
                    btn.click();
                }
            }
        }

        // Force Theater Mode if sidebar is hidden
        if (cfg.sidebar) {
            var flexy = document.querySelector('ytd-watch-flexy');
            if (flexy && !flexy.hasAttribute('theater')) {
                var sizeBtn = document.querySelector('.ytp-size-button');
                if (sizeBtn) sizeBtn.click();
            }
        }
    }

    // ── Targeted MutationObserver ─────────────────────────────────────
    function startObserving() {
        var app = document.querySelector('ytd-app');
        if (!app) {
            var target = document.body || document.documentElement;
            if (!target) return;
            var waitObserver = new MutationObserver(function() {
                var ytApp = document.querySelector('ytd-app');
                if (ytApp) {
                    waitObserver.disconnect();
                    observeApp(ytApp);
                }
            });
            waitObserver.observe(target, { childList: true, subtree: true });
            return;
        }
        observeApp(app);
    }

    function observeApp(app) {
        var observer = new MutationObserver(function(mutations) {
            var needsJsHides = false;
            for (var m = 0; m < mutations.length; m++) {
                var nodes = mutations[m].addedNodes;
                for (var n = 0; n < nodes.length; n++) {
                    var node = nodes[n];
                    if (node.nodeType !== 1) continue;
                    var tag = node.tagName;
                    if (tag === 'YTD-GUIDE-SECTION-RENDERER' ||
                        tag === 'YTD-GUIDE-ENTRY-RENDERER') {
                        needsJsHides = true;
                    }
                    if (tag === 'YTP-AUTONAV-TOGGLE-BUTTON-RENDERER' ||
                        (node.classList && node.classList.contains('ytp-autonav-toggle-button'))) {
                        needsJsHides = true;
                    }
                }
            }
            if (needsJsHides) runJsActions();
        });
        observer.observe(app, { childList: true, subtree: true });
    }

    // ── SPA Navigation ───────────────────────────────────────────────
    window.addEventListener('yt-navigate-finish', function() {
        if (checkShortsRedirect()) return;
        applyClasses();
        runJsActions();
    });
    window.addEventListener('yt-page-data-updated', function() {
        runJsActions();
    });
    window.addEventListener('yt-navigate-start', function() {
        checkShortsRedirect();
        applyClasses();
    });

    // ── Boot: defer ALL DOM access until the document exists ─────────
    function boot() {
        if (checkShortsRedirect()) return;
        injectStyleSheet();
        applyClasses();
        runJsActions();
        startObserving();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }
})();

