// Stride Browser - YouTube Enhancer
// Injected on youtube.com to set quality, speed, autoplay, and loop preferences.
// Only runs on youtube.com. Inspired by MisterTube-V3 (github.com/NextEra-Development/MisterTube-V3).
// Handles ads, live streams, shorts, quality re-forcing, and speed persistence.
(function() {
    'use strict';

    if (!location.hostname.includes('youtube.com')) return;

    // Prevent duplicate injection
    if (window.__STRIDE_YT_ENHANCER_LOADED) return;
    window.__STRIDE_YT_ENHANCER_LOADED = true;

    var QUALITY = '{{QUALITY}}';
    var SPEED = parseFloat('{{SPEED}}');
    var DISABLE_AUTO = {{DISABLE_AUTO}};
    var PAUSE_ON_SWITCH = {{PAUSE_ON_SWITCH}};
    var LOOP_VIDEO = {{LOOP_VIDEO}};

    // ── Helpers ──────────────────────────────────────────────────────

    function getPlayer() {
        return document.getElementById('movie_player') || null;
    }

    function getVideo() {
        return document.querySelector('video') || null;
    }

    function isAdPlaying() {
        var player = getPlayer();
        return player ? player.classList.contains('ad-showing') : false;
    }

    function isLiveStream() {
        var player = getPlayer();
        if (!player || !player.getVideoData) return false;
        try {
            var data = player.getVideoData();
            return !!(data && data.isLive);
        } catch(e) { return false; }
    }

    function isShorts() {
        return location.pathname.startsWith('/shorts/');
    }

    function shouldApply() {
        return !isAdPlaying() && !isShorts();
    }

    // ── Quality Forcing ──────────────────────────────────────────────

    var _qualityListenerAdded = false;

    function applyQuality() {
        if (QUALITY === 'auto') return;
        if (isLiveStream()) return; // Live streams use adaptive — don't force

        var player = getPlayer();
        if (!player || !player.setPlaybackQualityRange) return;

        try {
            var available = player.getAvailableQualityLevels
                ? player.getAvailableQualityLevels()
                : [];

            var target = QUALITY;

            // Support "highest" and "lowest" meta-values
            if (target === 'highest' && available.length > 0) {
                target = available[0];
            } else if (target === 'lowest' && available.length > 0) {
                target = available[available.length - 1];
            }

            // Validate: if target quality isn't available, pick the best available
            if (available.length > 0 && available.indexOf(target) === -1) {
                target = available[0]; // fallback to highest available
            }

            player.setPlaybackQualityRange(target, target);

            // Listen for quality changes and re-force (adaptive streaming override)
            if (!_qualityListenerAdded && player.addEventListener) {
                player.addEventListener('onPlaybackQualityChange', function(newQuality) {
                    if (!shouldApply()) return;
                    var desired = QUALITY === 'highest' ? (available[0] || QUALITY) : QUALITY;
                    if (newQuality !== desired) {
                        try { player.setPlaybackQualityRange(desired, desired); } catch(e) {}
                    }
                });
                _qualityListenerAdded = true;
            }
        } catch(e) {}
    }

    // ── Speed Control ────────────────────────────────────────────────

    var _speedListenerAdded = false;

    function applySpeed() {
        if (!SPEED || SPEED === 1.0) return;

        var video = getVideo();
        if (!video) return;

        video.playbackRate = SPEED;

        // Also sync with YouTube's internal speed state (updates the UI menu)
        var player = getPlayer();
        if (player && player.setPlaybackRate) {
            try { player.setPlaybackRate(SPEED); } catch(e) {}
        }

        // Listen for YouTube resetting the speed and re-apply
        if (!_speedListenerAdded) {
            video.addEventListener('ratechange', function() {
                if (!shouldApply()) return;
                if (Math.abs(video.playbackRate - SPEED) > 0.01) {
                    video.playbackRate = SPEED;
                }
            });
            _speedListenerAdded = true;
        }
    }

    // ── Loop ─────────────────────────────────────────────────────────

    function applyLoop() {
        if (!LOOP_VIDEO) return;
        var video = getVideo();
        if (video) video.loop = true;
    }

    // ── Autoplay ─────────────────────────────────────────────────────

    function applyAutoplay() {
        if (!DISABLE_AUTO) return;
        try {
            var btn = document.querySelector('.ytp-autonav-toggle-button');
            if (btn && btn.getAttribute('aria-checked') === 'true') {
                btn.click();
            }
        } catch(e) {}
    }

    // ── Main Apply ───────────────────────────────────────────────────

    function applyAll() {
        if (!shouldApply()) return;
        applySpeed();
        applyLoop();
        applyQuality();
        applyAutoplay();
    }

    // Wait for the player to be truly ready (not just DOM-present)
    function waitForPlayer(callback) {
        var attempts = 0;
        function check() {
            var player = getPlayer();
            if (player && player.getPlayerState && player.getPlayerState() !== -1) {
                callback();
                return;
            }
            attempts++;
            if (attempts < 60) { // ~1 second max wait
                requestAnimationFrame(check);
            }
        }
        check();
    }

    // ── Ad-aware apply ───────────────────────────────────────────────
    // Waits for ad to end before applying settings.

    function applyWhenReady() {
        if (isShorts()) return;

        if (isAdPlaying()) {
            // Wait for ad to finish
            var player = getPlayer();
            if (player) {
                var adObserver = new MutationObserver(function() {
                    if (!isAdPlaying()) {
                        adObserver.disconnect();
                        waitForPlayer(applyAll);
                    }
                });
                adObserver.observe(player, { attributes: true, attributeFilter: ['class'] });
            }
            return;
        }

        waitForPlayer(applyAll);
    }

    // ── Pause on tab switch ──────────────────────────────────────────

    if (PAUSE_ON_SWITCH) {
        document.addEventListener('visibilitychange', function() {
            var video = getVideo();
            if (!video) return;
            if (document.hidden && !video.paused) {
                video.pause();
                video._stridePaused = true;
            } else if (!document.hidden && video._stridePaused) {
                video.play();
                video._stridePaused = false;
            }
        });
    }

    // ── SPA Navigation ───────────────────────────────────────────────

    window.addEventListener('yt-navigate-finish', function() {
        // Reset listener flags — new video, new player instance
        _qualityListenerAdded = false;
        _speedListenerAdded = false;
        applyWhenReady();
    });

    // ── Video src change observer (failsafe) ─────────────────────────
    // Catches video changes that navigation events miss (autoplay next, playlist)

    var _srcObserver = null;

    function observeVideoSrc() {
        var video = getVideo();
        if (!video) return;
        if (_srcObserver) _srcObserver.disconnect();

        _srcObserver = new MutationObserver(function(mutations) {
            for (var m = 0; m < mutations.length; m++) {
                if (mutations[m].attributeName === 'src') {
                    _qualityListenerAdded = false;
                    _speedListenerAdded = false;
                    applyWhenReady();
                    return;
                }
            }
        });
        _srcObserver.observe(video, { attributes: true, attributeFilter: ['src'] });
    }

    // ── App observer (wait for ytd-app, then video) ──────────────────

    function startObserving() {
        var app = document.querySelector('ytd-app');
        if (!app) {
            var waitObserver = new MutationObserver(function() {
                var ytApp = document.querySelector('ytd-app');
                if (ytApp) {
                    waitObserver.disconnect();
                    observeForVideo(ytApp);
                }
            });
            waitObserver.observe(document.body || document.documentElement, { childList: true, subtree: true });
            return;
        }
        observeForVideo(app);
    }

    function observeForVideo(app) {
        var observer = new MutationObserver(function(mutations) {
            for (var m = 0; m < mutations.length; m++) {
                var nodes = mutations[m].addedNodes;
                for (var n = 0; n < nodes.length; n++) {
                    var node = nodes[n];
                    if (node.nodeType !== 1) continue;
                    if (node.tagName === 'VIDEO') {
                        observeVideoSrc();
                        applyWhenReady();
                        return;
                    }
                }
            }
        });
        observer.observe(app, { childList: true, subtree: true });
    }

    // ── Initial run ──────────────────────────────────────────────────

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() {
            applyWhenReady();
            startObserving();
            observeVideoSrc();
        });
    } else {
        applyWhenReady();
        startObserving();
        observeVideoSrc();
    }
})();
