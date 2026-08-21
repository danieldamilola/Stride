(function() {
    try { if (window.top !== window.self) return; } catch (e) { return; }
    if (window.__strideLinkPreviewInstalled) return;
    window.__strideLinkPreviewInstalled = true;

    var altHeld = false;
    var lastSentUrl = "";
    var lastSentAt = 0;

    function isValidForPreview(href) {
        if (!href) return false;
        href = href.trim();
        if (!href) return false;
        if (href.indexOf("javascript:") === 0) return false;
        if (href.indexOf("mailto:") === 0) return false;
        if (href.indexOf("tel:") === 0) return false;
        if (href.indexOf("blob:") === 0) return false;
        if (href.indexOf("data:") === 0) return false;
        if (href.charAt(0) === "#") return false;
        if (href.indexOf("http://") === 0 || href.indexOf("https://") === 0) return true;
        if (href.indexOf("/") === 0) return true;
        return false;
    }

    function getAnchor(el) {
        if (!el) return null;
        if (el.closest) return el.closest("a[href]");
        var cur = el;
        while (cur) {
            if (cur.tagName === "A" && cur.getAttribute("href")) return cur;
            cur = cur.parentElement;
        }
        return null;
    }

    function sendPreview(anchor) {
        if (!anchor || !anchor.href) return;
        var href = anchor.href;
        if (!isValidForPreview(href)) return;
        var now = Date.now();
        if (href === lastSentUrl && now - lastSentAt < 400) return;
        var rect = anchor.getBoundingClientRect();
        if (!rect || rect.width < 4 || rect.height < 4) return;
        lastSentUrl = href;
        lastSentAt = now;
        try {
            if (window.chrome && window.chrome.webview) {
                var payload = JSON.stringify({
                    url: href,
                    rect: [rect.left, rect.top, rect.width, rect.height],
                    trigger: "alt-press"
                });
                window.chrome.webview.postMessage("LINK_PREVIEW_PEEK:" + payload);
            }
        } catch (e) {}
    }

    document.addEventListener("keydown", function(e) {
        if (e.key === "Alt" || e.code === "AltLeft" || e.code === "AltRight" || e.altKey) altHeld = true;
    }, true);

    document.addEventListener("keyup", function(e) {
        if (e.key === "Alt" || e.code === "AltLeft" || e.code === "AltRight") altHeld = false;
        if (!e.altKey) altHeld = false;
    }, true);

    window.addEventListener("blur", function() { altHeld = false; });

    document.addEventListener("click", function(e) {
        if (altHeld || e.altKey) {
            var a = getAnchor(e.target);
            if (a && isValidForPreview(a.href)) {
                e.preventDefault();
                e.stopPropagation();
                if (e.stopImmediatePropagation) e.stopImmediatePropagation();
                sendPreview(a);
            }
        }
    }, true);

    document.addEventListener("keydown", function(e) {
        if (e.key === "Enter" && (e.altKey || altHeld)) {
            var focused = document.activeElement ? getAnchor(document.activeElement) : null;
            if (focused && isValidForPreview(focused.href)) {
                e.preventDefault();
                e.stopPropagation();
                sendPreview(focused);
            }
        }
    }, true);
})();
