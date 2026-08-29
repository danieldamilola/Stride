// Stride Browser - YouTube Enhancer
// Injected on youtube.com to set quality, speed, autoplay, and loop preferences.
// Only runs on youtube.com. Inspired by MisterTube-V3 (github.com/NextEra-Development/MisterTube-V3).
// Handles ads, live streams, shorts, quality re-forcing, and speed persistence.
(function() {
    'use strict';

    var host = location.hostname;
    if (host !== 'youtube.com' && host !== 'www.youtube.com' && !host.endsWith('.youtube.com')) return;

    // Config source: live re-injection sets window.__STRIDE_YT_CONFIG and wins;
    // otherwise the config seeded in localStorage at document creation applies.
    function readConfig() {
        if (window.__STRIDE_YT_CONFIG) return window.__STRIDE_YT_CONFIG;
        try {
            var raw = localStorage.getItem('__stride_yt_enhancer');
            if (raw) return JSON.parse(raw);
        } catch (e) {}
        return { enabled: false };
    }

    // Shared config object, mutated in place on live reload so listeners that
    // closed over it always see current values.
    if (!window.__STRIDE_YT_ENHANCER_CFG) window.__STRIDE_YT_ENHANCER_CFG = {};
    var cfg = window.__STRIDE_YT_ENHANCER_CFG;
    function refreshConfig() {
        var latest = readConfig();
        for (var k in cfg) delete cfg[k];
        for (var k2 in latest) cfg[k2] = latest[k2];
        return cfg;
    }
    refreshConfig();

    // Live reload: re-read config and re-apply without re-registering anything.
    if (window.__STRIDE_YT_ENHANCER_LOADED) {
        refreshConfig();
        applyAll();
        return;
    }
    window.__STRIDE_YT_ENHANCER_LOADED = true;

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

    // Rank ladder so an unavailable target falls back to the closest lower
    // quality instead of jumping to the highest.
    var QUALITY_RANKS = { 'highres': 8, 'hd2160': 7, 'hd1440': 6, 'hd1080': 5, 'hd720': 4, 'large': 3, 'medium': 2, 'small': 1, 'tiny': 0 };

    function resolveTargetQuality(available) {
        var target = cfg.quality;
        if (target === 'auto') return 'auto';
        if (!available || available.length === 0) return target;
        if (target === 'highest') return available[0];
        if (target === 'lowest') return available[available.length - 1];
        if (available.indexOf(target) !== -1) return target;
        var targetRank = QUALITY_RANKS[target];
        if (targetRank === undefined) return available[0];
        var best = null;
        for (var i = 0; i < available.length; i++) {
            var rank = QUALITY_RANKS[available[i]];
            if (rank === undefined) continue;
            if (rank <= targetRank && (best === null || rank > QUALITY_RANKS[best])) best = available[i];
        }
        return best || available[available.length - 1];
    }

    function applyQuality() {
        if (cfg.quality === 'auto') return;
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

    // Sets the rate and marks the change as ours so the ratechange listener
    // does not mistake it for a manual user override.
    function setRate(video, rate) {
        video._strideApplying = true;
        video.playbackRate = rate;
        setTimeout(function() { video._strideApplying = false; }, 0);
    }

    function applySpeed() {
        var speed = cfg.speed;
        if (!speed || speed === 1.0) return;

        var video = getVideo();
        if (!video) return;

        // Respect a manual speed choice until the next navigation.
        if (video._strideUserOverride) return;

        setRate(video, speed);

        // Also sync with YouTube's internal speed state (updates the UI menu)
        var player = getPlayer();
        if (player && player.setPlaybackRate) {
            try { player.setPlaybackRate(speed); } catch(e) {}
        }

        // Listen for speed changes. YouTube resetting it gets re-applied;
        // a manual user change is respected until the next navigation.
        if (_speedListenerVideo !== video) {
            video.addEventListener('ratechange', function() {
                if (video._strideApplying) return;
                if (!shouldApply()) return;
                var speed = cfg.speed;
                if (speed && speed !== 1.0 && Math.abs(video.playbackRate - speed) > 0.01) {
                    video._strideUserOverride = true;
                } else if (speed && speed !== 1.0 && Math.abs(video.playbackRate - speed) <= 0.01 && video._strideUserOverride && video._strideApplying !== true) {
                    // YouTube reverted to our target on its own; clear the override.
                    video._strideUserOverride = false;
                }
            });
            _speedListenerVideo = video;
        }
    }

    // ── Loop ─────────────────────────────────────────────────────────

    function applyLoop() {
        if (!cfg.loop && !cfg.enabled) return;
        var video = getVideo();
        if (video) video.loop = !!cfg.loop;
    }

    // ── Autoplay ─────────────────────────────────────────────────────

    function applyAutoplay() {
        if (!cfg.disableAuto) return;
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
        if (!cfg.enabled) {
            // Live-disabled: undo what we set so nothing lingers.
            var v = getVideo();
            if (v) v.loop = false;
            return;
        }
        if (!shouldApply()) return;
        applySpeed();
        applyLoop();
        applyQuality();
        applyAutoplay();
    }

    // Wait for the player to be truly ready (not just DOM-present).
    // Time-based cap, not frame-based, so behavior is the same at any refresh rate.
    function waitForPlayer(callback) {
        var start = Date.now();
        function check() {
            var player = getPlayer();
            if (player && player.getPlayerState && player.getPlayerState() !== -1) {
                callback();
                return;
            }
            if (Date.now() - start < 5000) {
                setTimeout(check, 100);
            }
        }
        check();
    }

    // ── Ad-aware apply ───────────────────────────────────────────────
    // Waits for the ad break to end before applying settings. The observer
    // stays armed until an apply actually succeeds, so multi-ad pods do not
    // leave quality and speed unapplied.

    var _adObserver = null;

    function applyWhenReady() {
        if (isShorts()) return;

        if (isAdPlaying()) {
            var player = getPlayer();
            if (!player) return;
            if (_adObserver) _adObserver.disconnect();
            _adObserver = new MutationObserver(function() {
                if (isAdPlaying()) return;
                waitForPlayer(function() {
                    // Another ad in the pod may have started; keep waiting.
                    if (isAdPlaying()) return;
                    if (_adObserver) { _adObserver.disconnect(); _adObserver = null; }
                    applyAll();
                });
            });
            _adObserver.observe(player, { attributes: true, attributeFilter: ['class'] });
            return;
        }

        waitForPlayer(applyAll);
    }

    // ── Pause on tab switch ──────────────────────────────────────────

    // Always registered; config-gated so live reload can flip the behavior.
    document.addEventListener('visibilitychange', function() {
        if (!cfg.enabled || !cfg.pauseOnSwitch) return;
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

    // ── SPA Navigation ───────────────────────────────────────────────

    window.addEventListener('yt-navigate-finish', function() {
        // The video element is usually reused across navigations, so clear the
        // manual speed override and re-apply the configured speed.
        var v = getVideo();
        if (v) v._strideUserOverride = false;
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

