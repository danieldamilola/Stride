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
    private readonly string _telemetryToken;
    private readonly Func<Guid, bool> _isTabAlive;
    private readonly Func<Guid, dynamic?> _getWebView;
    private readonly Func<string, BrowserTab> _createTab;
    private readonly Func<BrowserTab, Task> _activateAsync;
    private readonly Action<BrowserTab> _closeTab;
    private readonly Func<BrowserTab, CancellationToken, Task<CoreWebView2?>> _ensureTabReadyAsync;
    private readonly Action<BrowserTab> _removeSentinelTab;
    private readonly Services.LinkPreview.ILinkPreviewDownloadSuppressor _downloadSuppressor;

    public WebViewIpcBridge(
        Dispatcher dispatcher,
        BrowserSettings settings,
        IDownloadStore downloadStore,
        string ipcToken,
        string telemetryToken,
        Dictionary<string, Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation> activeNativeDownloads,
        Func<Guid, bool> isTabAlive,
        Func<Guid, dynamic?> getWebView,
        Func<string, BrowserTab> createTab,
        Func<BrowserTab, Task> activateAsync,
        Action<BrowserTab> closeTab,
        Func<BrowserTab, CancellationToken, Task<CoreWebView2?>> ensureTabReadyAsync,
        Action<BrowserTab> removeSentinelTab,
        Services.LinkPreview.ILinkPreviewDownloadSuppressor downloadSuppressor)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _downloadStore = downloadStore;
        _ipcToken = ipcToken;
        _telemetryToken = telemetryToken;
        _activeNativeDownloads = activeNativeDownloads;
        _isTabAlive = isTabAlive;
        _getWebView = getWebView;
        _createTab = createTab;
        _activateAsync = activateAsync;
        _closeTab = closeTab;
        _ensureTabReadyAsync = ensureTabReadyAsync;
        _removeSentinelTab = removeSentinelTab;
        _downloadSuppressor = downloadSuppressor;
    }

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
        Func<BrowserTab, CancellationToken, Task<CoreWebView2?>> ensureTabReadyAsync,
        Action<BrowserTab> removeSentinelTab,
        Services.LinkPreview.ILinkPreviewDownloadSuppressor downloadSuppressor)
        : this(
            dispatcher,
            settings,
            downloadStore,
            ipcToken,
            Guid.NewGuid().ToString("N"),
            activeNativeDownloads,
            isTabAlive,
            getWebView,
            createTab,
            activateAsync,
            closeTab,
            ensureTabReadyAsync,
            removeSentinelTab,
            downloadSuppressor)
    {
    }

    internal void Wire(dynamic wv, BrowserTab tab, WebViewSubscriptions subscriptions)
    {
        CoreWebView2 core = wv.CoreWebView2;
        TryDetachHostWindowClose((object)wv, (object)core);
        EventHandler<object> onContainsFullScreenElementChanged = (_, _) =>
        {
            if (!_isTabAlive(tab.Id)) return;
            _dispatcher.InvokeAsync(() =>
            {
                FullScreenChanged?.Invoke(core.ContainsFullScreenElement);
            });
        };
        core.ContainsFullScreenElementChanged += onContainsFullScreenElementChanged;
        subscriptions.Add(() => core.ContainsFullScreenElementChanged -= onContainsFullScreenElementChanged);
        WireWebMessageReceived(core, tab, subscriptions);

        WireNewWindowRequested(core, tab, subscriptions);

        EventHandler<object> onWindowCloseRequested = (_, _) =>
        {
            _dispatcher.Invoke(() => _closeTab(tab));
        };
        core.WindowCloseRequested += onWindowCloseRequested;
        subscriptions.Add(() => core.WindowCloseRequested -= onWindowCloseRequested);
    }

    /// <summary>
    /// Wires the handlers that belong to the control for as long as it lives, not to the
    /// window it is shown in. Downloads and page dialogs must not be wired again when a
    /// tab moves, or the download table would gain a second entry per tab and a dialog
    /// would be owned by a window that is no longer on screen.
    /// </summary>
    public void WireLifetimeEvents(dynamic wv, BrowserTab tab)
    {
        CoreWebView2 core = wv.CoreWebView2;
        Handlers.TabDownloadHandler.Wire(core, _dispatcher, _downloadStore, _activeNativeDownloads, _downloadSuppressor);
        Handlers.TabDialogHandler.Wire(core, _dispatcher, _settings, () => Window.GetWindow((FrameworkElement)wv));
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

    public bool ProcessIncomingWebMessage(BrowserTab tab, string? source, string? msg)
    {
        if (!_isTabAlive(tab.Id) || string.IsNullOrEmpty(msg)) return false;

        // 1. External telemetry handling (theme-color, link-preview)
        if (msg.StartsWith(_telemetryToken + ":THEME_COLOR:", StringComparison.Ordinal))
        {
            var colorStr = msg.Substring((_telemetryToken + ":THEME_COLOR:").Length);
            _dispatcher.InvokeAsync(() =>
            {
                tab.ThemeColor = colorStr;
                TabStateChanged?.Invoke(tab);
            });
            return true;
        }

        if (msg.StartsWith(_telemetryToken + ":LINK_PREVIEW_PEEK:", StringComparison.Ordinal))
        {
            if (!_settings.LinkPreviewEnabled) return true;

            var json = msg.Substring((_telemetryToken + ":LINK_PREVIEW_PEEK:").Length);
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                var url = root.GetProperty("url").GetString() ?? string.Empty;
                if (!root.TryGetProperty("rect", out var rectArr) ||
                    rectArr.ValueKind != System.Text.Json.JsonValueKind.Array ||
                    rectArr.GetArrayLength() < 4)
                {
                    return true;
                }

                if (rectArr[0].ValueKind != System.Text.Json.JsonValueKind.Number ||
                    rectArr[1].ValueKind != System.Text.Json.JsonValueKind.Number ||
                    rectArr[2].ValueKind != System.Text.Json.JsonValueKind.Number ||
                    rectArr[3].ValueKind != System.Text.Json.JsonValueKind.Number)
                {
                    return true;
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
                Trace.WriteLine($"LinkPreview parse failed: {ex.Message}");
            }
            return true;
        }

        // 2. Trusted local assets (local.assets, user.assets)
        bool isTrustedLocalAsset = false;
        if (!string.IsNullOrEmpty(source))
        {
            if (source.StartsWith("http://local.assets/", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://local.assets/", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://user.assets/", StringComparison.OrdinalIgnoreCase))
            {
                isTrustedLocalAsset = true;
            }
        }

        if (isTrustedLocalAsset)
        {
            if (IpcSecurityPolicy.IsRouteAllowedForLocalAsset(msg))
            {
                _dispatcher.Invoke(() => WebMessageReceived?.Invoke(msg));
                return true;
            }
            return false;
        }

        // 3. Trusted external origin download requests (e.g. wallhaven.cc)
        if (msg.StartsWith(_telemetryToken + ":" + WebMessagePrefix.DownloadRequest, StringComparison.Ordinal) ||
            msg.StartsWith(WebMessagePrefix.DownloadRequest, StringComparison.Ordinal))
        {
            if (!string.IsNullOrEmpty(source) &&
                Uri.TryCreate(source, UriKind.Absolute, out var sourceUri) &&
                ContentScriptInjector.IsTrustedExternalOrigin(sourceUri.Host))
            {
                var payload = msg.StartsWith(_telemetryToken + ":", StringComparison.Ordinal)
                    ? msg[(_telemetryToken.Length + 1)..]
                    : msg;
                _dispatcher.Invoke(() => WebMessageReceived?.Invoke(payload));
                return true;
            }
            return false;
        }

        // 4. Privileged internal page commands
        // Source MUST be "about:blank" (internal pages rendered via NavigateToString)
        if (!string.Equals(source, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            Trace.WriteLine($"Rejected IPC command from untrusted origin: {source}");
            return false;
        }

        // Tab must have an active verified internal document identity
        var activeDoc = tab.ActiveInternalDocument;
        if (activeDoc == null || activeDoc.TabId != tab.Id)
        {
            Trace.WriteLine($"Rejected IPC command: tab {tab.Id} has no active internal document");
            return false;
        }

        // Message must start with the valid document token prefix
        if (!msg.StartsWith(activeDoc.DocumentToken + ":", StringComparison.Ordinal) &&
            !msg.StartsWith(_ipcToken + ":", StringComparison.Ordinal))
        {
            Trace.WriteLine("Rejected IPC command: missing or invalid document token prefix");
            return false;
        }

        var tokenLength = msg.StartsWith(activeDoc.DocumentToken + ":", StringComparison.Ordinal)
            ? activeDoc.DocumentToken.Length
            : _ipcToken.Length;
        var stripped = msg[(tokenLength + 1)..];

        // Check route permissions for this internal page kind
        if (!IpcSecurityPolicy.IsRouteAllowedForPage(activeDoc.PageKind, stripped))
        {
            Trace.WriteLine($"Rejected IPC command: route '{stripped}' is not permitted for page kind {activeDoc.PageKind}");
            return false;
        }

        _dispatcher.Invoke(() => WebMessageReceived?.Invoke(stripped));
        return true;
    }

    private void WireWebMessageReceived(CoreWebView2 core, BrowserTab tab, WebViewSubscriptions subscriptions)
    {
        EventHandler<CoreWebView2WebMessageReceivedEventArgs> onWebMessageReceived = (_, e) =>
        {
            var msg = e.TryGetWebMessageAsString();
            ProcessIncomingWebMessage(tab, e.Source, msg);
        };
        core.WebMessageReceived += onWebMessageReceived;
        subscriptions.Add(() => core.WebMessageReceived -= onWebMessageReceived);
    }

    public interface IDeferralToken
    {
        void Complete();
    }

    private sealed class CoreWebView2DeferralAdapter : IDeferralToken
    {
        private readonly CoreWebView2Deferral _deferral;
        public CoreWebView2DeferralAdapter(CoreWebView2Deferral deferral) => _deferral = deferral;
        public void Complete() => _deferral.Complete();
    }

    public async Task ProcessNewWindowRequestedAsync(
        CoreWebView2Deferral? deferral,
        Action<CoreWebView2?> assignNewWindow,
        string uri,
        Action<string>? updateTabUrl = null)
    {
        IDeferralToken? token = deferral is null ? null : new CoreWebView2DeferralAdapter(deferral);
        await ProcessNewWindowRequestedAsync(token, assignNewWindow, uri, updateTabUrl);
    }

    public async Task ProcessNewWindowRequestedAsync(
        IDeferralToken? deferral,
        Action<CoreWebView2?> assignNewWindow,
        string uri,
        Action<string>? updateTabUrl = null)
    {
        BrowserTab? newTab = null;
        try
        {
            newTab = _createTab("internal://pending-native");

            var readyCore = await _ensureTabReadyAsync(newTab, CancellationToken.None);
            if (readyCore is not null && _isTabAlive(newTab.Id))
            {
                var wv = _getWebView(newTab.Id);
                if (wv is Microsoft.Web.WebView2.Wpf.WebView2 std4) std4.DefaultBackgroundColor = _settings.ForceDarkMode ? WebViewFactory.DarkBackground : System.Drawing.Color.White;
                else if (wv is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl comp4) comp4.DefaultBackgroundColor = _settings.ForceDarkMode ? WebViewFactory.DarkBackground : System.Drawing.Color.White;

                assignNewWindow(readyCore);
                newTab.Url = uri;
                updateTabUrl?.Invoke(uri);
            }
            else if (newTab is not null)
            {
                _removeSentinelTab(newTab);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"NewWindowRequested error: {ex.Message}");
            if (newTab is not null)
            {
                try { _removeSentinelTab(newTab); }
                catch (Exception cleanupEx) { Trace.WriteLine($"Failed to remove sentinel tab: {cleanupEx.Message}"); }
            }
        }
        finally
        {
            deferral?.Complete();
        }
    }

    private void WireNewWindowRequested(CoreWebView2 core, BrowserTab tab, WebViewSubscriptions subscriptions)
    {
        EventHandler<CoreWebView2NewWindowRequestedEventArgs> onNewWindowRequested = (_, e) =>
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
                await ProcessNewWindowRequestedAsync(deferral, coreReady => e.NewWindow = coreReady, e.Uri);
            });
        };
        core.NewWindowRequested += onNewWindowRequested;
        subscriptions.Add(() => core.NewWindowRequested -= onNewWindowRequested);
    }
}