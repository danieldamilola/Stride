// Stride Browser - Force Dark Mode (powered by Dark Reader, MIT License)
// Simply enables Dark Reader on all pages when the user has Force Dark Mode on.
(function() {
    'use strict';

    // Skip internal/blank pages
    if (!location.hostname || location.protocol === 'data:' || location.protocol === 'about:') return;

    // Skip domains that are known to completely break with Dark Reader (e.g. Google Sign-In)
    const excludeDomains = [
        'accounts.google.com'
    ];
    if (excludeDomains.includes(location.hostname)) return;

    let retries = 0;

    function tryEnable() {
        if (typeof DarkReader === 'undefined' || !DarkReader.enable) {
            if (retries > 100) { // 1 second timeout
                console.warn('Stride: DarkReader failed to load within 1 second. Aborting force dark mode.');
                return;
            }
            retries++;
            setTimeout(tryEnable, 10);
            return;
        }

        try {
            DarkReader.enable({
                brightness: 100,
                contrast: 100,
                sepia: 0
            });
        } catch (e) {
            console.error('Stride: DarkReader.enable threw an error:', e);
        }
    }

    tryEnable();
})();
