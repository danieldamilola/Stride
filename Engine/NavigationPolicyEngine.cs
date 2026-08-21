using System;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Helpers;
using StrideBrowser.Interop;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine;

/// <summary>
/// Encapsulates navigation policies: custom protocol handling, focus mode blocks, and HTTPS upgrades.
/// Exposes pure string-level predicates for unit testing alongside WebView2 event adapters.
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

    /// <summary>
    /// Determines whether the URI uses an external custom protocol requiring OS delegation.
    /// </summary>
    public static bool IsCustomProtocol(string? uriString, out string? scheme)
    {
        scheme = null;
        if (string.IsNullOrWhiteSpace(uriString) || !Uri.TryCreate(uriString, UriKind.Absolute, out var parsedUri))
            return false;

        scheme = parsedUri.Scheme.ToLowerInvariant();
        if (scheme is "http" or "https" or "file" or "data" or
            "about" or "edge" or "chrome" or "stride" or "javascript" or
            "extension" or "chrome-extension" or "internal")
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether the given URI targets a host blocked under active Focus mode.
    /// </summary>
    public bool IsBlockedFocusHost(string? uriString, out string? host)
    {
        host = null;
        if (string.IsNullOrWhiteSpace(uriString) || !Uri.TryCreate(uriString, UriKind.Absolute, out var parsedUri))
            return false;

        host = parsedUri.Host;
        return !string.IsNullOrEmpty(host) && _focusBlocklistService.IsBlocked(host);
    }

    /// <summary>
    /// Determines whether an HTTP URI should be automatically upgraded to HTTPS.
    /// Returns false for localhost, loopback, IPv4, IPv6, and non-HTTP schemes.
    /// </summary>
    public static bool ShouldUpgradeToHttps(string? uriString, out string? httpsUrl)
    {
        httpsUrl = null;
        if (string.IsNullOrWhiteSpace(uriString) || !uriString.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return false;

        var isLocalHostOrIp = false;
        if (Uri.TryCreate(uriString, UriKind.Absolute, out var parsedUri))
        {
            isLocalHostOrIp = parsedUri.IsLoopback ||
                              parsedUri.HostNameType == UriHostNameType.IPv4 ||
                              parsedUri.HostNameType == UriHostNameType.IPv6 ||
                              parsedUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            isLocalHostOrIp = uriString.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
                              uriString.StartsWith("http://127.", StringComparison.OrdinalIgnoreCase);
        }

        if (isLocalHostOrIp)
            return false;

        httpsUrl = "https://" + uriString["http://".Length..];
        return true;
    }

    private bool TryHandleCustomProtocol(
        CoreWebView2NavigationStartingEventArgs e,
        BrowserTab tab,
        Dispatcher dispatcher,
        Action<BrowserTab> closeTab)
    {
        if (!IsCustomProtocol(e.Uri, out var scheme))
            return false;

        e.Cancel = true;
        var uriForCustom = e.Uri!;
        dispatcher.InvokeAsync(() =>
        {
            // SECURITY: confirm before handing off to an external app - unprompted
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
                Process.Start(new ProcessStartInfo(uriForCustom) { UseShellExecute = true });
                if (tab.Url == InternalUrls.NewTab || tab.Url == "about:blank")
                    closeTab(tab);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Custom protocol launch failed: {ex.Message}");
            }
        });
        return true;
    }

    private bool TryHandleFocusLock(
        CoreWebView2NavigationStartingEventArgs e,
        BrowserTab tab,
        Dispatcher dispatcher,
        Action<BrowserTab> navigateToFocus)
    {
        if (!IsBlockedFocusHost(e.Uri, out _))
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
        if (!ShouldUpgradeToHttps(e.Uri, out var httpsUri))
            return false;

        e.Cancel = true;
        dispatcher.InvokeAsync(() =>
        {
            try { wv.CoreWebView2.Navigate(httpsUri); }
            catch (Exception ex) { Trace.WriteLine(ex); }
        });
        return true;
    }
}

