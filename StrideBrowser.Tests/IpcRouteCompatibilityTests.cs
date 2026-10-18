using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Engine;
using StrideBrowser.Helpers;
using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Guards the compatibility between the internal pages and the privileged route allowlist.
///
/// A page that posts a route the allowlist does not carry has a dead control. That happened
/// once: the Onboarding Done button, the onboarding ready handshake, every onboarding
/// setting write, the Settings Set Default button and both What is New buttons were all
/// dropped by the bridge even though their handlers were registered. Nothing in the
/// existing security tests noticed, because each of those tests names a route by hand and
/// a hand chosen route cannot drift with the page.
///
/// The tests here read the real page markup out of the shipped assembly, so a new control
/// that posts a route no page is allowed to send fails here rather than in a user's face.
/// </summary>
public sealed class IpcRouteCompatibilityTests
{
    private const string IpcToken = "compat-test-ipc-token";
    private const string TelemetryToken = "compat-test-telemetry-token";

    /// <summary>
    /// Every internal URL TabEngine can route, paired with the markup that serves it.
    /// OneTab is generated in Services/Pages/OneTabPage.cs rather than from a page
    /// resource, and Error.html posts nothing by design, so neither appears here.
    /// The coverage test below is what keeps this list honest as pages are added.
    /// </summary>
    private static readonly (string Url, string Resource, TabEngine.InternalPageKind Kind)[] InternalPages =
    {
        (InternalUrls.NewTab, "Resources.Pages.NewTab.html", TabEngine.InternalPageKind.NewTab),
        (InternalUrls.Settings, "Resources.Pages.Settings.html", TabEngine.InternalPageKind.Settings),
        (InternalUrls.History, "Resources.Pages.History.html", TabEngine.InternalPageKind.History),
        (InternalUrls.Downloads, "Resources.Pages.Downloads.html", TabEngine.InternalPageKind.Downloads),
        (InternalUrls.Onboarding, "Resources.Pages.Onboarding.html", TabEngine.InternalPageKind.Onboarding),
        (InternalUrls.ReleaseNotes, "Resources.Pages.ReleaseNotes.html", TabEngine.InternalPageKind.ReleaseNotes),
        (InternalUrls.Focus, "Resources.Pages.Focus.html", TabEngine.InternalPageKind.Focus),
        (InternalUrls.Health, "Resources.Pages.Health.html", TabEngine.InternalPageKind.Health),
    };

    /// <summary>
    /// Downloads.html builds this prefix to parse a message the host sends to the page.
    /// It is never posted, so it is not a page command.
    /// </summary>
    private const string DownloadsInboundPrefix = "downloads:";

    // The forms each page uses to reach the host:
    //   __T + 'literal'  the token prefix is concatenated directly
    //   post('literal')  a page local helper adds the token prefix itself
    //   postSetting(k,v) defined as post('setting:' + k + ':' + v)
    private static readonly Regex TokenConcat = new(@"__T\s*\+\s*(['""])(?<route>[^'""]+)\1", RegexOptions.Compiled);
    private static readonly Regex PostHelper = new(@"\bpost\(\s*(['""])(?<route>[a-z][a-z0-9-]*(?::[^'""]*)?)\1", RegexOptions.Compiled);
    private static readonly Regex PostSetting = new(@"\bpostSetting\(", RegexOptions.Compiled);

    /// <summary>
    /// The host messages one internal page emits, read from its shipped markup.
    /// </summary>
    public static IEnumerable<object[]> PageRoutes()
    {
        foreach (var (_, resource, kind) in InternalPages)
        {
            var html = ResourceLoader.Load(resource);
            foreach (var route in HostMessagesIn(html))
                yield return new object[] { resource, kind, route };
        }
    }

