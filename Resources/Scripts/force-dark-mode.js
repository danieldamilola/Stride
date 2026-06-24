// Stride Browser - Force Dark Mode (powered by Dark Reader, MIT License)
// Simply enables Dark Reader on all pages when the user has Force Dark Mode on.
// No detection — the user explicitly opted in. Like Chrome's #enable-force-dark.
// Dark Reader: https://github.com/darkreader/darkreader — MIT License
(function() {
    'use strict';

    // Skip internal/blank pages
    if (!location.hostname || location.protocol === 'data:' || location.protocol === 'about:') return;

    function tryEnable() {
        if (typeof DarkReader === 'undefined' || !DarkReader.enable) {
            setTimeout(tryEnable, 50);
            return;
        }

        DarkReader.enable({
            brightness: 100,
            contrast: 100,
            sepia: 0
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', tryEnable);
    } else {
        tryEnable();
    }
})();
