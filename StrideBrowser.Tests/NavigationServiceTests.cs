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
    [InlineData("internal://newtab", "Search")]
    public void ToDisplayText_ShowsDomainOrPageName(string url, string expected)
    {
        Assert.Equal(expected, NavigationService.ToDisplayText(url, "Fallback"));
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
}