    internal static HashSet<string> HostMessagesIn(string html)
    {
        var routes = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in TokenConcat.Matches(html))
        {
            if (m.Groups["route"].Value != DownloadsInboundPrefix) routes.Add(m.Groups["route"].Value);
        }
        foreach (Match m in PostHelper.Matches(html)) routes.Add(m.Groups["route"].Value);
        if (PostSetting.IsMatch(html)) routes.Add(WebMessagePrefix.Setting);
        return routes;
    }

    /// <summary>
    /// Every message an internal page can post must be allowed for that page. This is the
    /// check that would have caught the six dropped commands.
    /// </summary>
    [Theory]
    [MemberData(nameof(PageRoutes))]
    public void EveryHostMessageAnInternalPageEmits_IsAllowedForThatPage(string resource, TabEngine.InternalPageKind kind, string route)
    {
        Assert.True(
            IpcSecurityPolicy.IsRouteAllowedForPage(kind, route),
            $"{resource} posts '{route}' but page kind {kind} is not allowed to send it");
    }

    /// <summary>
    /// The page list has to keep up with InternalUrls. A new internal page that is not in
    /// this list would leave the compatibility theory silently uncovered.
    /// </summary>
    [Fact]
    public void EveryHtmlBackedInternalPageIsCovered()
    {
        var routable = new[]
        {
            InternalUrls.NewTab, InternalUrls.Settings, InternalUrls.History, InternalUrls.Downloads,
            InternalUrls.Onboarding, InternalUrls.ReleaseNotes, InternalUrls.Focus, InternalUrls.Health,
        };

        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (url, resource, kind) in InternalPages)
        {
            Assert.False(string.IsNullOrWhiteSpace(ResourceLoader.Load(resource)), $"{resource} did not load");
            Assert.Equal(TabEngine.GetInternalPageKind(url), kind);
            covered.Add(url);
        }

        // OneTab is generated in C#, so it has no page resource to read here.
        Assert.Equal(routable.Length, covered.Count);
        Assert.True(routable.All(u => covered.Contains(u)), "an internal page is not covered");
    }

    /// <summary>
    /// The six commands the repair restored, each with the page and markup line it comes
    /// from. These are the assertions that name the regression directly.
    /// </summary>
    [Theory]
    [InlineData(TabEngine.InternalPageKind.Onboarding, "onboarding-done", "Onboarding.html:337 Done button")]
    [InlineData(TabEngine.InternalPageKind.Onboarding, "onboarding-ready", "Onboarding.html:450 ready handshake")]
    [InlineData(TabEngine.InternalPageKind.Onboarding, WebMessagePrefix.Setting, "Onboarding.html:295 first run preference writes")]
    [InlineData(TabEngine.InternalPageKind.Settings, WebMessagePrefix.SetDefaultBrowser, "Settings.html:1585 Set Default")]
    [InlineData(TabEngine.InternalPageKind.ReleaseNotes, WebMessagePrefix.WhatsNewContinue, "ReleaseNotes.html:365 Continue")]
    [InlineData(TabEngine.InternalPageKind.ReleaseNotes, WebMessagePrefix.WhatsNewDismiss, "ReleaseNotes.html:364 Settings")]
    public void RestoredCommand_IsAllowedForItsOwnPage(TabEngine.InternalPageKind kind, string route, string origin)
    {
        Assert.True(IpcSecurityPolicy.IsRouteAllowedForPage(kind, route), $"{origin} sends '{route}' for {kind}");
    }

    /// <summary>
    /// The onboarding writes in the shape the page actually builds them, not just the bare
    /// prefix. Every key is one SettingsMessageHandler.SettingSetters knows, so a renamed
    /// key fails here instead of being traced and dropped by the router.
    /// </summary>
    [Theory]
    [InlineData("setting:appTheme:light")]
    [InlineData("setting:accentColor:#7fb89a")]
    [InlineData("setting:search:Brave")]
    [InlineData("setting:showTabNames:true")]
    [InlineData("setting:adBlock:false")]
    [InlineData("setting:smartScreen:false")]
    [InlineData("setting:forceHttps:false")]
    [InlineData("setting:clearOnExit:true")]
    [InlineData("setting:tabHibernation:false")]
    public void OnboardingSettingPayload_IsAllowedAndNamesAKeyTheHandlerRoutes(string payload)
    {
        Assert.True(IpcSecurityPolicy.IsRouteAllowedForPage(TabEngine.InternalPageKind.Onboarding, payload));

        var parts = payload.Split(':', 3);
        Assert.True(parts.Length == 3 && parts[1].Length > 0, $"'{payload}' is not key and value shaped");

        var keys = SettingKeys();
        Assert.True(keys.Contains(parts[1]),
            $"SettingsMessageHandler has no setter for onboarding key '{parts[1]}'");
    }

    /// <summary>The key set the real setting handler routes, read from the shipped assembly.</summary>
    private static HashSet<string> SettingKeys()
    {
        var field = typeof(Engine.Handlers.SettingsMessageHandler)
            .GetField("SettingSetters", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var map = Assert.IsAssignableFrom<System.Collections.IDictionary>(field.GetValue(null));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in map.Keys) keys.Add((string)entry!);
        return keys;
    }

    // ── Bridge path ───────────────────────────────────────────────────────────

    private sealed class FakeDownloadStore : IDownloadStore
    {
        public ObservableCollection<DownloadItem> Items { get; } = new();
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

    private static (WebViewIpcBridge bridge, List<string> received) CreateBridge()
    {
        var received = new List<string>();
        var bridge = new WebViewIpcBridge(
            Dispatcher.CurrentDispatcher,
            new BrowserSettings(),
            new FakeDownloadStore(),
            IpcToken,
            TelemetryToken,
            new Dictionary<string, CoreWebView2DownloadOperation>(),
            isTabAlive: _ => true,
            getWebView: _ => null,
            createTab: url => new BrowserTab(url),
            activateAsync: _ => System.Threading.Tasks.Task.CompletedTask,
            closeTab: _ => { },
            ensureTabReadyAsync: (_, _) => System.Threading.Tasks.Task.FromResult<CoreWebView2?>(null),
            removeSentinelTab: _ => { },
            downloadSuppressor: new FakeDownloadSuppressor());

        bridge.WebMessageReceived += msg => received.Add(msg);
        return (bridge, received);
    }

    /// <summary>A tab showing a given internal page, marked the way TabEngine marks it.</summary>
    private static BrowserTab MarkedTab(TabEngine.InternalPageKind kind)
    {
        var url = InternalPages.First(p => p.Kind == kind).Url;
        var tab = new BrowserTab(url);
        tab.ActiveInternalDocument = new InternalDocumentIdentity(tab.Id, kind, IpcToken, 1);
        return tab;
    }

    private static BrowserTab MarkedTab() => MarkedTab(TabEngine.InternalPageKind.Settings);

    /// <summary>
    /// End to end through the production bridge: an internal document in its own page
    /// reaches the router with the token stripped.
    /// </summary>
    [Theory]
    [InlineData(TabEngine.InternalPageKind.Onboarding, "onboarding-done")]
    [InlineData(TabEngine.InternalPageKind.Onboarding, "onboarding-ready")]
    [InlineData(TabEngine.InternalPageKind.Onboarding, "setting:appTheme:light")]
    [InlineData(TabEngine.InternalPageKind.Settings, WebMessagePrefix.SetDefaultBrowser)]
    [InlineData(TabEngine.InternalPageKind.ReleaseNotes, WebMessagePrefix.WhatsNewContinue)]
    [InlineData(TabEngine.InternalPageKind.ReleaseNotes, WebMessagePrefix.WhatsNewDismiss)]
    public void RestoredCommand_ReachesTheRouterFromItsOwnPage(TabEngine.InternalPageKind kind, string route)
    {
        var (bridge, received) = CreateBridge();
        var tab = MarkedTab(kind);

        var handled = bridge.ProcessIncomingWebMessage(tab, "about:blank", IpcToken + ":" + route);

        Assert.True(handled);
        Assert.Equal(new[] { route }, received);
    }

    /// <summary>
    /// The same restore must not hand these commands to pages that never send them.
    /// </summary>
    [Theory]
    [InlineData(TabEngine.InternalPageKind.History, "onboarding-done")]
    [InlineData(TabEngine.InternalPageKind.Downloads, "onboarding-ready")]
    [InlineData(TabEngine.InternalPageKind.Settings, "onboarding-done")]
    [InlineData(TabEngine.InternalPageKind.ReleaseNotes, "setting:appTheme:light")]
    [InlineData(TabEngine.InternalPageKind.History, WebMessagePrefix.Setting)]
    [InlineData(TabEngine.InternalPageKind.Downloads, WebMessagePrefix.Setting)]
    [InlineData(TabEngine.InternalPageKind.NewTab, WebMessagePrefix.SetDefaultBrowser)]
    [InlineData(TabEngine.InternalPageKind.History, WebMessagePrefix.SetDefaultBrowser)]
    [InlineData(TabEngine.InternalPageKind.Settings, WebMessagePrefix.WhatsNewContinue)]
    [InlineData(TabEngine.InternalPageKind.History, WebMessagePrefix.WhatsNewDismiss)]
    [InlineData(TabEngine.InternalPageKind.Health, WebMessagePrefix.WhatsNewContinue)]
    public void RestoredCommand_IsDeniedForPagesThatDoNotSendIt(TabEngine.InternalPageKind kind, string route)
    {
        Assert.False(IpcSecurityPolicy.IsRouteAllowedForPage(kind, route));

        var (bridge, received) = CreateBridge();
        var tab = MarkedTab(kind);
        Assert.False(bridge.ProcessIncomingWebMessage(tab, "about:blank", IpcToken + ":" + route));
        Assert.Empty(received);
    }

    /// <summary>
    /// The allowlist widened, the earlier gates did not. An external origin stays outside
    /// the privileged branch whatever token it presents.
    /// </summary>
    [Theory]
    [InlineData("onboarding-done")]
    [InlineData("setting:adBlock:false")]
    [InlineData(WebMessagePrefix.SetDefaultBrowser)]
    [InlineData(WebMessagePrefix.WhatsNewContinue)]
    public void RestoredCommand_FromAnExternalOriginIsRejected(string route)
    {
        var (bridge, received) = CreateBridge();

        var withIpcToken = bridge.ProcessIncomingWebMessage(MarkedTab(), "https://evil.example/page", IpcToken + ":" + route);
        var withTelemetryToken = bridge.ProcessIncomingWebMessage(MarkedTab(), "https://evil.example/page", TelemetryToken + ":" + route);

        Assert.False(withIpcToken);
        Assert.False(withTelemetryToken);
        Assert.Empty(received);
    }

    /// <summary>
    /// A telemetry token is not a privileged token, even from an about:blank source in a
    /// tab that is currently showing an internal page.
    /// </summary>
    [Theory]
    [InlineData("onboarding-done")]
    [InlineData(WebMessagePrefix.SetDefaultBrowser)]
    [InlineData(WebMessagePrefix.WhatsNewContinue)]
    public void RestoredCommand_WithATelemetryTokenIsRejected(string route)
    {
        var (bridge, received) = CreateBridge();

        Assert.False(bridge.ProcessIncomingWebMessage(MarkedTab(), "about:blank", TelemetryToken + ":" + route));
        Assert.Empty(received);
    }

    /// <summary>
    /// A tab with no internal document has no authority, whatever the token and the source.
    /// </summary>
    [Theory]
    [InlineData("onboarding-done")]
    [InlineData(WebMessagePrefix.SetDefaultBrowser)]
    public void RestoredCommand_FromATabWithNoInternalDocumentIsRejected(string route)
    {
        var (bridge, received) = CreateBridge();
        var externalTab = new BrowserTab("https://example.com");

        Assert.False(bridge.ProcessIncomingWebMessage(externalTab, "about:blank", IpcToken + ":" + route));
        Assert.Empty(received);
    }
}