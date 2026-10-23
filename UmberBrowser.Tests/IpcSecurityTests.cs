using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using UmberBrowser.Engine;
using UmberBrowser.Models;
using UmberBrowser.Models.LinkPreview;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class IpcSecurityTests
{
    private sealed class FakeDownloadStore : IDownloadStore
    {
        public System.Collections.ObjectModel.ObservableCollection<DownloadItem> Items { get; } = new();
        public void Add(DownloadItem item) => Items.Add(item);
        public void Remove(string id) { }
        public void ClearCompleted() => Items.Clear();
        public void ClearAll() => Items.Clear();
        public DownloadItem? Get(string id) => null;
    }

    private sealed class FakeDownloadSuppressor : Services.LinkPreview.ILinkPreviewDownloadSuppressor
    {
        public void Add(string url) { }
        public bool ShouldSuppress(string url) => false;
    }

    private static (WebViewIpcBridge bridge, List<string> receivedMessages, List<string> tabStateChanges, List<string> linkPreviews) CreateBridge(
        string ipcToken,
        string telemetryToken,
        BrowserSettings settings,
        Func<Guid, bool>? isTabAlive = null)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var downloadStore = new FakeDownloadStore();
        var nativeDownloads = new Dictionary<string, CoreWebView2DownloadOperation>();
        var received = new List<string>();
        var tabStates = new List<string>();
        var linkPreviews = new List<string>();

        var bridge = new WebViewIpcBridge(
            dispatcher,
            settings,
            downloadStore,
            ipcToken,
            telemetryToken,
            nativeDownloads,
            isTabAlive: isTabAlive ?? (_ => true),
            getWebView: _ => null,
            createTab: url => new BrowserTab(url),
            activateAsync: _ => System.Threading.Tasks.Task.CompletedTask,
            closeTab: _ => { },
            ensureTabReadyAsync: (_, _) => System.Threading.Tasks.Task.FromResult<CoreWebView2?>(null),
            removeSentinelTab: _ => { },
            downloadSuppressor: new FakeDownloadSuppressor());

        bridge.WebMessageReceived += msg => received.Add(msg);
        bridge.TabStateChanged += tab => tabStates.Add(tab.ThemeColor);
        bridge.LinkPreviewRequested += (tab, url, rect, trigger) => linkPreviews.Add(url);

        return (bridge, received, tabStates, linkPreviews);
    }

    [Fact]
    public void ExternalOrigin_WithCapturedTelemetryToken_CannotInvokePrivilegedSettings()
    {
        var ipcToken = "trusted-internal-token-12345";
        var telemetryToken = "telemetry-token-67890";
        var settings = new BrowserSettings();
        var (bridge, received, _, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("https://evil.com");
        tab.ActiveInternalDocument = null; // external web page has no internal document

        // Hostile script observed telemetryToken and attempts to turn off ad blocking
        var hostilePayload = telemetryToken + ":setting:adBlock:false";
        var handled = bridge.ProcessIncomingWebMessage(tab, "https://evil.com/page.html", hostilePayload);

        Assert.False(handled);
        Assert.Empty(received);
    }

    [Fact]
    public void ExternalOrigin_WithGuessedIpcToken_CannotInvokePrivilegedSettings()
    {
        var ipcToken = "trusted-internal-token-12345";
        var telemetryToken = "telemetry-token-67890";
        var settings = new BrowserSettings();
        var (bridge, received, _, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("https://evil.com");
        tab.ActiveInternalDocument = null;

        var hostilePayload = ipcToken + ":setting:adBlock:false";
        var handled = bridge.ProcessIncomingWebMessage(tab, "https://evil.com/page.html", hostilePayload);

        Assert.False(handled);
        Assert.Empty(received);
    }

    [Fact]
    public void ExternalOrigin_InAboutBlankIframe_WithoutActiveInternalDocument_CannotInvokePrivilegedSettings()
    {
        var ipcToken = "trusted-internal-token-12345";
        var telemetryToken = "telemetry-token-67890";
        var settings = new BrowserSettings();
        var (bridge, received, _, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("https://evil.com");
        tab.ActiveInternalDocument = null;

        // Even if source is about:blank from an iframe, tab has no active internal document
        var hostilePayload = ipcToken + ":setting:adBlock:false";
        var handled = bridge.ProcessIncomingWebMessage(tab, "about:blank", hostilePayload);

        Assert.False(handled);
        Assert.Empty(received);
    }

    [Fact]
    public void ExternalOrigin_TelemetryMessage_Succeeds()
    {
        var ipcToken = "trusted-internal-token-12345";
        var telemetryToken = "telemetry-token-67890";
        var settings = new BrowserSettings();
        var (bridge, received, tabStates, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("https://example.com");
        tab.ActiveInternalDocument = null;

        var telemetryPayload = "THEME_COLOR:#112233";
        var handled = bridge.ProcessIncomingWebMessage(tab, "https://example.com", telemetryPayload);

        Assert.True(handled);
        Assert.Empty(received); // Telemetry is handled directly and never forwarded to privileged router
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.Equal("#112233", tab.ThemeColor);
    }

    [Fact]
    public void InternalSettingsPage_WithValidIdentityAndToken_CanInvokeSetting()
    {
        var ipcToken = "trusted-internal-token-12345";
        var telemetryToken = "telemetry-token-67890";
        var settings = new BrowserSettings();
        var (bridge, received, _, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("umber://settings");
        tab.ActiveInternalDocument = new InternalDocumentIdentity(tab.Id, TabEngine.InternalPageKind.Settings, ipcToken, 1);

        var validPayload = ipcToken + ":setting:darkMode:true";
        var handled = bridge.ProcessIncomingWebMessage(tab, "about:blank", validPayload);

        Assert.True(handled);
        Assert.Equal(new[] { "setting:darkMode:true" }, received);
    }

    [Fact]
    public void InternalSettingsPage_CannotInvokeHistoryClear()
    {
        var ipcToken = "trusted-internal-token-12345";
        var telemetryToken = "telemetry-token-67890";
        var settings = new BrowserSettings();
        var (bridge, received, _, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("umber://settings");
        tab.ActiveInternalDocument = new InternalDocumentIdentity(tab.Id, TabEngine.InternalPageKind.Settings, ipcToken, 1);

        // Settings page route permissions do not permit history-clear
        var crossRoutePayload = ipcToken + ":history-clear";
        var handled = bridge.ProcessIncomingWebMessage(tab, "about:blank", crossRoutePayload);

        Assert.False(handled);
        Assert.Empty(received);
    }

    [Fact]
    public void InternalHistoryPage_CanInvokeHistoryClear_ButNotSettings()
    {
        var ipcToken = "trusted-internal-token-12345";
        var telemetryToken = "telemetry-token-67890";
        var settings = new BrowserSettings();
        var (bridge, received, _, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("umber://history");
        tab.ActiveInternalDocument = new InternalDocumentIdentity(tab.Id, TabEngine.InternalPageKind.History, ipcToken, 1);

        var historyClearPayload = ipcToken + ":history-clear";
        var handledClear = bridge.ProcessIncomingWebMessage(tab, "about:blank", historyClearPayload);
        Assert.True(handledClear);
        Assert.Equal(new[] { "history-clear" }, received);

        var settingPayload = ipcToken + ":setting:adBlock:false";
        var handledSetting = bridge.ProcessIncomingWebMessage(tab, "about:blank", settingPayload);
        Assert.False(handledSetting);
        Assert.Single(received);
    }

    [Fact]
    public void LocalAsset_AllowedRoute_Succeeds()
    {
        var ipcToken = "trusted-internal-token-12345";
        var telemetryToken = "telemetry-token-67890";
        var settings = new BrowserSettings();
        var (bridge, received, _, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("http://local.assets/TCLens/options/options.html");
        tab.ActiveInternalDocument = null;

        var localPayload = "tclens-get-text:sample text";
        var handled = bridge.ProcessIncomingWebMessage(tab, "http://local.assets/TCLens/options/options.html", localPayload);

        Assert.True(handled);
        Assert.Equal(new[] { "tclens-get-text:sample text" }, received);
    }

    [Fact]
    public void LocalAsset_ForbiddenRoute_IsRejected()
    {
        var ipcToken = "trusted-internal-token-12345";
        var telemetryToken = "telemetry-token-67890";
        var settings = new BrowserSettings();
        var (bridge, received, _, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("http://local.assets/TCLens/options/options.html");
        tab.ActiveInternalDocument = null;

        var hostilePayload = "setting:adBlock:false";
        var handled = bridge.ProcessIncomingWebMessage(tab, "http://local.assets/TCLens/options/options.html", hostilePayload);

        Assert.False(handled);
        Assert.Empty(received);
    }

    [Fact]
    public void TrustedExternalOrigin_DownloadRequest_Succeeds()
    {
        var ipcToken = "trusted-internal-token-12345";
        var telemetryToken = "telemetry-token-67890";
        var settings = new BrowserSettings();
        var (bridge, received, _, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("https://wallhaven.cc/w/123456");
        tab.ActiveInternalDocument = null;

        var payload = "wallhaven-download:https://w.wallhaven.cc/full/123456.jpg";
        var handled = bridge.ProcessIncomingWebMessage(tab, "https://wallhaven.cc/w/123456", payload);

        Assert.True(handled);
        Assert.Equal(new[] { "download-request:https://w.wallhaven.cc/full/123456.jpg" }, received);
    }

    [Fact]
    public void UntrustedExternalOrigin_DownloadRequest_IsRejected()
    {
        var ipcToken = "trusted-internal-token-12345";
        var telemetryToken = "telemetry-token-67890";
        var settings = new BrowserSettings();
        var (bridge, received, _, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("https://evil.com/fake-downloader");
        tab.ActiveInternalDocument = null;

        var payload = telemetryToken + ":download-request:https://evil.com/malware.exe";
        var handled = bridge.ProcessIncomingWebMessage(tab, "https://evil.com/fake-downloader", payload);

        Assert.False(handled);
        Assert.Empty(received);
    }

    [Fact]
    public void TransferredTab_RetainsActiveDocumentIdentity_AndAuthorizesInternalCommands()
    {
        var ipcToken = "process-shared-ipc-token";
        var telemetryToken = "process-shared-telemetry-token";
        var settings = new BrowserSettings();
        var (sourceBridge, _, _, _) = CreateBridge(ipcToken, telemetryToken, settings);
        var (destBridge, destReceived, _, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("umber://settings");
        tab.ActiveInternalDocument = new InternalDocumentIdentity(tab.Id, TabEngine.InternalPageKind.Settings, ipcToken, 1);

        // When tab is transferred from source window to destination window,
        // tab instance is handed over and destBridge receives messages
        var validPayload = ipcToken + ":setting:forceHttps:true";
        var handled = destBridge.ProcessIncomingWebMessage(tab, "about:blank", validPayload);

        Assert.True(handled);
        Assert.Equal(new[] { "setting:forceHttps:true" }, destReceived);
    }

    [Fact]
    public void NavigationAway_ClearsActiveInternalDocument_RejectingSubsequentCommands()
    {
        var ipcToken = "trusted-internal-token-12345";
        var telemetryToken = "telemetry-token-67890";
        var settings = new BrowserSettings();
        var (bridge, received, _, _) = CreateBridge(ipcToken, telemetryToken, settings);

        var tab = new BrowserTab("umber://settings");
        tab.ActiveInternalDocument = new InternalDocumentIdentity(tab.Id, TabEngine.InternalPageKind.Settings, ipcToken, 1);

        // Navigation away clears active internal document
        tab.Url = "https://external-website.com";
        tab.ActiveInternalDocument = null;

        var command = ipcToken + ":setting:darkMode:false";
        var handled = bridge.ProcessIncomingWebMessage(tab, "about:blank", command);

        Assert.False(handled);
        Assert.Empty(received);
    }
}
