using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using System.Windows;
using StrideBrowser.Models;
using StrideBrowser.Models.LinkPreview;
using StrideBrowser.Services;

namespace StrideBrowser.Engine;

/// <summary>
/// Wires the IPC surface of a WebView2 control: web-message reception with
/// per-session token verification, new-window/tab handling, fullscreen events,
/// and posting messages back to the active tab. TabEngine supplies the tab
/// lifecycle callbacks; this class knows nothing about tab bookkeeping.
/// </summary>
public sealed class WebViewIpcBridge
{
    public event Action<string>? WebMessageReceived;
    public event Action<bool>? FullScreenChanged;

    /// <summary>Fires for tab-affecting messages (theme color) so the engine can refresh state.</summary>
    public event Action<BrowserTab>? TabStateChanged;

    public event Action<BrowserTab, string, Rect, LinkPreviewTrigger>? LinkPreviewRequested;

    private readonly Dispatcher _dispatcher;
    private readonly BrowserSettings _settings;
    private readonly IDownloadStore _downloadStore;
    private readonly Dictionary<string, Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation> _activeNativeDownloads;
    private readonly string _ipcToken;
    private readonly Func<Guid, bool> _isTabAlive;
    private readonly Func<Guid, dynamic?> _getWebView;
    private readonly Func<string, BrowserTab> _createTab;
    private readonly Func<BrowserTab, Task> _activateAsync;
    private readonly Action<BrowserTab> _closeTab;
    private readonly Services.LinkPreview.ILinkPreviewDownloadSuppressor _downloadSuppressor;

