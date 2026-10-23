(function() {
    // Only the top-level document describes the page the user is looking at. The host
    // drops messages that arrive from a frame, so the script does no work in one.
    if (window.top !== window.self) return;

    // The host bakes the adaptive tint setting into the script it registers, so a
    // document knows whether it should watch the page before the first event fires.
    // A setting change under a live page arrives as a control message instead.
    var enabled = __UMBER_THEME_TINT_ENABLED__;

    const pageEvents = ['click', 'resize', 'scroll', 'visibilitychange', 'DOMContentLoaded', 'load'];
    const animationEvents = ['transitionend', 'transitioncancel', 'animationend', 'animationcancel'];
    const throttleIntervalMs = 250;

    let lastSent = null;
    let lastSentAt = 0;
    let dispatchTimeout = null;
    let observeTimeout = null;
    let observer = null;
    let attached = false;

    const colorScheme = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

    function post(color) {
        window.chrome.webview.postMessage('THEME_COLOR:' + color);
    }

    function colorToHex(color, baseColor) {
        if (!color || color === 'rgba(0, 0, 0, 0)' || color === 'transparent') return '';
        var canvas = document.createElement('canvas');
        canvas.width = 1;
        canvas.height = 1;
        var ctx = canvas.getContext('2d');

        ctx.fillStyle = '#000000';
        ctx.fillStyle = color;
        var parsed1 = ctx.fillStyle;
        ctx.fillStyle = '#ffffff';
        ctx.fillStyle = color;
        var parsed2 = ctx.fillStyle;

        if (parsed1 === '#000000' && parsed2 === '#ffffff') return '';

        var isDark = colorScheme ? colorScheme.matches : false;
        ctx.fillStyle = isDark ? '#121212' : '#ffffff';
        if (baseColor && baseColor !== 'rgba(0, 0, 0, 0)' && baseColor !== 'transparent') {
            ctx.fillStyle = baseColor;
        }
        ctx.fillRect(0, 0, 1, 1);
        ctx.fillStyle = color;
        ctx.fillRect(0, 0, 1, 1);

        var data = ctx.getImageData(0, 0, 1, 1).data;
        function hex(x) { return ('0' + x.toString(16)).slice(-2); }
        return '#' + hex(data[0]) + hex(data[1]) + hex(data[2]);
    }

    // Returns the colour of the page, or an empty string when the page has none.
    // The caller decides whether that colour is worth a message.
    function extractThemeColor() {
        var isDark = colorScheme ? colorScheme.matches : false;
        var bodyBg = document.body ? window.getComputedStyle(document.body).backgroundColor : null;
        var docBg = document.documentElement ? window.getComputedStyle(document.documentElement).backgroundColor : null;
        var baseBg = (bodyBg && bodyBg !== 'rgba(0, 0, 0, 0)') ? bodyBg : docBg;

        // 1. Prioritize visual top element (dynamic scrolling adaptation)
        try {
            var elements = document.elementsFromPoint(window.innerWidth / 2, 3);
            for (var i = 0; i < elements.length; i++) {
                var el = elements[i];
                if (el.offsetWidth >= window.innerWidth * 0.9 && el.offsetHeight >= 20) {
                    var style = window.getComputedStyle(el);
                    var bg = style.backgroundColor;
                    if (bg && bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent') {
                        var h = colorToHex(bg, baseBg);
                        if (h) return h;
                    }
                }
            }
        } catch(e) {}

        // 2. Fall back to meta theme-color tag
        var metaTags = document.querySelectorAll('meta[name="theme-color"]');
        var themeColor = '';
        for (var i = 0; i < metaTags.length; i++) {
            var media = metaTags[i].getAttribute('media');
            if (!media) {
                if (!themeColor) themeColor = metaTags[i].getAttribute('content');
            } else if (isDark && media === '(prefers-color-scheme: dark)') {
                themeColor = metaTags[i].getAttribute('content');
            } else if (!isDark && media === '(prefers-color-scheme: light)') {
                themeColor = metaTags[i].getAttribute('content');
            }
        }
        var hex = colorToHex(themeColor, baseBg);
        if (hex) return hex;

        // 3. Fall back to body or document element background
        try {
            if (document.body) {
                var bodyStyle = window.getComputedStyle(document.body);
                var bg = bodyStyle.backgroundColor;
                if (bg && bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent') {
                    var h = colorToHex(bg, baseBg);
                    if (h) return h;
                }
            }

            if (document.documentElement) {
                var docStyle = window.getComputedStyle(document.documentElement);
                var bg = docStyle.backgroundColor;
                if (bg && bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent') {
                    var h = colorToHex(bg, baseBg);
                    if (h) return h;
                }
            }
        } catch(e) {}

        return '';
    }

    // Reports the page colour, but only when it differs from the colour the host
    // already has. An unchanged colour costs the host a full tab refresh, and a page
    // that scrolls without changing its colour must not produce one message per
    // scroll event.
    function sendColour() {
        if (!enabled) return;
        clearTimeout(dispatchTimeout);
        dispatchTimeout = null;
        const remaining = throttleIntervalMs + lastSentAt - Date.now();
        const dispatch = function() {
            dispatchTimeout = null;
            if (!enabled) return;
            if (document.visibilityState !== 'visible') return;
            lastSentAt = Date.now();
            const color = extractThemeColor();
            if (color === lastSent) return;
            lastSent = color;
            post(color);
        };
        if (remaining <= 0) {
            dispatch();
        } else {
            dispatchTimeout = setTimeout(dispatch, remaining);
        }
    }

    function sendColourRequiresFocus() {
        if (!enabled) return;
        if (document.hasFocus()) sendColour();
    }

    function observe() {
        if (!observer) return;
        if (document.head && document.documentElement) {
            observer.observe(document.head, { childList: true, subtree: true, attributes: true, attributeFilter: ['content'] });
            observer.observe(document.documentElement, { childList: true, attributes: false });
            sendColour();
        } else {
            observeTimeout = setTimeout(observe, 50);
        }
    }

    function attach() {
        if (attached) return;
        attached = true;

        pageEvents.forEach(function(event) {
            window.addEventListener(event, sendColour);
        });
        animationEvents.forEach(function(event) {
            window.addEventListener(event, sendColourRequiresFocus);
        });
        if (colorScheme && colorScheme.addEventListener) {
            colorScheme.addEventListener('change', sendColour);
        }

        observer = new MutationObserver(function(mutations) {
            sendColour();
        });
        observe();
    }

    // Stops every listener, the observer and every timer. Nothing on the page is
    // read and nothing is sent while the setting is off.
    function detach() {
        if (!attached) return;
        attached = false;

        pageEvents.forEach(function(event) {
            window.removeEventListener(event, sendColour);
        });
        animationEvents.forEach(function(event) {
            window.removeEventListener(event, sendColourRequiresFocus);
        });
        if (colorScheme && colorScheme.removeEventListener) {
            colorScheme.removeEventListener('change', sendColour);
        }

        if (observer) {
            observer.disconnect();
            observer = null;
        }
        clearTimeout(dispatchTimeout);
        dispatchTimeout = null;
        clearTimeout(observeTimeout);
        observeTimeout = null;
    }

    // The host tells the document when the setting changes. Turning the setting on
    // starts the watchers again and reports the current colour, even when the
    // colour is the one the host already knows.
    function setEnabled(next) {
        if (next === enabled) return;
        enabled = next;
        lastSent = null;
        if (enabled) {
            attach();
            sendColour();
        } else {
            detach();
        }
    }

    function onHostMessage(event) {
        if (!event || typeof event.data !== 'string') return;
        if (event.data === 'theme-tint-control:1') setEnabled(true);
        else if (event.data === 'theme-tint-control:0') setEnabled(false);
    }

    if (window.chrome && window.chrome.webview && window.chrome.webview.addEventListener) {
        window.chrome.webview.addEventListener('message', onHostMessage);
    }

    if (enabled) attach();
})();
