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

    [Fact]
    public void Resolve_Localhost_UsesHttpScheme()
    {
        Assert.Equal("http://localhost:8080", CreateService().Resolve("localhost:8080"));
    }

    [Fact]
    public void Resolve_LoopbackIp_UsesHttpScheme()
    {
        Assert.Equal("http://127.0.0.1:3000", CreateService().Resolve("127.0.0.1:3000"));
    }

    [Fact]
    public void Resolve_PublicIp_UsesHttpsScheme()
    {
        Assert.Equal("https://8.8.8.8", CreateService().Resolve("8.8.8.8"));
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
}
