using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly HashSet<string> _activeNativeDownloads;
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
        HashSet<string> activeNativeDownloads,
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

    private void WireWebMessageReceived(CoreWebView2 core, BrowserTab tab)
    {
        core.WebMessageReceived += (_, e) =>
        {
            if (!_isTabAlive(tab.Id)) return;

            var msg = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(msg)) return;

            bool isTrustedLocalAsset = false;
            try
            {
                if (e.Source.StartsWith("http://local.assets/", StringComparison.OrdinalIgnoreCase) ||
                    e.Source.StartsWith("https://local.assets/", StringComparison.OrdinalIgnoreCase) ||
                    e.Source.StartsWith("https://user.assets/", StringComparison.OrdinalIgnoreCase))
                {
                    isTrustedLocalAsset = true;
                }
            }
            catch (Exception ex) { Trace.WriteLine(ex); }

            if (msg.StartsWith("THEME_COLOR:"))
            {
                var colorStr = msg.Substring("THEME_COLOR:".Length);
                _dispatcher.InvokeAsync(() =>
                {
                    tab.ThemeColor = colorStr;
                    TabStateChanged?.Invoke(tab);
                });
                return;
            }

            if (msg.StartsWith("LINK_PREVIEW_PEEK:"))
            {
                var json = msg.Substring("LINK_PREVIEW_PEEK:".Length);
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var url = root.GetProperty("url").GetString() ?? string.Empty;
                    var rectArr = root.GetProperty("rect");
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

            if (!isTrustedLocalAsset)
            {
                // SECURITY: Internal pages loaded via NavigateToString (origin = about:blank) embed a per-session secret token
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