    public WebViewIpcBridge(
        Dispatcher dispatcher,
        BrowserSettings settings,
        IDownloadStore downloadStore,
        string ipcToken,
        Dictionary<string, Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation> activeNativeDownloads,
        Func<Guid, bool> isTabAlive,
        Func<Guid, dynamic?> getWebView,
        Func<string, BrowserTab> createTab,
        Func<BrowserTab, Task> activateAsync,
        Action<BrowserTab> closeTab,
        Services.LinkPreview.ILinkPreviewDownloadSuppressor downloadSuppressor)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _downloadStore = downloadStore;
        _ipcToken = ipcToken;
        _activeNativeDownloads = activeNativeDownloads;
        _isTabAlive = isTabAlive;
        _getWebView = getWebView;
        _createTab = createTab;
        _activateAsync = activateAsync;
        _closeTab = closeTab;
        _downloadSuppressor = downloadSuppressor;
    }

    public void Wire(dynamic wv, BrowserTab tab)
    {
        CoreWebView2 core = wv.CoreWebView2;
        TryDetachHostWindowClose((object)wv, (object)core);
        core.ContainsFullScreenElementChanged += (_, _) =>
        {
            if (!_isTabAlive(tab.Id)) return;
            _dispatcher.InvokeAsync(() =>
            {
                FullScreenChanged?.Invoke(core.ContainsFullScreenElement);
            });
        };
        WireWebMessageReceived(core, tab);

        Handlers.TabDownloadHandler.Wire(core, _dispatcher, _downloadStore, _activeNativeDownloads, _downloadSuppressor);

        WireNewWindowRequested(core, tab);

        core.WindowCloseRequested += (_, _) =>
        {
            _dispatcher.Invoke(() => _closeTab(tab));
        };

        Handlers.TabDialogHandler.Wire(core, _dispatcher, _settings);
    }

    /// <summary>
    /// Removes the WebView2 control's built-in WindowCloseRequested handler. That handler
    /// closes the host WPF window whenever page script calls window.close, which is how a
    /// download landing page takes the whole browser down. Stride closes only the tab, so
    /// the built-in handler must go. Returns true when a built-in handler was removed.
    /// </summary>
    public static bool TryDetachHostWindowClose(object? control, object? core)
    {
        if (control is null || core is null) return false;
        try
        {
            var evt = core.GetType().GetEvent("WindowCloseRequested");
            if (evt?.EventHandlerType is null) return false;

            var inner = FindInnerWebView2Base(control);
            if (inner is null) return false;

            var removed = false;
            for (var t = inner.GetType(); t is not null && t != typeof(object); t = t.BaseType)
            {
                var candidates = t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name.Contains("WindowClose", StringComparison.Ordinal));
                foreach (var method in candidates)
                {
                    try
                    {
                        var del = Delegate.CreateDelegate(evt.EventHandlerType, inner, method, throwOnBindFailure: false);
                        if (del is null) continue;
                        evt.RemoveEventHandler(core, del);
                        removed = true;
                    }
                    catch { /* try the next candidate */ }
                }
            }
            return removed;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Host window-close detach failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds the WebView2Base engine held inside a WebView2 or WebView2CompositionControl.
    /// The built-in window-close handler lives on that inner instance, not on the control.
    /// </summary>
    internal static object? FindInnerWebView2Base(object control)
    {
        for (var t = control.GetType(); t is not null && t != typeof(object); t = t.BaseType)
        {
            var field = t.GetField("m_webview2Base", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field?.GetValue(control) is object inner) return inner;
        }
        return null;
    }

    public void PostMessageToActiveTab(Guid? activeTabId, string message)
    {
        if (activeTabId is null) return;
        var wv = _getWebView(activeTabId.Value);
        if (wv?.CoreWebView2 is null) return;
        try
        {
            wv.CoreWebView2.PostWebMessageAsString(message);
        }
        catch (Exception ex) { Trace.WriteLine($"PostMessageToActiveTab failed: {ex.Message}"); }
    }

    /// <summary>
    /// Origins whose web messages bypass the per-session IPC token.
    /// <para>
    /// Only <c>local.assets</c> qualifies. That origin is mapped to the application's own
    /// read-only <c>Resources\Pages</c> folder, which ships with the binary and holds the
    /// bundled internal page templates.
    /// </para>
    /// <para>
    /// <c>user.assets</c> must never appear here. It is mapped to
    /// <c>%APPDATA%\Stride\Backgrounds</c>, a user-writable folder the Settings page
    /// opens in Explorer for the user to drop wallpaper images into. Any HTML file
    /// landing there becomes a document on a real HTTPS origin with <c>window.chrome.webview</c>
    /// available, so treating it as trusted hands unauthenticated callers the whole
    /// privileged route table (<c>install-update:</c>, <c>download-request:</c>,
    /// <c>download-open:</c>, <c>open:</c>, every <c>setting:</c> key) with no secret.
    /// The same reasoning already keeps the temp directory unmapped; see
    /// <see cref="WebViewFactory"/>. Backgrounds are only ever consumed as image URLs,
    /// so requiring the token costs nothing.
    /// </para>
    /// </summary>
    /// <summary>
    /// A theme colour is either empty, meaning "use the default", or a <c>#rrggbb</c> literal.
    /// Nothing else is accepted, because the value is applied to the toolbar tint and written
    /// into the UI without further encoding.
    /// </summary>
    public static bool IsThemeColorValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        if (value.Length != 7 || value[0] != '#') return false;
        for (int i = 1; i < value.Length; i++)
            if (!Uri.IsHexDigit(value[i])) return false;
        return true;
    }

    /// <summary>
    /// Origins whose web messages bypass the per-session IPC token.
    /// <para>
    /// Only <c>local.assets</c> qualifies. That origin is mapped to the application's own
    /// read-only <c>Resources\Pages</c> folder, which ships with the binary and holds the
    /// bundled internal page templates.
    /// </para>
    /// <para>
    /// <c>user.assets</c> must never appear here. It is mapped to
    /// <c>%APPDATA%\Stride\Backgrounds</c>, a user-writable folder the Settings page
    /// opens in Explorer for the user to drop wallpaper images into. Any HTML file
    /// landing there becomes a document on a real HTTPS origin with <c>window.chrome.webview</c>
    /// available, so treating it as trusted hands unauthenticated callers the whole
    /// privileged route table (<c>install-update:</c>, <c>download-request:</c>,
    /// <c>download-open:</c>, <c>open:</c>, every <c>setting:</c> key) with no secret.
    /// The same reasoning already keeps the temp directory unmapped; see
    /// <see cref="WebViewFactory"/>. Backgrounds are only ever consumed as image URLs,
    /// so requiring the token costs nothing.
    /// </para>
    /// </summary>
    public static bool IsTrustedAssetOrigin(string? source)
    {
        if (string.IsNullOrEmpty(source)) return false;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        return string.Equals(uri.Host, "local.assets", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when a web message came from the tab's top-level document rather than a frame
    /// inside it.
    /// <para>
    /// Both the theme colour and a link preview should describe the page the user is looking
    /// at. A cross-origin iframe posts into the same bridge, so without this an ad iframe could
    /// repaint the toolbar or force a preview.
    /// </para>
    /// <para>
    /// Compared against CoreWebView2.Source, read live at message time. The tab's own Url
    /// property is only refreshed on NavigationCompleted, so it is still the previous page's
    /// address when a content script fires on document creation. Fragment and query are
    /// ignored because neither changes which document is showing.
    /// </para>
    /// <para>
    /// Fails open when either side cannot be parsed. The value is shape-checked and carries no
    /// privilege, so an unreadable URL is not worth breaking the feature over.
    /// </para>
    /// </summary>
    public static bool IsSameTopLevelDocument(string? source, string? topLevelSource)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(topLevelSource)) return true;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var from)) return true;
        if (!Uri.TryCreate(topLevelSource, UriKind.Absolute, out var top)) return true;

        return string.Equals(from.Scheme, top.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(from.Host, top.Host, StringComparison.OrdinalIgnoreCase)
            && from.Port == top.Port
            && string.Equals(from.AbsolutePath.TrimEnd('/'), top.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads CoreWebView2.Source, which is null while the control is torn down.
    /// </summary>
    private static string? TopLevelSource(CoreWebView2 core)
    {
        try { return core.Source; }
        catch (Exception ex) { Trace.WriteLine($"TopLevelSource read failed: {ex.Message}"); return null; }
    }

    /// <summary>Hosts allowed to request a wallpaper download through the bridge.</summary>
    private static readonly string[] WallhavenHosts = { "wallhaven.cc", "www.wallhaven.cc" };

    /// <summary>
    /// True when a web message came from wallhaven.cc or its www form. The check is on the
    /// exact host, so a lookalike such as wallhaven.cc.evil.test does not match.
    /// </summary>
    public static bool IsWallhavenSource(string? source)
    {
        if (string.IsNullOrEmpty(source)) return false;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        return Array.Exists(WallhavenHosts, h => string.Equals(uri.Host, h, StringComparison.OrdinalIgnoreCase));
    }

    private void WireWebMessageReceived(CoreWebView2 core, BrowserTab tab)
    {
        core.WebMessageReceived += (_, e) =>
        {
            if (!_isTabAlive(tab.Id)) return;

            var msg = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(msg)) return;

            bool isTrustedLocalAsset = IsTrustedAssetOrigin(e.Source);

            // THEME_COLOR is emitted by the injected theme-color script with no token. It is
            // not a privileged message: the value is derived from the page's own computed
            // styles, so the page could send it unauthenticated anyway. Publishing the session
            // token to that script only widened the set of origins able to read it. The value
            // is shape-checked because it is applied to UI.
            if (msg.StartsWith("THEME_COLOR:", StringComparison.Ordinal))
            {
                var colorStr = msg.Substring("THEME_COLOR:".Length);
                if (!IsThemeColorValue(colorStr))
                {
                    Trace.WriteLine($"Ignoring malformed THEME_COLOR from {e.Source}");
                    return;
                }
                // The sender must be the tab's own top-level document. A cross-origin iframe
                // on the page posts to the same bridge, so without this an ad iframe could
                // repaint the toolbar. NavigationCompleted re-frames the value anyway.
                if (!IsSameTopLevelDocument(e.Source, TopLevelSource(core))) return;
                _dispatcher.InvokeAsync(() =>
                {
                    tab.ThemeColor = colorStr;
                    TabStateChanged?.Invoke(tab);
                });
                return;
            }

            // LINK_PREVIEW_PEEK arrives without the session token. It carries no privilege of its own:
            // it asks the host to consider showing a preview, and LinkPreviewService runs the
            // URL through LinkPreviewPolicy before anything is fetched or rendered. The token
            // used to guard it only proved the sender had read the injected script, which the
            // page itself is the source of.
            // The wallhaven download button posts this from a page on wallhaven.cc. Authorisation is by
            // message source origin, not by a token, because the old scheme published the
            // session token as a global on those origins. Any script on wallhaven.cc could read
            // it and drive every privileged route; this way only this one route is reachable,
            // and only from that site.
            if (msg.StartsWith("wallhaven-download:", StringComparison.Ordinal))
            {
                if (!IsWallhavenSource(e.Source)) return;
                if (!IsSameTopLevelDocument(e.Source, TopLevelSource(core))) return;
                var wallhavenUrl = msg.Substring("wallhaven-download:".Length);
                _dispatcher.Invoke(() => WebMessageReceived?.Invoke(
                    WebMessagePrefix.DownloadRequest + wallhavenUrl));
                return;
            }

            if (msg.StartsWith("LINK_PREVIEW_PEEK:", StringComparison.Ordinal))
            {
                if (!_settings.LinkPreviewEnabled) return;

                var json = msg.Substring("LINK_PREVIEW_PEEK:".Length);
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var url = root.GetProperty("url").GetString() ?? string.Empty;
                    if (!root.TryGetProperty("rect", out var rectArr) ||
                        rectArr.ValueKind != System.Text.Json.JsonValueKind.Array ||
                        rectArr.GetArrayLength() < 4)
                    {
                        return;
                    }

                    if (rectArr[0].ValueKind != System.Text.Json.JsonValueKind.Number ||
                        rectArr[1].ValueKind != System.Text.Json.JsonValueKind.Number ||
                        rectArr[2].ValueKind != System.Text.Json.JsonValueKind.Number ||
                        rectArr[3].ValueKind != System.Text.Json.JsonValueKind.Number)
                    {
                        return;
                    }

                    var left = rectArr[0].GetDouble();
                    var top = rectArr[1].GetDouble();
                    var width = rectArr[2].GetDouble();
                    var height = rectArr[3].GetDouble();
                    var rect = new Rect(left, top, width, height);
                    var trigger = LinkPreviewTrigger.AltPress;
                    _dispatcher.InvokeAsync(() => LinkPreviewRequested?.Invoke(tab, url, rect, trigger));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"LinkPreview parse failed: {ex.Message}");
                }
                return;
            }

            // A web message from a subframe never carries the privileged intent of the page the user is
            // on. Refusing frames early also keeps a cross-origin iframe from reaching the
            // internal-page routes if it ever guessed the session token.
            if (!IsSameTopLevelDocument(e.Source, TopLevelSource(core))) return;

            if (!isTrustedLocalAsset)
            {
                // SECURITY: Internal pages loaded via NavigateToString (origin = about:blank)
                // embed a per-session secret token. The token stays in the binary and in those
                // templates. It is no longer substituted into any script injected into a remote
                // page, so a site that can read its own injected source has nothing to read.
                if (!msg.StartsWith(_ipcToken + ":", StringComparison.Ordinal))
                {
                    return;
                }
                // Strip the token prefix before forwarding the payload
                msg = msg[(_ipcToken.Length + 1)..];
            }

            if (!string.IsNullOrEmpty(msg))
            {
                _dispatcher.Invoke(() => WebMessageReceived?.Invoke(msg));
            }
        };
    }

    private void WireNewWindowRequested(CoreWebView2 core, BrowserTab tab)
    {
        core.NewWindowRequested += (_, e) =>
        {
            // Treat as popup if it requests specific dimensions or isn't explicitly user-initiated (e.g. OAuth flows)
            if (e.WindowFeatures.HasPosition || e.WindowFeatures.HasSize || !e.IsUserInitiated)
            {
                // Let WebView2 handle the popup natively! This is the most reliable way to handle OAuth popups
                // without breaking window.opener or causing the site to think popups are blocked.
                e.Handled = false;
                return;
            }

            // Otherwise, handle it ourselves by opening a new Stride tab
            e.Handled = true;
            var deferral = e.GetDeferral();

            _ = _dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // Create tab with a sentinel URL so NavigateInitialUrl does NOT navigate it.
                    // This keeps the CoreWebView2 clean so we can assign it to e.NewWindow.
                    var newTab = _createTab("internal://pending-native");

                    // ActivateAsync is triggered via TabStateChanged but we await it directly to ensure readiness
                    await _activateAsync(newTab);

                    var wv = _getWebView(newTab.Id);
                    if (wv?.CoreWebView2 != null)
                    {
                        // Fix background color (internal:// made it transparent, we want opaque for the popup)
                        if (wv is Microsoft.Web.WebView2.Wpf.WebView2 std4) std4.DefaultBackgroundColor = _settings.ForceDarkMode ? WebViewFactory.DarkBackground : System.Drawing.Color.White;
                        else if (wv is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl comp4) comp4.DefaultBackgroundColor = _settings.ForceDarkMode ? WebViewFactory.DarkBackground : System.Drawing.Color.White;

                        // Let WebView2 do the navigation natively!
                        // This allows extensions (like uBlock Origin) to intercept the new window request!
                        e.NewWindow = wv.CoreWebView2;
                        newTab.Url = e.Uri; // Update the UI to reflect the requested URL
                    }
                }
                catch (Exception ex) { Trace.WriteLine($"NewWindowRequested error: {ex.Message}"); }
                finally
                {
                    deferral.Complete();
                }
            });
        };
    }
}