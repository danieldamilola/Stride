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

    // Tracks which player object the quality-change listener is already attached
    // to. YouTube's SPA usually reuses the same #movie_player across videos, so
    // this identity check (rather than a plain boolean) prevents re-adding a
    // duplicate listener on every navigation while still attaching a fresh one
    // if the player object is ever actually replaced.
    var _qualityListenerPlayer = null;

    // Resolves "highest"/"lowest" meta-values against the live quality list.
    // Called both when forcing quality up-front and when re-forcing after
    // YouTube changes it, so it never relies on a stale snapshot.
    function resolveTargetQuality(available) {
        var target = QUALITY;
        if (target === 'highest' && available.length > 0) {
            target = available[0];
        } else if (target === 'lowest' && available.length > 0) {
            target = available[available.length - 1];
        }
        if (available.length > 0 && available.indexOf(target) === -1) {
            target = available[0]; // fallback to highest available
        }
        return target;
    }

    function applyQuality() {
        if (QUALITY === 'auto') return;
        if (isLiveStream()) return; // Live streams use adaptive - don't force

        var player = getPlayer();
        if (!player || !player.setPlaybackQualityRange) return;

        try {
            var available = player.getAvailableQualityLevels
                ? player.getAvailableQualityLevels()
                : [];

            var target = resolveTargetQuality(available);
            player.setPlaybackQualityRange(target, target);

            // Listen for quality changes and re-force (adaptive streaming override)
            if (player.addEventListener && _qualityListenerPlayer !== player) {
                player.addEventListener('onPlaybackQualityChange', function(newQuality) {
                    if (!shouldApply()) return;
                    var currentAvailable = player.getAvailableQualityLevels
                        ? player.getAvailableQualityLevels()
                        : [];
                    var desired = resolveTargetQuality(currentAvailable);
                    if (newQuality !== desired) {
                        try { player.setPlaybackQualityRange(desired, desired); } catch(e) {}
                    }
                });
                _qualityListenerPlayer = player;
            }
        } catch(e) {}
    }

    // ── Speed Control ────────────────────────────────────────────────

    // Same identity-tracking approach as quality: avoids stacking duplicate
    // 'ratechange' listeners on the same <video> element across SPA navigations.
    var _speedListenerVideo = null;

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
        if (_speedListenerVideo !== video) {
            video.addEventListener('ratechange', function() {
                if (!shouldApply()) return;
                if (Math.abs(video.playbackRate - SPEED) > 0.01) {
                    video.playbackRate = SPEED;
                }
            });
            _speedListenerVideo = video;
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
            // Robust engine-level disable
            window.localStorage.setItem('yt-player-autonav-state', JSON.stringify({ data: "1", creation: Date.now() }));
            
            // Also click the UI button if it's visually enabled
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

