// Stride Browser - Force Dark Mode (powered by Dark Reader, MIT License)
// Detects if a site already has dark mode — only enables Dark Reader on light pages.
// Dark Reader: https://github.com/darkreader/darkreader — MIT License
(function() {
    'use strict';

    // Skip internal/blank pages
    if (!location.hostname || location.protocol === 'data:' || location.protocol === 'about:') return;

    function getBrightness(color) {
        if (!color || color === 'transparent' || color === 'rgba(0, 0, 0, 0)') return -1;
        var match = color.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/);
        if (!match) return -1;
        var r = parseInt(match[1]), g = parseInt(match[2]), b = parseInt(match[3]);
        // Relative luminance formula
        return (r * 299 + g * 587 + b * 114) / 1000;
    }

    /**
     * Samples the brightness of visible content areas on the page.
     * Returns the average brightness of sampled elements, or -1 if none found.
     * This catches cases where body is dark but content sections are light
     * (e.g., icons8.com, many modern sites with dark chrome but light content).
     */
    function sampleContentBrightness() {
        // Common content container selectors (ordered by specificity)
        var selectors = [
            'main', 'article', '[role="main"]',
            '#content', '#app', '#root', '#__next', '#__nuxt',
            '.main-content', '.page-content', '.content',
            '.container', '.wrapper'
        ];
        var samples = [];

        for (var i = 0; i < selectors.length; i++) {
            var els = document.querySelectorAll(selectors[i]);
            for (var j = 0; j < els.length && j < 3; j++) {
                var bg = window.getComputedStyle(els[j]).backgroundColor;
                var b = getBrightness(bg);
                if (b >= 0) samples.push(b);
            }
            if (samples.length >= 4) break; // enough samples
        }

        // Also sample the first few large visible divs as a fallback
        if (samples.length < 2) {
            var divs = document.querySelectorAll('body > div, body > section, body > main');
            for (var k = 0; k < divs.length && k < 5; k++) {
                var rect = divs[k].getBoundingClientRect();
                // Only sample elements that are visible and occupy meaningful space
                if (rect.width > 200 && rect.height > 100) {
                    var bg2 = window.getComputedStyle(divs[k]).backgroundColor;
                    var b2 = getBrightness(bg2);
                    if (b2 >= 0) samples.push(b2);
                }
            }
        }

        if (samples.length === 0) return -1;

        var sum = 0;
        for (var s = 0; s < samples.length; s++) sum += samples[s];
        return sum / samples.length;
    }

    function isPageAlreadyDark() {
        var body = document.body;
        if (!body) return false;

        var bodyBg = window.getComputedStyle(body).backgroundColor;
        var bodyBrightness = getBrightness(bodyBg);

        var html = document.documentElement;
        var htmlBg = window.getComputedStyle(html).backgroundColor;
        var htmlBrightness = getBrightness(htmlBg);

        // Use whichever is actually set (not transparent)
        var rootBrightness = bodyBrightness >= 0 ? bodyBrightness : htmlBrightness;

        // If the root is dark, also check actual content areas.
        // Many modern sites have a dark body but light content sections.
        if (rootBrightness >= 0 && rootBrightness < 50) {
            var contentBrightness = sampleContentBrightness();
            // If content areas are light (brightness > 150), the page is NOT truly dark
            if (contentBrightness > 150) return false;
            // If we found dark content areas, or couldn't find any, trust the root
            return true;
        }

        // Root is light — not dark
        if (rootBrightness >= 50) return false;

        // Root is transparent — check content directly
        var contentBrightness2 = sampleContentBrightness();
        if (contentBrightness2 >= 0 && contentBrightness2 < 50) return true;

        return false;
    }

    function tryEnable() {
        if (typeof DarkReader === 'undefined' || !DarkReader.enable) {
            setTimeout(tryEnable, 50);
            return;
        }

        // Wait a moment for CSS to load, then check if already dark
        setTimeout(function() {
            if (isPageAlreadyDark()) {
                // Site already has dark mode — don't apply Dark Reader
                return;
            }

            DarkReader.enable({
                brightness: 100,
                contrast: 100,
                sepia: 0
            });

            // Re-check after full load in case the site switches to dark theme late
            // (e.g., JS-driven theme detection)
            setTimeout(function() {
                if (isPageAlreadyDark()) {
                    DarkReader.disable();
                }
            }, 500);
        }, 300);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', tryEnable);
    } else {
        tryEnable();
    }
})();
