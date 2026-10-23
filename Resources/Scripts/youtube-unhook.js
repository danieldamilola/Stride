// Umber Browser - YouTube Unhook
// CSS class-toggle pattern inspired by unhookng (github.com/TheArchons/unhookng).
// Reads window.__UMBER_UNHOOK config injected by C#.
// Toggles classes on <html> to gate pre-written CSS rules.
(function() {
    'use strict';

    var host = location.hostname;
    if (host !== 'youtube.com' && !host.endsWith('.youtube.com')) return;

    // Embedded players get none of this and have no ytd-app. The script is
    // registered for every document on this host, so without this the
    // stylesheet and a permanently armed observer are injected into every
    // /embed/ frame for nothing.
    if (location.pathname.startsWith('/embed/')) return;

    // Class applied to guide sections this script hides, so the hiding is
    // reversible at runtime. See the moreYT rule in the stylesheet.
    var HIDE_SECTION_CLASS = 'umber-unhook-hide-section';

    // Config source: live re-injection sets window.__UMBER_UNHOOK and wins;
    // otherwise the config seeded in localStorage at document creation applies.
    function readConfig() {
        if (window.__UMBER_UNHOOK) return window.__UMBER_UNHOOK;
        try {
            var raw = localStorage.getItem('__umber_unhook');
            if (raw) return JSON.parse(raw);
        } catch (e) {}
        return {};
    }

    // Shared config object, mutated in place on live reload so listeners that
    // closed over it always see current values.
    if (!window.__UMBER_UNHOOK_CFG) window.__UMBER_UNHOOK_CFG = {};
    var cfg = window.__UMBER_UNHOOK_CFG;
    function refreshConfig() {
        var latest = readConfig();
        for (var k in cfg) delete cfg[k];
        for (var k2 in latest) cfg[k2] = latest[k2];
        return cfg;
    }
    refreshConfig();

    // Whether this document already has the script. Decided here, acted on below.
    // It has to be decided after CSS and FEATURES are assigned, not before:
    // var is hoisted but unassigned, so running the re-injection path above those
    // definitions called applyClasses with FEATURES undefined and threw a TypeError
    // on every settings change, and handed injectStyleSheet an undefined stylesheet
    // whenever the style node had been removed.
    var isReinjection = !!window.__UMBER_UNHOOK_LOADED;

    // ── Static CSS - all rules pre-written, gated by html.umber-unhook-* classes ──
    var CSS = [
        // ─ Home Feed ─
        'html.umber-unhook-homeFeed ytd-browse[page-subtype="home"] ytd-rich-grid-renderer { display:none!important }',
        'html.umber-unhook-homeFeed ytd-browse[page-subtype="home"] #contents.ytd-rich-grid-renderer { display:none!important }',
        'html.umber-unhook-homeFeed ytd-rich-section-renderer { display:none!important }',

        // ─ Shorts ─────────────────────────────────────────────────────
        // What broke the feed before, and what this avoids:
        //
        // The old rule was
        //   ytd-item-section-renderer:has(ytd-reel-shelf-renderer)
        // YouTube wraps ordinary home-feed videos in item sections too, so
        // that hid real feed content along with the shelf and cut the page's
        // scroll height. The feed then had nothing to scroll.
        //
        // Every :has() below uses the child combinator, so it only matches a
        // direct-child relationship and cannot wander the feed tree. The
        // [is-shorts] attribute is the marker YouTube itself puts on the Shorts
        // shelf, which is why these survive UI refreshes: it is an API contract,
        // not a styling detail.
        //
        // The history page is excluded so Shorts you have already watched can
        // still be removed from your history.

        // The Shorts shelf on the home feed. The direct-child path stops this
        // from matching any other kind of shelf.
        'html.umber-unhook-shorts ytd-rich-shelf-renderer[is-shorts] { display:none!important }',
        'html.umber-unhook-shorts ytd-rich-section-renderer:has(> #contents > ytd-rich-shelf-renderer[is-shorts]) { display:none!important }',

        // Individual Shorts sitting in the home feed grid among normal videos.
        'html.umber-unhook-shorts ytd-browse[page-subtype="home"] ytd-rich-item-renderer[is-slim-media][rendered-from-rich-grid] { display:none!important }',

        // Carousel shelves outside the home feed, e.g. Subscriptions.
        'html.umber-unhook-shorts ytd-browse:not([page-subtype="history"]) ytd-reel-shelf-renderer { display:none!important }',

        // Left navigation, expanded and collapsed.
        'html.umber-unhook-shorts ytd-guide-entry-renderer:has(> a[title="Shorts"]) { display:none!important }',
        'html.umber-unhook-shorts ytd-mini-guide-entry-renderer:has(> a[aria-label="Shorts"]) { display:none!important }',

        // Channel page tab and the Shorts filter chip.
        'html.umber-unhook-shorts yt-tab-shape[tab-title="Shorts"] { display:none!important }',
        'html.umber-unhook-shorts yt-chip-cloud-chip-renderer:has(> #chip-container > yt-formatted-string[title="Shorts"]) { display:none!important }',

        // Search results: the shelf and any individual Short.
        'html.umber-unhook-shorts ytd-search ytd-reel-shelf-renderer { display:none!important }',
        'html.umber-unhook-shorts ytd-search ytd-video-renderer:has(> a[href^="/shorts/"]) { display:none!important }',

        // ─ Mixes ─
        'html.umber-unhook-mixes ytd-radio-renderer { display:none!important }',
        'html.umber-unhook-mixes ytd-compact-radio-renderer { display:none!important }',

        // ─ Explore / Trending ─
        'html.umber-unhook-explore ytd-guide-entry-renderer:has(a[href^="/feed/explore"]) { display:none!important }',
        'html.umber-unhook-explore ytd-guide-entry-renderer:has(a[href^="/feed/trending"]) { display:none!important }',

        // ─ Subscriptions ─
        'html.umber-unhook-subscriptions ytd-browse[page-subtype="subscriptions"] #contents { display:none!important }',

        // ─ Video Sidebar ────────────────────────────────────────────
        // Hides the whole right column so the video takes the full width.
        //
        // #columns is the flex row holding #primary and #secondary. Removing
        // #secondary from the flow is enough on its own: the flex layout gives
        // the space back to #primary, which is already sized as a fraction of
        // #columns, so #primary needs no width override at all.
        //
        // The old version of this also carried '#primary { max-width:none }'.
        // That is what broke scrolling. It removed the ceiling on the player
        // column, so the player sized itself to the full window width and
        // reserved matching vertical space, and in theater mode the player is
        // pinned to the top of the scroller. The description ended up below the
        // fold with no scroll range left to reach it and the wheel did nothing.
        //
        // --ytd-watch-flexy-sidebar-width is YouTube's own variable for the
        // sidebar's reserved width. Zeroing it stops the column being reserved
        // even before #secondary is laid out, which avoids a one-frame flash of
        // the player at the narrow width. It sizes rather than hides, so it
        // cannot push content below the fold.
        'html.umber-unhook-sidebar ytd-watch-flexy #secondary { display:none!important }',
        'html.umber-unhook-sidebar ytd-watch-flexy { --ytd-watch-flexy-sidebar-width:0px!important }',
        // #primary must be allowed to shrink inside the flex row, otherwise it
        // keeps its intrinsic width and overflows the row.
        'html.umber-unhook-sidebar ytd-watch-flexy #primary { min-width:0!important }',
        // The old "hide recommended videos" rule was
        //   ytd-watch-next-secondary-results-renderer { display:none }
        // which emptied the sidebar's contents but left the column, so the
        // video stayed narrow with an empty black strip beside it. That was a
        // separate toggle doing half the job of this one. Hiding #secondary
        // subsumes it, since there is nothing left to empty.

        // ─ Comments ─
        'html.umber-unhook-comments #comments { display:none!important }',
        'html.umber-unhook-comments ytd-comments { display:none!important }',
        'html.umber-unhook-comments ytd-engagement-panel-section-list-renderer[target-id="engagement-panel-comments-section"] { display:none!important }',

        // ─ Video Info ─
        'html.umber-unhook-videoInfo #above-the-fold { display:none!important }',
        'html.umber-unhook-videoInfo ytd-watch-metadata { display:none!important }',
        'html.umber-unhook-videoInfo #below { display:none!important }',

        // ─ Live Chat ─
        'html.umber-unhook-liveChat ytd-live-chat-frame { display:none!important }',
        'html.umber-unhook-liveChat #chat-container { display:none!important }',
        'html.umber-unhook-liveChat #chat { display:none!important }',

        // ─ Playlist ─
        'html.umber-unhook-playlist ytd-playlist-panel-renderer { display:none!important }',
        'html.umber-unhook-playlist #playlist { display:none!important }',

        // ─ Merch / Offers ─
        'html.umber-unhook-merch ytd-offer-module-renderer { display:none!important }',
        'html.umber-unhook-merch ytd-brand-video-shelf-renderer { display:none!important }',

        // ─ Fundraiser ─
        'html.umber-unhook-fundraiser ytd-donation-shelf-renderer { display:none!important }',
        'html.umber-unhook-fundraiser ytd-donation-unavailable-renderer { display:none!important }',

        // ─ End Screen ─
        // The current end screen is the video-wall overlay above. The old
        // .ytp-endscreen-content carousel elements were removed from the player
        // and their rules never matched anything.

        // ─ End Cards / Annotations ─
        'html.umber-unhook-endCards .ytp-ce-element { display:none!important }',
        'html.umber-unhook-endCards .ytp-ce-covering-overlay { display:none!important }',
        'html.umber-unhook-endCards .ytp-cards-teaser { display:none!important }',
        'html.umber-unhook-endCards .ytp-ce-covering-image { display:none!important }',

        'html.umber-unhook-annotations .iv-branding { display:none!important }',

        // ─ Top Header ─
        // The header is position:fixed, so the scroll offset YouTube applies
        // comes from --ytd-masthead-height. Zeroing it is what pulls the page
        // back up. The body margin reset is not needed and collided with Dark
        // Reader, which is also injected.
        'html.umber-unhook-topHeader #masthead-container { display:none!important }',
        'html.umber-unhook-topHeader #masthead { display:none!important }',
        'html.umber-unhook-topHeader ytd-masthead { display:none!important }',
        'html.umber-unhook-topHeader { --ytd-masthead-height:0px!important }',

        // ─ Notifications ─
        'html.umber-unhook-notifications ytd-notification-topbar-button-renderer { display:none!important }',
        'html.umber-unhook-notifications ytd-guide-entry-renderer:has(a[title="Notifications"]) { display:none!important }',
        'html.umber-unhook-notifications ytd-guide-entry-renderer:has(a[href^="/feed/notifications"]) { display:none!important }',

        // ─ Inapt Search Results ─
        'html.umber-unhook-inaptSearch ytd-horizontal-card-list-renderer { display:none!important }',

        // ─ Channel Watermark (new) ─
        'html.umber-unhook-endCards .ytp-ce-channel-watermark { display:none!important }',

        // Voice Search (moves with the masthead controls it sits in)
        'html.umber-unhook-topHeader #voice-search-button { display:none!important }',

        // ─ Thanks / Clip buttons (new) ─
        'html.umber-unhook-merch yt-button-shape[id="super-thanks-button"] { display:none!important }',
        'html.umber-unhook-merch yt-button-shape[id="clip-button"] { display:none!important }',

        // ─ Video Wall / Autoplay overlay at end of video ─
        'html.umber-unhook-endFeed .ytp-suggestion-set { display:none!important }',
        'html.umber-unhook-endFeed .html5-endscreen { display:none!important }',
        'html.umber-unhook-endFeed .videowall-endscreen { display:none!important }',

        // ─ Shorts in search results: REMOVED with the rest of the Shorts
        // feature, for the reasons given at the top of this list.

        // ─ "More from YouTube" guide section ─
        // Class-gated like every other rule, so turning the setting off at
        // runtime restores the section instead of leaving it hidden for the
        // life of the document.
        'html.umber-unhook-moreYT ytd-guide-section-renderer.umber-unhook-hide-section { display:none!important }'
    ].join('\n');

    // ── Inject static CSS once ───────────────────────────────────────
    function injectStyleSheet() {
        if (document.getElementById('__umber_unhook')) return;
        var target = document.head || document.documentElement;
        if (!target) return; // safety - will retry via DOMContentLoaded
        var style = document.createElement('style');
        style.id = '__umber_unhook';
        style.textContent = CSS;
        target.appendChild(style);
    }

    // ── Toggle classes on <html> based on config ─────────────────────
    // The sidebar and shorts features are both back. Shorts is rewritten to use
    // child combinators and YouTube's own [is-shorts] marker; see the notes in
    // the stylesheet for what broke the feed before.
    var FEATURES = [
        'homeFeed', 'mixes', 'explore', 'subscriptions',
        'sidebar', 'shorts', 'comments', 'videoInfo', 'liveChat',
        'playlist', 'merch', 'fundraiser', 'endFeed', 'endCards',
        'annotations', 'topHeader', 'notifications', 'inaptSearch'
    ];

    function applyClasses() {
        var html = document.documentElement;
        if (!html) return;
        for (var i = 0; i < FEATURES.length; i++) {
            var key = FEATURES[i];
            var className = 'umber-unhook-' + key;
            if (cfg[key]) {
                html.classList.add(className);
            } else {
                html.classList.remove(className);
            }
        }
    }

    // ─ Shorts Redirect: REMOVED with the rest of the Shorts feature ─

    // ── Re-injection ─────────────────────────────────────────────────
    // Everything above is assigned by the time this runs, which is the whole point
    // of the split. The path re-reads the config and re-applies without registering
    // a second set of listeners or observers, which would stack on every settings
    // change and make the page slower for no visible benefit.
    if (isReinjection) {
        refreshConfig();
        // Re-inject in case YouTube's SPA or another injected script (Dark
        // Reader rewrites <head>) removed the stylesheet. Without this,
        // applyClasses would add classes that have no rules behind them.
        injectStyleSheet();
        applyClasses();
        runJsActions();
        return;
    }
    window.__UMBER_UNHOOK_LOADED = true;

    // ── JS-only actions (can't be done with CSS) ─────────────────────
    function runJsActions() {
        // "More from YouTube" section - match by section links first, since the
        // header text is locale-dependent, with the English text as fallback.
        // Toggled as a class so the rule can be switched off at runtime. The
        // inline style this used to set was never reverted.
        var sections = document.querySelectorAll('ytd-guide-section-renderer');
        for (var i = 0; i < sections.length; i++) {
            var header = sections[i].querySelector('#guide-section-title');
            var text = header ? (header.textContent || '').trim().toLowerCase() : '';
            var isMoreFromYt = text === 'more from youtube' ||
                !!sections[i].querySelector('a[href*="/premium"], a[href*="/creators"]');

            if (cfg.moreYT && isMoreFromYt) {
                sections[i].classList.add(HIDE_SECTION_CLASS);
            } else {
                sections[i].classList.remove(HIDE_SECTION_CLASS);
            }
        }

        // Disable autoplay toggle. Skip when the YouTube Enhancer is enabled
        // with autoplay off, since it already owns this toggle.
        if (cfg.autoplay) {
            var ytCfg = window.__UMBER_YT_CONFIG;
            if (!ytCfg) {
                try {
                    var raw = localStorage.getItem('__umber_yt_enhancer');
                    if (raw) ytCfg = JSON.parse(raw);
                } catch (e) {}
            }
            var enhancerHandles = ytCfg && ytCfg.enabled && ytCfg.disableAuto;
            if (!enhancerHandles) {
                var btn = document.querySelector('.ytp-autonav-toggle-button');
                // One click per button, not one per call. YouTube applies the
                // click asynchronously, so aria-checked still reads "true" for a
                // moment after we clicked it and every later runJsActions would
                // click again and turn autoplay-next back on. The flag sits on the
                // node, so a button the player has since replaced is eligible
                // again while the one we already acted on is left alone.
                if (btn && btn.getAttribute('aria-checked') === 'true' && !btn.__umberAutonavClicked) {
                    btn.__umberAutonavClicked = true;
                    btn.click();
                }
            }
        }

        // Theater mode is deliberately not forced here.
        //
        // This used to click a size button to turn it on when the sidebar was
        // hidden. Two problems. The button it named, .ytp-size-button, is the
        // pre-2018 cycling control and is not the theater button on the current
        // player, so the click could land on fullscreen and make the whole
        // Umber window jump in and out of fullscreen. And theater mode pins
        // the player to the top of the scroll container, which interacts badly
        // with a hidden sidebar: the player keeps its full height, the
        // description falls below the fold, and there is no scroll range left
        // to reach it.
        //
        // Hiding #secondary is enough to reclaim the width. Theater mode stays
        // the user's choice, made with the player's own button or the "t" key.
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
                    // The autonav control is a plain button created by the
                    // player, not a Polymer element, so match on the class only.
                    if (node.classList &&
                        node.classList.contains('ytp-autonav-toggle-button')) {
                        needsJsHides = true;
                    }
                }
            }
            if (needsJsHides) {
                // Re-read first. These listeners were registered once and close over
                // cfg, so without this they would keep applying whatever the settings
                // were when the document loaded and would undo a setting the user
                // has since turned off.
                refreshConfig();
                runJsActions();
            }
        });
        observer.observe(app, { childList: true, subtree: true });
    }

    // ── SPA Navigation ───────────────────────────────────────────────
    // Each handler re-reads the config for the same reason as the observer.
    window.addEventListener('yt-navigate-finish', function() {
        refreshConfig();
        applyClasses();
        runJsActions();
    });
    window.addEventListener('yt-page-data-updated', function() {
        refreshConfig();
        runJsActions();
    });
    window.addEventListener('yt-navigate-start', function() {
        refreshConfig();
        applyClasses();
    });

    // ── Boot: defer ALL DOM access until the document exists ─────────
    function boot() {
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

