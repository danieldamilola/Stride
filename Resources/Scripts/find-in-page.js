// Spur Browser - Find in Page
// Injected to handle Ctrl+F find-in-page functionality within WebView2.
(function() {
    'use strict';

    // This script provides a minimal find-in-page overlay.
    // The browser engine calls window.chrome.webview.postMessage to communicate results.

    let currentMatch = 0;
    let matches = [];

    function findInPage(query) {
        clearHighlights();
        if (!query) return;

        const walker = document.createTreeWalker(
            document.body,
            NodeFilter.SHOW_TEXT,
            null
        );

        const textNodes = [];
        while (walker.nextNode()) {
            textNodes.push(walker.currentNode);
        }

        matches = [];
        const regex = new RegExp(query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi');

        textNodes.forEach(node => {
            const parent = node.parentElement;
            if (!parent || parent.tagName === 'SCRIPT' || parent.tagName === 'STYLE') return;

            let match;
            while ((match = regex.exec(node.textContent)) !== null) {
                matches.push({ node, index: match.index, length: match[0].length });
            }
        });

        if (matches.length > 0) {
            currentMatch = 0;
            highlightCurrent();
        }

        window.chrome.webview.postMessage('find-results:' + matches.length);
    }

    function highlightCurrent() {
        if (matches.length === 0) return;
        // Scroll the match into view
        const m = matches[currentMatch];
        if (m && m.node && m.node.parentElement) {
            m.node.parentElement.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    }

    function clearHighlights() {
        matches = [];
        currentMatch = 0;
    }

    function nextMatch() {
        if (matches.length === 0) return;
        currentMatch = (currentMatch + 1) % matches.length;
        highlightCurrent();
    }

    function prevMatch() {
        if (matches.length === 0) return;
        currentMatch = (currentMatch - 1 + matches.length) % matches.length;
        highlightCurrent();
    }

    // Expose to WebView2
    window.__spurFind = findInPage;
    window.__spurFindNext = nextMatch;
    window.__spurFindPrev = prevMatch;
    window.__spurFindClear = clearHighlights;
})();
