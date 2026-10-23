using Xunit;
using UmberBrowser.Services.CommandLine;
using System.IO;
using System;

namespace UmberBrowser.Tests;

public class CommandLineUrlParserTests
{
    private readonly CommandLineUrlParser _parser = new();

    [Fact]
    public void TryParse_WithHttp_ReturnsTrueAndUrl()
    {
        bool result = _parser.TryParse("http://example.com", out string url);
        Assert.True(result);
        Assert.Equal("http://example.com", url);
    }

    [Fact]
    public void TryParse_WithHttps_ReturnsTrueAndUrl()
    {
        bool result = _parser.TryParse("https://example.com", out string url);
        Assert.True(result);
        Assert.Equal("https://example.com", url);
    }

    [Fact]
    public void TryParse_WithUmberScheme_ReturnsTrueAndUrl()
    {
        bool result = _parser.TryParse("umber://settings", out string url);
        Assert.True(result);
        Assert.Equal("umber://settings", url);
    }

    [Fact]
    public void TryParse_WithInvalidString_ReturnsFalse()
    {
        bool result = _parser.TryParse("just-some-text", out string url);
        Assert.False(result);
        Assert.Empty(url);
    }
}
