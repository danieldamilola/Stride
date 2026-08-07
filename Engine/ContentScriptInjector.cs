using Microsoft.Web.WebView2.Core;
using StrideBrowser.Helpers;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine;

public sealed class ContentScriptInjector
{
    private readonly YouTubeEnhancer _youtubeEnhancer;
    private readonly YouTubeUnhook _youtubeUnhook;

    private static readonly HashSet<string> _trustedExternalOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "wallhaven.cc",
        "www.wallhaven.cc",
    };

    public ContentScriptInjector(YouTubeEnhancer youtubeEnhancer, YouTubeUnhook youtubeUnhook)
    {
        _youtubeEnhancer = youtubeEnhancer;
        _youtubeUnhook = youtubeUnhook;
    }

    public async Task InjectAsync(CoreWebView2 core, BrowserSettings settings, string ipcToken)
    {
        if (settings.AdBlockEnabled)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                ResourceLoader.Load("Resources.Scripts.youtube-adnuke.js"));
        }

        var enhancer = _youtubeEnhancer.GetScript(settings);
        if (!string.IsNullOrEmpty(enhancer))
            await core.AddScriptToExecuteOnDocumentCreatedAsync(enhancer);

        // Wallhaven Direct Downloader
        var hostsJson = string.Join(", ", _trustedExternalOrigins.Select(h => $"\"{h}\""));
        var tokenScript = $$"""
(function() {
    var allowedHosts = [{{hostsJson}}];
    if (allowedHosts.indexOf(window.location.hostname) !== -1) {
        window.__T = '{{ipcToken}}:';
    }
})();
""";
        await core.AddScriptToExecuteOnDocumentCreatedAsync(tokenScript);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            ResourceLoader.Load("Resources.Scripts.wallhaven-downloader.js"));

        var unhook = _youtubeUnhook.GetScript(settings);
        if (!string.IsNullOrEmpty(unhook))
            await core.AddScriptToExecuteOnDocumentCreatedAsync(unhook);
            
        // Adaptive Theme Color Extractor (Ported from Adaptive-Tab-Bar-Colour Extension)
        var themeColorScript = @"
(function() {
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
        
        var isDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
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
                            window.chrome.webview.postMessage('THEME_COLOR:' + h);
                            return;
                        }
                    }
                }
            }
        } catch(e) {}

        // 2. Fall back to meta theme-color tag
        var metaTags = document.querySelectorAll('meta[name=""theme-color""]');
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
            window.chrome.webview.postMessage('THEME_COLOR:' + hex);
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
                        window.chrome.webview.postMessage('THEME_COLOR:' + h);
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
                        window.chrome.webview.postMessage('THEME_COLOR:' + h);
                        return;
                    }
                }
            }
        } catch(e) {}

        window.chrome.webview.postMessage('THEME_COLOR:');
    }
    
    let dispatchTimeout;
    let lastSentAt = 0;
    const throttleIntervalMs = 250;

    function sendColour() {
        clearTimeout(dispatchTimeout);
        const remaining = throttleIntervalMs + lastSentAt - Date.now();
        const dispatch = function() {
            if (document.visibilityState !== 'visible') return;
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
        ['click', 'resize', 'scroll', 'visibilitychange', 'DOMContentLoaded', 'load'].forEach(function(event) {
            window.addEventListener(event, sendColour);
        });
        
        ['transitionend', 'transitioncancel', 'animationend', 'animationcancel'].forEach(function(event) {
            window.addEventListener(event, sendColourRequiresFocus);
        });

        var observer = new MutationObserver(function(mutations) {
            sendColour();
        });
        
        function tryObserve() {
            if (document.head && document.documentElement) {
                observer.observe(document.head, { childList: true, subtree: true, attributes: true, attributeFilter: ['content'] });
                observer.observe(document.documentElement, { childList: true, attributes: false });
                sendColour();
            } else {
                setTimeout(tryObserve, 50);
            }
        }
        tryObserve();
    }
    
    enableDynamic();
})();
";
        await core.AddScriptToExecuteOnDocumentCreatedAsync(themeColorScript);

    }
}
