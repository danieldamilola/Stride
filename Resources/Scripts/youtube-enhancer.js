// Spur Browser - YouTube Enhancer
// Injected on youtube.com to set quality, speed, autoplay, and loop preferences.
(function() {
    'use strict';

    const QUALITY = '{{QUALITY}}';
    const SPEED = parseFloat('{{SPEED}}');
    const DISABLE_AUTO = {{DISABLE_AUTO}};
    const PAUSE_ON_SWITCH = {{PAUSE_ON_SWITCH}};
    const LOOP_VIDEO = {{LOOP_VIDEO}};

    function applySettings() {
        const video = document.querySelector('video');
        if (!video) return;

        // Set playback speed
        if (SPEED && SPEED !== 1.0) {
            video.playbackRate = SPEED;
        }

        // Loop video
        if (LOOP_VIDEO) {
            video.loop = true;
        }

        // Set quality via YouTube player API
        if (QUALITY !== 'auto') {
            try {
                const player = document.getElementById('movie_player');
                if (player && player.setPlaybackQualityRange) {
                    player.setPlaybackQualityRange(QUALITY, QUALITY);
                }
            } catch(e) {}
        }

        // Disable autoplay
        if (DISABLE_AUTO) {
            try {
                const player = document.getElementById('movie_player');
                if (player) {
                    const autoplayBtn = document.querySelector('.ytp-autonav-toggle-button');
                    if (autoplayBtn && autoplayBtn.getAttribute('aria-checked') === 'true') {
                        autoplayBtn.click();
                    }
                }
            } catch(e) {}
        }
    }

    // Pause on tab switch
    if (PAUSE_ON_SWITCH) {
        document.addEventListener('visibilitychange', function() {
            const video = document.querySelector('video');
            if (!video) return;
            if (document.hidden && !video.paused) {
                video.pause();
                video._spurPaused = true;
            } else if (!document.hidden && video._spurPaused) {
                video.play();
                video._spurPaused = false;
            }
        });
    }

    // Apply on initial load and navigation
    const observer = new MutationObserver(applySettings);
    observer.observe(document.body, { childList: true, subtree: true });
    setTimeout(applySettings, 1000);
    setTimeout(applySettings, 3000);
})();
