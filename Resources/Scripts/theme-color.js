(function() {
    // The session token is not needed here. The host already checks the message source
    // before it accepts a THEME_COLOR value, and everything this script computes comes from
    // the page's own computed styles, so the page could send it unauthenticated anyway.
    // Publishing the token into this closure only widened the set of pages that could read it.
    var initialEnabled = typeof __STRIDE_THEME_ENABLED__ === 'undefined' ? true : __STRIDE_THEME_ENABLED__;
    if (window.top && window.top !== window) return;
    if (window.__strideThemeColor) {
        window.__strideThemeColor.setEnabled(initialEnabled);
        return;
    }
    var enabled = false, lastColor;
    var canvas, context;
    var t = function(value) {
        if (!enabled || value === lastColor) return;
        lastColor = value;
        window.chrome.webview.postMessage('THEME_COLOR:' + value);
    };

    function colorToHex(color, baseColor) {
        if (!color || color === 'rgba(0, 0, 0, 0)' || color === 'transparent') return '';
        if (!context) {
            canvas = document.createElement('canvas');
            canvas.width = canvas.height = 1;
            context = canvas.getContext('2d');
        }
        var ctx = context;
        if (!ctx) return '';

        ctx.fillStyle = '#000000';
        ctx.fillStyle = color;
        var parsed1 = ctx.fillStyle;
        ctx.fillStyle = '#ffffff';
        ctx.fillStyle = color;
        var parsed2 = ctx.fillStyle;

        if (parsed1 === '#000000' && parsed2 === '#ffffff') return '';

        var isDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
        ctx.clearRect(0, 0, 1, 1);
        ctx.fillStyle = isDark ? '#121212' : '#ffffff';
        ctx.fillRect(0, 0, 1, 1);
        if (baseColor && baseColor !== 'rgba(0, 0, 0, 0)' && baseColor !== 'transparent') {
            ctx.fillStyle = baseColor;
            ctx.fillRect(0, 0, 1, 1);
        }
        ctx.fillStyle = color;
        ctx.fillRect(0, 0, 1, 1);

        var data = ctx.getImageData(0, 0, 1, 1).data;
        function hex(x) { return ('0' + x.toString(16)).slice(-2); }
        return '#' + hex(data[0]) + hex(data[1]) + hex(data[2]);
    }

    function extractThemeColor() {
        var isDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
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
                        if (h) {
                            t(h);
                            return;
                        }
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
        if (hex) {
            t(hex);
            return;
        }

        // 3. Fall back to body or document element background
        try {
            if (document.body) {
                var bodyStyle = window.getComputedStyle(document.body);
                var bg = bodyStyle.backgroundColor;
                if (bg && bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent') {
                    var h = colorToHex(bg, baseBg);
                    if (h) {
                        t(h);
                        return;
                    }
                }
            }

            if (document.documentElement) {
                var docStyle = window.getComputedStyle(document.documentElement);
                var bg = docStyle.backgroundColor;
                if (bg && bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent') {
                    var h = colorToHex(bg, baseBg);
                    if (h) {
                        t(h);
                        return;
                    }
                }
            }
        } catch(e) {}

        t('');
    }

    let dispatchTimeout, observeTimeout, observer, observedBody;
    let lastSentAt = 0;
    const scheme = window.matchMedia?.('(prefers-color-scheme: dark)');
    const dynamicEvents = ['click', 'resize', 'scroll', 'visibilitychange', 'DOMContentLoaded', 'load'];
    const animationEvents = ['transitionend', 'transitioncancel', 'animationend', 'animationcancel'];
    const throttleIntervalMs = 250;

    function sendColour() {
        if (!enabled) return;
        if (observer && document.body && document.body !== observedBody) {
            observedBody = document.body;
            observer.observe(observedBody, { attributes:true, attributeFilter:['class','style'] });
        }
        clearTimeout(dispatchTimeout);
        const remaining = throttleIntervalMs + lastSentAt - Date.now();
        const dispatch = function() {
            if (!enabled || document.visibilityState !== 'visible') return;
            lastSentAt = Date.now();
            extractThemeColor();
        };
        if (remaining <= 0) {
            dispatch();
        } else {
            dispatchTimeout = setTimeout(dispatch, remaining);
        }
    }

    function sendColourRequiresFocus() {
        if (document.hasFocus()) sendColour();
    }

    function enableDynamic() {
        dynamicEvents.forEach(event => window.addEventListener(event, sendColour));
        animationEvents.forEach(event => window.addEventListener(event, sendColourRequiresFocus));
        scheme?.addEventListener?.('change', sendColour);
        observer = new MutationObserver(sendColour);
        function tryObserve() {
            if (!enabled) return;
            if (document.head && document.documentElement) {
                observer.observe(document.head, { childList:true, subtree:true, attributes:true, attributeFilter:['content','media'] });
                observer.observe(document.documentElement, { childList:true, attributes:true, attributeFilter:['class','style'] });
                sendColour();
            } else {
                observeTimeout = setTimeout(tryObserve,50);
            }
        }
        tryObserve();
    }
    function setEnabled(value) {
        value = !!value;
        if (enabled === value) return;
        enabled = value;
        if (enabled) {
            lastColor = undefined;
            lastSentAt = 0;
            enableDynamic();
        } else {
            clearTimeout(dispatchTimeout);
            clearTimeout(observeTimeout);
            observer?.disconnect();
            observedBody = null;
            dynamicEvents.forEach(event => window.removeEventListener(event,sendColour));
            animationEvents.forEach(event => window.removeEventListener(event,sendColourRequiresFocus));
            scheme?.removeEventListener?.('change',sendColour);
        }
    }
    window.__strideThemeColor = { setEnabled };
    setEnabled(initialEnabled);
})();
