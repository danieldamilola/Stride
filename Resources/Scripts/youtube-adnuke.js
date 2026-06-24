// Stride Browser — YouTube Ad Nuker
// Runs at document_start on youtube.com. Kills ads instantly — no skip, no wait.
// Multi-layer approach: DOM removal, video skip, player API override.
(function() {
    'use strict';

    if (!location.hostname.includes('youtube.com')) return;
    if (window.__STRIDE_AD_NUKER_LOADED) return;
    window.__STRIDE_AD_NUKER_LOADED = true;

    // ── CSS: Hide all ad-related elements instantly ──────────────────
    var AD_CSS = [
        // Pre-roll / mid-roll ad overlays
        '.ytp-ad-module { display:none!important }',
        '.ytp-ad-overlay-container { display:none!important }',
        '.ytp-ad-overlay-slot { display:none!important }',
        '.ytp-ad-text-overlay { display:none!important }',
        '.ytp-ad-image-overlay { display:none!important }',
        '.ytp-ad-skip-button-container { display:none!important }',
        '.ytp-ad-skip-button-modern { display:none!important }',
        '.ytp-ad-player-overlay { display:none!important }',
        '.ytp-ad-player-overlay-layout { display:none!important }',
        '.ytp-ad-player-overlay-flyout-cta { display:none!important }',
        '.ytp-ad-action-interstitial { display:none!important }',
        '.ytp-ad-action-interstitial-background-container { display:none!important }',
        '.ytp-ad-action-interstitial-slot { display:none!important }',
        '.ytp-ad-message-container { display:none!important }',
        '.ytp-ad-persistent-progress-bar-container { display:none!important }',
        '.ytp-ad-survey-interstitial { display:none!important }',

        // Banner / display ads in page
        '#player-ads { display:none!important }',
        '#masthead-ad { display:none!important }',
        '#ad-container { display:none!important }',
        'ytd-ad-slot-renderer { display:none!important }',
        'ytd-rich-item-renderer:has(.ytd-ad-slot-renderer) { display:none!important }',
        'ytd-display-ad-renderer { display:none!important }',
        'ytd-promoted-sparkles-web-renderer { display:none!important }',
        'ytd-promoted-sparkles-text-search-renderer { display:none!important }',
        'ytd-promoted-video-renderer { display:none!important }',
        'ytd-in-feed-ad-layout-renderer { display:none!important }',
        'ytd-banner-promo-renderer { display:none!important }',
        'ytd-statement-banner-renderer { display:none!important }',
        'ytd-brand-video-shelf-renderer { display:none!important }',
        'ytd-brand-video-singleton-renderer { display:none!important }',
        'ytd-engagement-panel-section-list-renderer[target-id="engagement-panel-ads"] { display:none!important }',

        // Search result ads
        'ytd-search-pyv-renderer { display:none!important }',
        'ytd-movie-offer-module-renderer { display:none!important }',

        // Sidebar / companion ads
        '#companion { display:none!important }',
        '#google_companion_ad_div { display:none!important }',
        'ytd-companion-slot-renderer { display:none!important }',

        // Premium upsell / membership nags
        'ytd-mealbar-promo-renderer { display:none!important }',
        'tp-yt-paper-dialog:has(yt-upsell) { display:none!important }',
        'yt-upsell-dialog-renderer { display:none!important }',
        '#masthead-ad { display:none!important }',

        // Popup ad dialogs (YouTube anti-adblock walls)
        'tp-yt-iron-overlay-backdrop { display:none!important }',
        'ytd-popup-container:has(ytd-enforcement-message-view-model) { display:none!important }',
        'ytd-enforcement-message-view-model { display:none!important }'
    ].join('\n');

    // Inject CSS — deferred to when DOM is ready
    function injectAdCss() {
        if (document.getElementById('__stride_ad_nuke')) return;
        var target = document.head || document.documentElement;
        if (!target) return;
        var style = document.createElement('style');
        style.id = '__stride_ad_nuke';
        style.textContent = AD_CSS;
        target.appendChild(style);
    }

    // ── Core: Skip video ads instantly ───────────────────────────────

    function nukeVideoAd() {
        var player = document.querySelector('.html5-video-player');
        if (!player) return;

        // Not an ad? Do nothing.
        if (!player.classList.contains('ad-showing') &&
            !player.classList.contains('ad-interrupting')) return;

        var video = player.querySelector('video');
        if (!video) return;

        // Instantly skip to end — this triggers the ad completion and loads the real video
        if (video.duration && isFinite(video.duration)) {
            video.currentTime = video.duration;
        }

        // Mute during the skip frame so user hears nothing
        video.muted = true;

        // Also click any skip button that exists
        var skipButtons = player.querySelectorAll(
            '.ytp-ad-skip-button, .ytp-ad-skip-button-modern, ' +
            '.ytp-ad-skip-button-slot button, .ytp-skip-ad-button, ' +
            'button.ytp-ad-overlay-close-button, ' +
            '.ytp-ad-skip-button-container button'
        );
        for (var i = 0; i < skipButtons.length; i++) {
            try { skipButtons[i].click(); } catch(e) {}
        }

        // Try the player API skipAd
        var moviePlayer = document.getElementById('movie_player');
        if (moviePlayer) {
            if (typeof moviePlayer.skipAd === 'function') {
                try { moviePlayer.skipAd(); } catch(e) {}
            }
            if (typeof moviePlayer.cancelPlayback === 'function') {
                try { moviePlayer.cancelPlayback(); } catch(e) {}
            }
        }
    }

    // ── Watch for ad-showing class on player ─────────────────────────
    // MutationObserver on the player element's class attribute.

    function watchPlayer() {
        var player = document.querySelector('.html5-video-player');
        if (!player) return;
        if (player._strideAdWatcher) return;

        // Nuke any ad that's already playing when we attach
        nukeVideoAd();

        var observer = new MutationObserver(function(mutations) {
            for (var m = 0; m < mutations.length; m++) {
                if (mutations[m].attributeName === 'class') {
                    if (player.classList.contains('ad-showing') ||
                        player.classList.contains('ad-interrupting')) {
                        nukeVideoAd();
                        // Check rapidly in case the first skip doesn't take
                        setTimeout(nukeVideoAd, 50);
                        setTimeout(nukeVideoAd, 200);
                        setTimeout(nukeVideoAd, 500);
                    } else {
                        // Ad ended — unmute
                        var v = player.querySelector('video');
                        if (v && v.muted && v._strideMuted !== true) {
                            v.muted = false;
                        }
                    }
                }
            }
        });
        observer.observe(player, { attributes: true, attributeFilter: ['class'] });
        player._strideAdWatcher = true;
    }

    // ── Watch for ad elements in DOM ─────────────────────────────────
    // Remove ad containers as soon as they appear.

    function removeAdElements() {
        var selectors = [
            'ytd-ad-slot-renderer',
            'ytd-promoted-sparkles-web-renderer',
            'ytd-promoted-video-renderer',
            'ytd-in-feed-ad-layout-renderer',
            'ytd-display-ad-renderer',
            '#player-ads',
            '#masthead-ad',
            'ytd-banner-promo-renderer',
            'ytd-mealbar-promo-renderer'
        ];
        for (var s = 0; s < selectors.length; s++) {
            var els = document.querySelectorAll(selectors[s]);
            for (var e = 0; e < els.length; e++) {
                els[e].remove();
            }
        }
    }

    // ── Video event interception ─────────────────────────────────────
    // If an ad starts playing, skip it immediately via the video's timeupdate.

    function interceptVideo() {
        var video = document.querySelector('video');
        if (!video || video._strideAdInterceptor) return;

        video.addEventListener('timeupdate', function() {
            var player = document.querySelector('.html5-video-player');
            if (player && (player.classList.contains('ad-showing') ||
                           player.classList.contains('ad-interrupting'))) {
                if (video.duration && isFinite(video.duration) && video.currentTime < video.duration - 0.1) {
                    video.currentTime = video.duration;
                }
            }
        });
        video._strideAdInterceptor = true;
    }

    // ── Main observer — wait for ytd-app then watch for player/ads ──

    function startAdNuker() {
        injectAdCss();
        watchPlayer();
        interceptVideo();
        removeAdElements();

        var app = document.querySelector('ytd-app') || document.body;
        if (!app) return;

        var observer = new MutationObserver(function(mutations) {
            var needsCheck = false;
            for (var m = 0; m < mutations.length; m++) {
                var nodes = mutations[m].addedNodes;
                for (var n = 0; n < nodes.length; n++) {
                    var node = nodes[n];
                    if (node.nodeType !== 1) continue;
                    var tag = node.tagName;
                    // Player appeared — attach watcher
                    if (tag === 'DIV' && (node.classList.contains('html5-video-player') || node.id === 'movie_player')) {
                        watchPlayer();
                        interceptVideo();
                    }
                    // Ad elements appeared — remove them
                    if (tag === 'YTD-AD-SLOT-RENDERER' ||
                        tag === 'YTD-PROMOTED-SPARKLES-WEB-RENDERER' ||
                        tag === 'YTD-PROMOTED-VIDEO-RENDERER' ||
                        tag === 'YTD-IN-FEED-AD-LAYOUT-RENDERER' ||
                        tag === 'YTD-DISPLAY-AD-RENDERER' ||
                        tag === 'YTD-BANNER-PROMO-RENDERER' ||
                        tag === 'YTD-MEALBAR-PROMO-RENDERER') {
                        node.remove();
                        continue;
                    }
                    // Video element appeared
                    if (tag === 'VIDEO') {
                        interceptVideo();
                        needsCheck = true;
                    }
                }
            }
            if (needsCheck) watchPlayer();
        });
        observer.observe(app, { childList: true, subtree: true });
    }

    // ── SPA Navigation ───────────────────────────────────────────────

    window.addEventListener('yt-navigate-finish', function() {
        watchPlayer();
        interceptVideo();
        removeAdElements();
    });

    // ── Boot ─────────────────────────────────────────────────────────

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', startAdNuker);
    } else {
        startAdNuker();
    }
})();
