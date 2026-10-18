using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

public class NavigationServiceTests
{
    private static NavigationService CreateService(string searchEngine = "DuckDuckGo") =>
        new(new BrowserSettings { SearchEngine = searchEngine });

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?q=1")]
    public void Resolve_AbsoluteWebUrl_PassesThroughUnchanged(string url)
    {
        Assert.Equal(url, CreateService().Resolve(url));
    }

    [Fact]
    public void Resolve_NullInput_ReturnsNewTab()
    {
        Assert.Equal(InternalUrls.NewTab, CreateService().Resolve(null));
    }

    [Fact]
    public void Resolve_BlankInput_ReturnsNewTab()
    {
        Assert.Equal(InternalUrls.NewTab, CreateService().Resolve("   "));
    }

    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("sub.example.co.uk", "https://sub.example.co.uk")]
    public void Resolve_BareDomain_AddsHttpsScheme(string input, string expected)
    {
        Assert.Equal(expected, CreateService().Resolve(input));
    }

    [Theory]
    [InlineData("localhost:8080", "http://localhost:8080")]
    [InlineData("127.0.0.1:3000", "http://127.0.0.1:3000")]
    [InlineData("8.8.8.8", "https://8.8.8.8")]
    public void Resolve_HostAndIpAddresses_UseAppropriateScheme(string input, string expected)
    {
        Assert.Equal(expected, CreateService().Resolve(input));
    }

    [Fact]
    public void Resolve_SearchQuery_BuildsDuckDuckGoUrl()
    {
        Assert.Equal(
            "https://duckduckgo.com/?q=how%20to%20bake%20bread",
            CreateService("DuckDuckGo").Resolve("how to bake bread"));
    }

    [Fact]
    public void Resolve_UnknownSearchEngine_FallsBackToDuckDuckGo()
    {
        Assert.StartsWith("https://duckduckgo.com/?q=", CreateService("NotARealEngine").Resolve("cats"));
    }

    [Fact]
    public void BuildSearchUrl_UsesSelectedEngine()
    {
        Assert.Equal("https://www.bing.com/search?q=cats", CreateService("Bing").BuildSearchUrl("cats"));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123", "youtube.com")]
    [InlineData("https://youtube.com/watch?v=abc123", "youtube.com")]
    [InlineData("https://sub.example.co.uk/path?q=1", "sub.example.co.uk")]
    [InlineData("http://localhost:8080/app", "localhost")]
    public void ToDisplayDomain_StripsSchemePathAndWww(string url, string expected)
    {
        Assert.Equal(expected, NavigationService.ToDisplayDomain(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("internal://newtab")]
    [InlineData("internal://settings")]
    [InlineData("about:blank")]
    public void ToDisplayDomain_BlankAndInternal_ShowsEmpty(string? url)
    {
        Assert.Equal("", NavigationService.ToDisplayDomain(url));
    }

    [Fact]
    public void ToDisplayDomain_NonUrl_ShowsAsIs()
    {
        Assert.Equal("not a url at all", NavigationService.ToDisplayDomain("not a url at all"));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc", "youtube.com")]
    [InlineData("internal://downloads", "Downloads")]
    [InlineData("internal://settings", "Settings")]
    [InlineData("internal://history", "History")]
    [InlineData("internal://newtab", "")]
    public void ToDisplayText_ShowsDomainOrPageName(string url, string expected)
    {
        Assert.Equal(expected, NavigationService.ToDisplayText(url, "Fallback"));
    }

    [Fact]
    public void ToDisplayText_NewTab_IsEmptySoTheAddressHintCanDraw()
    {
        // The new tab has no identity to print. Real text here would be
        // selected on click and would read as a query the user never typed.
        Assert.Equal("", NavigationService.ToDisplayText(InternalUrls.NewTab, "New Tab"));
    }

    [Fact]
    public void ToDisplayText_InternalPagesWithIdentity_KeepTheirFriendlyNames()
    {
        // Emptying the new tab must not empty pages the user navigated to.
        Assert.Equal("Settings", NavigationService.ToDisplayText(InternalUrls.Settings, "Settings"));
        Assert.Equal("Downloads", NavigationService.ToDisplayText(InternalUrls.Downloads, "Downloads"));
    }

    [Theory]
    [InlineData("internal://onboarding", "Welcome to Stride")]
    [InlineData("internal://releasenotes", "What's New in Stride")]
    public void ToDisplayText_UnknownInternal_FallsBackToTitle(string url, string title)
    {
        Assert.Equal(title, NavigationService.ToDisplayText(url, title));
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("data:text/html,hi")]
    public void ToDisplayText_BlankPages_ShowEmpty(string url)
    {
        Assert.Equal("", NavigationService.ToDisplayText(url, "New Tab"));
    }

    [Fact]
    public void ShouldReload_UntouchedDisplay_Reloads()
    {
        Assert.True(NavigationService.ShouldReload("youtube.com", "youtube.com", false, "https://www.youtube.com/watch?v=1"));
    }

    [Fact]
    public void ShouldReload_TypedText_ResolvesFresh()
    {
        Assert.False(NavigationService.ShouldReload("github.com", "youtube.com", true, "https://www.youtube.com/watch?v=1"));
    }

    [Fact]
    public void ShouldReload_BindingSyncedDisplayText_StillResolvesFresh()
    {
        // Two way binding keeps AddressText identical to the box, so equality
        // alone must not trigger a reload once the user has typed.
        Assert.False(NavigationService.ShouldReload("github.com", "github.com", true, "https://www.youtube.com/watch?v=1"));
    }

    [Fact]
    public void ShouldReload_NoCurrentPage_ResolvesFresh()
    {
        Assert.False(NavigationService.ShouldReload("", "", false, ""));
    }

    [Fact]
    public void ShouldRefreshAddressDisplay_MovedSelection_Refreshes()
    {
        Assert.True(NavigationService.ShouldRefreshAddressDisplay(true, true, true));
    }

    [Fact]
    public void ShouldRefreshAddressDisplay_UnfocusedBox_Refreshes()
    {
        Assert.True(NavigationService.ShouldRefreshAddressDisplay(false, false, true));
    }

    [Fact]
    public void ShouldRefreshAddressDisplay_FocusedDisplayText_Refreshes()
    {
        // Display text the user never typed belongs to the old tab. Refreshing
        // stops one tab's domain from becoming another tab's query.
        Assert.True(NavigationService.ShouldRefreshAddressDisplay(false, true, false));
    }

    [Fact]
    public void ShouldRefreshAddressDisplay_FocusedTyping_Preserves()
    {
        Assert.False(NavigationService.ShouldRefreshAddressDisplay(false, true, true));
    }

    [Theory]
    [InlineData("xn--pple-43d.com", "xn--pple-43d.com")]
    [InlineData("\u0430pple.com", "xn--pple-43d.com")]
    [InlineData("b\u00FCcher.example", "xn--bcher-kva.example")]
    [InlineData("xn--bcher-kva.example", "xn--bcher-kva.example")]
    [InlineData("example.com", "example.com")]
    [InlineData("sub.domain.example.com", "sub.domain.example.com")]
    [InlineData("sub.\u0430pple.com", "sub.xn--pple-43d.com")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("[::1]", "[::1]")]
    [InlineData("::1", "[::1]")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void ToAsciiHost_CanonicalizesValidDnsHosts(string input, string expected)
    {
        Assert.Equal(expected, NavigationService.ToAsciiHost(input));
    }

    [Theory]
    [InlineData("https://xn--pple-43d.com/", "xn--pple-43d.com")]
    [InlineData("https://\u0430pple.com/", "xn--pple-43d.com")]
    [InlineData("https://b\u00FCcher.example/", "xn--bcher-kva.example")]
    [InlineData("https://xn--bcher-kva.example/", "xn--bcher-kva.example")]
    [InlineData("https://www.example.com/path", "example.com")]
    [InlineData("https://www.\u0430pple.com/path", "xn--pple-43d.com")]
    [InlineData("https://apple.com.attacker.com/", "apple.com.attacker.com")]
    [InlineData("https://\u0430pple.com.attacker.com/", "xn--pple-43d.com.attacker.com")]
    [InlineData("https://127.0.0.1:8080/test", "127.0.0.1")]
    [InlineData("https://[::1]:8443/test", "[::1]")]
    [InlineData("mailto:user@\u0430pple.com?subject=test", "mailto:user@\u0430pple.com?subject=test")]
    [InlineData("javascript:alert('hello')", "javascript:alert('hello')")]
    [InlineData("data:text/html,hello", "")]
    [InlineData("custom-scheme:opaque-data", "custom-scheme:opaque-data")]
    [InlineData("https://\u200D.example/", "\u200D.example")]
    [InlineData("https://\u0430pple.com./", "xn--pple-43d.com.")]
    [InlineData("about:blank", "")]
    [InlineData("", "")]
    public void ToDisplayDomain_NormalizesIdnAndPreservesExpectedHosts(string url, string expected)
    {
        Assert.Equal(expected, NavigationService.ToDisplayDomain(url));
    }

    [Theory]
    [InlineData("https://\u0430pple.com/path", "https://xn--pple-43d.com/path")]
    [InlineData("https://\u0430pple.com:8443/login?q=1#top", "https://xn--pple-43d.com:8443/login?q=1#top")]
    [InlineData("https://\u0430pple.com:443/a/../b?x=%41#%61", "https://xn--pple-43d.com:443/a/../b?x=%41#%61")]
    [InlineData("https://\u0430pple.com:443?x=1", "https://xn--pple-43d.com:443?x=1")]
    [InlineData("https://\u0430pple.com/a%2fb?q=%41&literal=\u0430pple.com#%61", "https://xn--pple-43d.com/a%2fb?q=%41&literal=\u0430pple.com#%61")]
    [InlineData("custom-scheme://\u0430pple.com:42/a%2fb?q=%41#%61", "custom-scheme://xn--pple-43d.com:42/a%2fb?q=%41#%61")]
    [InlineData("https://b\u00FCcher.example/katalog?id=42", "https://xn--bcher-kva.example/katalog?id=42")]
    [InlineData("https://user:pass@\u0430pple.com:8080/p?target=\u0430pple.com#\u0430pple", "https://user:pass@xn--pple-43d.com:8080/p?target=\u0430pple.com#\u0430pple")]
    [InlineData("https://example.com/path/\u0430pple.com?query=\u0430pple.com", "https://example.com/path/\u0430pple.com?query=\u0430pple.com")]
    [InlineData("https://[::1]:8443/test?q=1", "https://[::1]:8443/test?q=1")]
    [InlineData("https://127.0.0.1:8080/test", "https://127.0.0.1:8080/test")]
    [InlineData("mailto:user@\u0430pple.com?subject=test", "mailto:user@\u0430pple.com?subject=test")]
    [InlineData("https://\u200D.example/", "https://\u200D.example/")]
    [InlineData("https://\u0430pple.com./", "https://xn--pple-43d.com./")]
    [InlineData("custom-scheme:opaque-data", "custom-scheme:opaque-data")]
    [InlineData("not-a-valid-uri", "not-a-valid-uri")]
    [InlineData("", "")]
    public void FormatDisplayUrl_NormalizesHierarchicalDnsHosts(string url, string expected)
    {
        Assert.Equal(expected, NavigationService.FormatDisplayUrl(url));
    }

    [Fact]
    public void ToSafeAsciiHost_HandlesIdnExceptionsAndPreservesSecurityContext()
    {
        var normalUri = new Uri("https://\u0430pple.com/");
        var ipv6Uri = new Uri("https://[::1]:8443/");
        var invalidIdnUri = new Uri("https://\u200D.example/");
        var wwwUri = new Uri("https://www.\u0430pple.com/");

        Assert.Equal("xn--pple-43d.com", NavigationService.ToSafeAsciiHost(normalUri));
        Assert.Equal("[::1]", NavigationService.ToSafeAsciiHost(ipv6Uri));
        Assert.Equal("\u200D.example", NavigationService.ToSafeAsciiHost(invalidIdnUri));
        Assert.Equal("www.xn--pple-43d.com", NavigationService.ToSafeAsciiHost(wwwUri));
    }

    [Fact]
    public void ReloadInvariants_UntouchedRevealedIdnUrlTriggersReload()
    {
        string currentUrl = "https://\u0430pple.com:443/a/../b?x=%41#%61";
        string formattedUrl = NavigationService.FormatDisplayUrl(currentUrl);

        Assert.Equal("https://xn--pple-43d.com:443/a/../b?x=%41#%61", formattedUrl);

        // Untouched revealed address triggers reload of current URL
        bool reloadUntouched = NavigationService.ShouldReload(formattedUrl, formattedUrl, edited: false, currentUrl: currentUrl);
        Assert.True(reloadUntouched);

        // Edited address does not trigger reload
        bool reloadEdited = NavigationService.ShouldReload(formattedUrl + "!", formattedUrl, edited: true, currentUrl: currentUrl);
        Assert.False(reloadEdited);
    }

    [Fact]
    public void Resolve_AcceptsBothUnicodeAndPunycodeNavigableHosts()
    {
        var settings = new BrowserSettings();
        var nav = new NavigationService(settings);

        var resolvedUnicode = nav.Resolve("\u0430pple.com");
        var resolvedPunycode = nav.Resolve("xn--pple-43d.com");

        Assert.Equal("https://\u0430pple.com", resolvedUnicode);
        Assert.Equal("https://xn--pple-43d.com", resolvedPunycode);
    }
}
