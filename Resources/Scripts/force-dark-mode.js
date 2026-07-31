// Stride Browser - Force Dark Mode (powered by Dark Reader, MIT License)
// Simply enables Dark Reader on all pages when the user has Force Dark Mode on.
// No detection — the user explicitly opted in. Like Chrome's #enable-force-dark.
// Dark Reader: https://github.com/darkreader/darkreader — MIT License
(function() {
    'use strict';

    // Skip internal/blank pages
    if (!location.hostname || location.protocol === 'data:' || location.protocol === 'about:') return;

    // Skip domains that are known to completely break with Dark Reader (e.g. Google Sign-In)
    const excludeDomains = [
        'accounts.google.com'
    ];
    if (excludeDomains.includes(location.hostname)) return;

    // Prevent white flash by hiding the document completely until Dark Reader is ready
    const antiFouc = document.createElement('style');
    antiFouc.id = 'stride-anti-fouc';
    antiFouc.textContent = 'html { opacity: 0 !important; transition: none !important; }';
    
    if (document.head || document.documentElement) {
        (document.head || document.documentElement).appendChild(antiFouc);
    } else {
        const observer = new MutationObserver(() => {
            if (document.head || document.documentElement) {
                (document.head || document.documentElement).appendChild(antiFouc);
                observer.disconnect();
            }
        });
        observer.observe(document, { childList: true, subtree: true });
    }

    let retries = 0;

    function removeFouc() {
        const style = document.getElementById('stride-anti-fouc');
        if (style && style.parentNode) {
            style.parentNode.removeChild(style);
        }
    }

    function tryEnable() {
        if (typeof DarkReader === 'undefined' || !DarkReader.enable) {
            if (retries > 100) { // 1 second timeout
                console.warn('Stride: DarkReader failed to load within 1 second. Aborting force dark mode.');
                removeFouc();
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
        } finally {
            // Allow one animation frame for DarkReader CSS to apply, then reveal document
            requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    removeFouc();
                });
            });
        }
    }

    // Call immediately to prevent white flash (FOUC)
    tryEnable();
})();
