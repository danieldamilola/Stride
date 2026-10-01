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
    // Routed through applyWhenReady rather than applyAll so a reload mid-ad or
    // before the player is ready still lands, matching the first-run path.
    if (window.__STRIDE_YT_ENHANCER_LOADED) {
        refreshConfig();
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', applyWhenReady);
        } else {
            applyWhenReady();
        }
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

    // Rank ladder, highest first. Used both to pick a target and to sort the
    // formats found in the quality menu, so the menu's own order never matters.
    var QUALITY_RANKS = {
        'highres': 8, 'hd2160': 7, 'hd1440': 6, 'hd1080': 5, 'hd720': 4,
        'large': 3, 'medium': 2, 'small': 1, 'tiny': 0
    };

    // Quality menu label to the player's internal quality name. YouTube renders
    // labels such as "1080p" and "4K", not the player's own names.
    var QUALITY_LABELS = {
        'highres': 'highres', '4k': 'highres', '2160p': 'hd2160',
        '1440p': 'hd1440', '1080p': 'hd1080', '720p': 'hd720',
        '480p': 'large', '360p': 'medium', '240p': 'small', '144p': 'tiny'
    };

    function qualityFromLabel(text) {
        var label = (text || '').toLowerCase().trim();
        if (!label || label === 'auto') return null;
        if (QUALITY_LABELS[label]) return QUALITY_LABELS[label];
        var m = label.match(/(\d{3,4})p/);
        return (m && QUALITY_LABELS[m[1] + 'p']) || null;
    }

    // YouTube retired getAvailableQualityLevels, so the available formats are
    // read from the quality menu it renders rather than from the player API.
    // Returns an empty list when the menu is not currently open.
    function readMenuQualities() {
        var items = document.querySelectorAll(
            '.ytp-quality-menu [role="menuitemradio"], .ytp-quality-menu li');
        if (!items || items.length === 0) return [];
        var out = [];
        for (var i = 0; i < items.length; i++) {
            var q = qualityFromLabel(items[i].textContent);
            if (q && out.indexOf(q) === -1) out.push(q);
        }
        return out;
    }

    // Returns the quality to clamp to, or null to leave YouTube's own adaptive
    // choice alone. Null when the requested rung cannot be identified.
    function resolveTargetQuality(available) {
        var target = cfg.quality;
        if (!target || target === 'auto') return null;

        if (!available || available.length === 0) {
            // Without a list, a real rung can be sent through as-is. The
            // meta-values need a list to pick from, so they are skipped.
            return QUALITY_RANKS[target] !== undefined ? target : null;
        }

        var ranked = available.slice().sort(function(a, b) {
            return (QUALITY_RANKS[b] || -1) - (QUALITY_RANKS[a] || -1);
        });

        if (target === 'highest') return ranked[0];
        if (target === 'lowest') return ranked[ranked.length - 1];

        var targetRank = QUALITY_RANKS[target];
        if (targetRank === undefined) return null;
        if (available.indexOf(target) !== -1) return target;

        // Closest rung at or below the request, so an unavailable target never
        // climbs above what was asked for.
        for (var i = 0; i < ranked.length; i++) {
            if ((QUALITY_RANKS[ranked[i]] || -1) <= targetRank) return ranked[i];
        }
        return ranked[ranked.length - 1];
    }

    // The menu watcher lives on document for the life of the page, so a plain
    // boolean guards registration. Tracking the player instead would stack a
    // new handler every time YouTube replaces #movie_player.
    var _qualityMenuWatched = false;

    // Pending handle for the delayed clamp release, so repeated applies within
    // one video do not queue multiple releases.
    var _adaptiveResumeTimer = null;

    // Set once you pick a quality from YouTube's own settings menu. Sticky for
    // the rest of the page session, so the configured quality is applied at the
    // start of each video and never fought for after that.
    var _userChoseQuality = false;

    // Releases the clamp so YouTube's adaptive bitrate engine can move freely
    // again. Called when you pick a quality manually, and shortly after each
    // video starts so a stalled stream can drop resolution on its own.
    function releaseQualityClamp(player) {
        if (!player || !player.setPlaybackQualityRange) return;
        try { player.setPlaybackQualityRange('', ''); } catch(e) {}
    }

    // Watches for a click on YouTube's own quality menu. That is a genuine user
    // action, unlike onPlaybackQualityChange, which fires for both a manual pick
    // and YouTube's own adaptive downgrade and so cannot tell them apart.
    function watchQualityMenu() {
        if (_qualityMenuWatched) return;
        _qualityMenuWatched = true;

        document.addEventListener('click', function(e) {
            var item = e.target && e.target.closest
                ? e.target.closest('.ytp-quality-menu [role="menuitemradio"], .ytp-quality-menu li')
                : null;
            if (!item) return;

            // A pick of "Auto" hands control back to YouTube, same as a pick of
            // a specific resolution.
            _userChoseQuality = true;
            releaseQualityClamp(getPlayer());
        }, true);
    }

    function applyQuality() {
        if (cfg.quality === 'auto') return;
        if (isLiveStream()) return; // Live streams use adaptive - don't force

        var player = getPlayer();
        if (!player || !player.setPlaybackQualityRange) return;

        watchQualityMenu();

        // Your choice outranks the configured default until the next navigation.
        if (_userChoseQuality) return;

        try {
            var target = resolveTargetQuality(readMenuQualities());
            if (!target) return;
            player.setPlaybackQualityRange(target, target);
        } catch(e) {}
    }

    // ── Speed Control ────────────────────────────────────────────────

    // Same identity-tracking approach as the quality menu: avoids stacking
    // duplicate 'ratechange' listeners on the same <video> element across SPA
    // navigations.
    var _speedListenerVideo = null;

    // Records the rate Stride last wrote. ratechange is dispatched from a queued
    // media task rather than synchronously, so a boolean flag flipped on a timer
    // races the event it is meant to identify. Comparing against the last value
    // written is deterministic.
    function setRate(video, rate) {
        video._strideAppliedRate = rate;
        video.playbackRate = rate;
    }

    // True when the current rate is the one Stride wrote, meaning the change
    // came from us rather than from YouTube or the user.
    function isOurRateChange(video) {
        var applied = video._strideAppliedRate;
        return typeof applied === 'number' && Math.abs(video.playbackRate - applied) < 0.01;
    }

    function applySpeed() {
        var speed = cfg.speed;

        var video = getVideo();
        if (!video) return;

        attachSpeedListener(video);

        // Respect a manual speed choice until the next navigation.
        if (video._strideUserOverride) return;

        if (!speed || speed === 1.0) {
            // Back to normal speed, so a previous setting does not linger.
            if (Math.abs(video.playbackRate - 1.0) > 0.01) setRate(video, 1.0);
            return;
        }

        if (Math.abs(video.playbackRate - speed) > 0.01) setRate(video, speed);

        // Also sync with YouTube's internal speed state (updates the UI menu)
        var player = getPlayer();
        if (player && player.setPlaybackRate) {
            try { player.setPlaybackRate(speed); } catch(e) {}
        }
    }

    // A change we did not make means either YouTube reset the rate or the user
    // picked a new one from YouTube's own menu. Only the menu counts as the user
    // speaking; a bare reset is put back to the configured speed.
    function attachSpeedListener(video) {
        if (_speedListenerVideo === video) return;

        video.addEventListener('ratechange', function() {
            if (isOurRateChange(video)) return;
            if (!shouldApply()) return;

            var speed = cfg.speed;
            if (!speed || speed === 1.0) return;

            var chosenByUser = video._strideRateMenuOpen;
            if (chosenByUser) {
                video._strideUserOverride = true;
                return;
            }

            if (Math.abs(video.playbackRate - speed) > 0.01) setRate(video, speed);
        });

        _speedListenerVideo = video;
    }

    // Marks a rate change made through YouTube's playback-rate menu as the
    // user's own, so it is not undone. Same reasoning as the quality menu.
    var _speedMenuWatched = false;

    function watchSpeedMenu() {
        if (_speedMenuWatched) return;
        _speedMenuWatched = true;

        document.addEventListener('click', function(e) {
            var item = e.target && e.target.closest
                ? e.target.closest('.ytp-playback-rate-menu [role="menuitemradio"], .ytp-playback-rate-menu li')
                : null;
            if (!item) return;
            var video = getVideo();
            if (video) video._strideRateMenuOpen = true;
        }, true);

        // The flag marks the click that produced the following ratechange, so it
        // is cleared once that event has been handled.
        document.addEventListener('ratechange', function() {
            var video = getVideo();
            if (video) video._strideRateMenuOpen = false;
        }, true);
    }

    // ── Loop ─────────────────────────────────────────────────────────

    function applyLoop() {
        var video = getVideo();
        if (!video) return;
        // Written every time so turning the setting off at runtime takes effect.
        video.loop = !!(cfg.enabled && cfg.loop);
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

    // Undoes everything Stride wrote. Used when the enhancer is switched off in
    // settings on a tab that is already open, so nothing keeps applying.
    function undoAll() {
        var video = getVideo();
        if (video) {
            if (Math.abs(video.playbackRate - 1.0) > 0.01) {
                video.playbackRate = 1.0;
                var player = getPlayer();
                if (player && player.setPlaybackRate) {
                    try { player.setPlaybackRate(1.0); } catch(e) {}
                }
            }
            video.loop = false;
        }
        releaseQualityClamp(getPlayer());
    }

    function applyAll() {
        if (!cfg.enabled) {
            undoAll();
            return;
        }
        if (!shouldApply()) return;
        watchSpeedMenu();
        applySpeed();
        applyLoop();
        applyQuality();
        applyAutoplay();
        scheduleAdaptiveResume();
    }

    // Lets go of the quality clamp a few seconds after each video starts. The
    // configured quality still gets picked at the start, but YouTube is free to
    // drop resolution when the network slows instead of stalling at the clamp.
    function scheduleAdaptiveResume() {
        if (_adaptiveResumeTimer) clearTimeout(_adaptiveResumeTimer);
        _adaptiveResumeTimer = setTimeout(function() {
            _adaptiveResumeTimer = null;
            if (!cfg.enabled || cfg.quality === 'auto') return;
            if (_userChoseQuality) return;

            // An ad may have started inside the window, in which case applyAll
            // never ran for this video and the clamp is re-applied afterwards.
            if (isAdPlaying()) {
                applyWhenReady();
                return;
            }
            releaseQualityClamp(getPlayer());
        }, 4000);
    }

    // Wait for the player to be truly ready (not just DOM-present).
    // Time-based cap, not frame-based, so behavior is the same at any refresh rate.
    // One poll at a time: repeated applyWhenReady calls would otherwise leave
    // overlapping 100ms loops behind.
    var _playerPollHandle = null;

    function waitForPlayer(callback) {
        if (_playerPollHandle) return;
        var start = Date.now();
        function check() {
            var player = getPlayer();
            if (player && player.getPlayerState && player.getPlayerState() !== -1) {
                _playerPollHandle = null;
                callback();
                return;
            }
            if (Date.now() - start < 5000) {
                _playerPollHandle = setTimeout(check, 100);
            } else {
                _playerPollHandle = null;
            }
        }
        check();
    }

    // ── Ad-aware apply ───────────────────────────────────────────────
    // Waits for the ad break to end before applying settings. The observer
    // stays armed until an apply actually succeeds, so multi-ad pods do not
    // leave quality and speed unapplied.

    var _adObserver = null;
    var _adObservedPlayer = null;

    function applyWhenReady() {
        if (isShorts()) return;

        if (isAdPlaying()) {
            var player = getPlayer();
            if (!player) return;

            // YouTube can swap the player mid-roll. Watch the one we armed on,
            // and re-arm if the element we were observing is no longer the one
            // reporting the ad, otherwise the observer is orphaned and the
            // settings never land for this video.
            if (_adObserver && _adObservedPlayer !== player) {
                _adObserver.disconnect();
                _adObserver = null;
            }

            if (!_adObserver) {
                _adObservedPlayer = player;
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
            }
            return;
        }

        waitForPlayer(applyAll);
    }

    // ── Pause on tab switch ──────────────────────────────────────────

    // Always registered; config-gated so live reload can flip the behavior.
    // The element that was actually paused is remembered, not whatever
    // document.querySelector('video') returns on the way back: during a roll
    // that can be a different element, which would leave the real video paused
    // or force-play something the user never started.
    var _pausedByStride = null;

    document.addEventListener('visibilitychange', function() {
        if (!cfg.enabled || !cfg.pauseOnSwitch) return;

        if (document.hidden) {
            var video = getVideo();
            if (!video || video.paused) return;
            video.pause();
            _pausedByStride = video;
            return;
        }

        var paused = _pausedByStride;
        _pausedByStride = null;
        if (!paused) return;

        // A video that finished or moved on while hidden should not restart.
        if (paused.ended || paused.seeking) return;
        var resume = paused.play();
        if (resume && resume.catch) resume.catch(function() {});
    });

    // ── SPA Navigation ───────────────────────────────────────────────

    window.addEventListener('yt-navigate-finish', function() {
        // The video element is usually reused across navigations, so clear the
        // manual speed override and re-apply the configured speed.
        var v = getVideo();
        if (v) {
            v._strideUserOverride = false;
            v._strideRateMenuOpen = false;
        }

        // A manual quality pick applies to the video you were watching. The next
        // video starts at your configured quality again.
        _userChoseQuality = false;
        _pausedByStride = null;

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

