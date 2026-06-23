// Spur Browser - YouTube Ad Skip
// Automatically skips YouTube ads by clicking the skip button and speeding through unskippable ads.
(function() {
    'use strict';

    function skipAds() {
        // Click "Skip Ad" button if available
        const skipBtn = document.querySelector('.ytp-skip-ad-button, .ytp-ad-skip-button, .ytp-ad-skip-button-modern');
        if (skipBtn) {
            skipBtn.click();
            return;
        }

        // Speed through unskippable ads
        const ad = document.querySelector('.ad-showing');
        if (ad) {
            const video = document.querySelector('video');
            if (video && video.duration && isFinite(video.duration)) {
                video.currentTime = video.duration;
            }
        }

        // Remove overlay ads
        const overlayAd = document.querySelector('.ytp-ad-overlay-container');
        if (overlayAd) {
            overlayAd.remove();
        }
    }

    // Run periodically to catch ads
    setInterval(skipAds, 500);

    // Also observe DOM changes
    const observer = new MutationObserver(skipAds);
    observer.observe(document.body, { childList: true, subtree: true });
})();
