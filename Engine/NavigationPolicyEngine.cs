using System;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Helpers;
using StrideBrowser.Interop;

namespace StrideBrowser.Engine;

/// <summary>
/// Encapsulates navigation policies: custom protocol handling, focus mode blocks, and HTTPS upgrades.
/// Separated from TabEngine to clarify lifecycle vs policy logic.
/// </summary>
public sealed class NavigationPolicyEngine
{
    private readonly FocusBlocklistService _focusBlocklistService;

    public NavigationPolicyEngine(FocusBlocklistService focusBlocklistService)
    {
        _focusBlocklistService = focusBlocklistService;
    }

    /// <summary>
    /// Evaluates all navigation policies. Returns true if the navigation was intercepted and handled.
    /// </summary>
    public bool EvaluateAndHandle(
        CoreWebView2NavigationStartingEventArgs e,
        dynamic wv,
        BrowserTab tab,
        Dispatcher dispatcher,
        Action<BrowserTab> closeTab,
        Action<BrowserTab> navigateToFocus,
        bool isFocusLocked,
        bool forceHttps)
    {
        if (TryHandleCustomProtocol(e, tab, dispatcher, closeTab)) return true;
        if (isFocusLocked && TryHandleFocusLock(e, tab, dispatcher, navigateToFocus)) return true;
        if (forceHttps && TryUpgradeToHttps(e, wv, dispatcher)) return true;
        return false;
    }

    private bool TryHandleCustomProtocol(
        CoreWebView2NavigationStartingEventArgs e, 
        BrowserTab tab, 
        Dispatcher dispatcher,
        Action<BrowserTab> closeTab)
    {
        if (e.Uri is not string uriForCustom || !Uri.TryCreate(uriForCustom, UriKind.Absolute, out var parsedCustomUri))
            return false;

        var scheme = parsedCustomUri.Scheme.ToLowerInvariant();
        if (scheme == "http" || scheme == "https" || scheme == "file" || scheme == "data" ||
            scheme == "about" || scheme == "edge" || scheme == "chrome" || scheme == "stride" || scheme == "javascript" ||
            scheme == "extension" || scheme == "chrome-extension" || scheme == "internal")
            return false;

        e.Cancel = true;
        dispatcher.InvokeAsync(() =>
        {
            // SECURITY: confirm before handing off to an external app — unprompted
            // protocol-handler invocation is a known RCE vector for some installed apps.
            var displayUrl = uriForCustom.Length > 100 ? uriForCustom.Substring(0, 97) + "..." : uriForCustom;
            var dialog = new BaseBrowserDialogWindow
            {
                Owner = System.Windows.Application.Current.MainWindow,
                DialogTitle = "Open External App",
                DialogMessage = $"This page wants to open an external application to handle this link:\n\n{displayUrl}\n\nOnly continue if you trust this site.",
                CancelVisibility = System.Windows.Visibility.Visible,
                OkButtonText = "Open App",
                CancelButtonText = "Cancel"
            };
            dialog.ShowDialog();

            if (!dialog.IsAccepted) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uriForCustom) { UseShellExecute = true });
                if (tab.Url == InternalUrls.NewTab || tab.Url == "about:blank")
                    closeTab(tab);
            }
            catch (Exception ex) { Trace.WriteLine($"Custom protocol launch failed: {ex.Message}"); }
        });
        return true;
    }

    private bool TryHandleFocusLock(
        CoreWebView2NavigationStartingEventArgs e, 
        BrowserTab tab, 
        Dispatcher dispatcher,
        Action<BrowserTab> navigateToFocus)
    {
        if (e.Uri is not string uriStrFocus || !Uri.TryCreate(uriStrFocus, UriKind.Absolute, out var parsedFocusUri))
            return false;

        var host = parsedFocusUri.Host;
        if (string.IsNullOrEmpty(host) || !_focusBlocklistService.IsBlocked(host))
            return false;

        e.Cancel = true;
        dispatcher.InvokeAsync(() =>
        {
            tab.Url = InternalUrls.Focus;
            navigateToFocus(tab);
        });
        return true;
    }

    private bool TryUpgradeToHttps(
        CoreWebView2NavigationStartingEventArgs e, 
        dynamic wv, 
        Dispatcher dispatcher)
    {
        if (e.Uri is not string uriStr || !uriStr.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return false;

        var isLocalHostOrIp = false;
        if (Uri.TryCreate(uriStr, UriKind.Absolute, out var parsedUri))
        {
            isLocalHostOrIp = parsedUri.IsLoopback ||
                              parsedUri.HostNameType == UriHostNameType.IPv4 ||
                              parsedUri.HostNameType == UriHostNameType.IPv6 ||
                              parsedUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            isLocalHostOrIp = uriStr.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
                              uriStr.StartsWith("http://127.", StringComparison.OrdinalIgnoreCase);
        }

        if (isLocalHostOrIp)
            return false;

        e.Cancel = true;
        var httpsUri = "https://" + uriStr["http://".Length..];
        dispatcher.InvokeAsync(() => { try { wv.CoreWebView2.Navigate(httpsUri); } catch (Exception ex) { Trace.WriteLine(ex); } });
        return true;
    }
}
