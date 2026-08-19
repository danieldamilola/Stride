using System.Collections.Generic;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

public class NavigationPolicyEngineTests
{
    private class FakeSettingsStore : ISettingsStore
    {
        private BrowserSettings _settings;
        public FakeSettingsStore(BrowserSettings settings) => _settings = settings;
        public BrowserSettings Load() => _settings;
        public void Save(BrowserSettings settings) => _settings = settings;
    }

    [Theory]
    [InlineData("http://example.com", "https://example.com")]
    [InlineData("http://example.com/page?query=123#hash", "https://example.com/page?query=123#hash")]
    [InlineData("HTTP://TEST.ORG/SUB", "https://TEST.ORG/SUB")]
    public void ShouldUpgradeToHttps_UpgradesStandardHttpUrls(string inputUrl, string expectedHttpsUrl)
    {
        var result = NavigationPolicyEngine.ShouldUpgradeToHttps(inputUrl, out var upgradedUrl);

        Assert.True(result);
        Assert.Equal(expectedHttpsUrl, upgradedUrl);
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:5000")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://192.168.1.100")]
    [InlineData("http://10.0.0.1:3000")]
    public void ShouldUpgradeToHttps_IgnoresLocalhostAndIpAddresses(string inputUrl)
    {
        var result = NavigationPolicyEngine.ShouldUpgradeToHttps(inputUrl, out var upgradedUrl);

        Assert.False(result);
        Assert.Null(upgradedUrl);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("file:///C:/test.html")]
    [InlineData("internal://settings")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not a uri")]
    public void ShouldUpgradeToHttps_IgnoresNonHttpOrInvalidUrls(string? inputUrl)
    {
        var result = NavigationPolicyEngine.ShouldUpgradeToHttps(inputUrl, out var upgradedUrl);

        Assert.False(result);
        Assert.Null(upgradedUrl);
    }

    [Theory]
    [InlineData("zoommtg://zoom.us/join?action=join", "zoommtg")]
    [InlineData("slack://channel?team=T123&id=C123", "slack")]
    [InlineData("spotify:track:12345", "spotify")]
    [InlineData("mailto:user@example.com", "mailto")]
    [InlineData("steam://rungameid/730", "steam")]
    public void IsCustomProtocol_DetectsExternalProtocols(string uriString, string expectedScheme)
    {
        var result = NavigationPolicyEngine.IsCustomProtocol(uriString, out var scheme);

        Assert.True(result);
        Assert.Equal(expectedScheme, scheme);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("file:///c:/path/to/file.txt")]
    [InlineData("data:text/html,<h1>Hello</h1>")]
    [InlineData("about:blank")]
    [InlineData("edge://settings")]
    [InlineData("chrome://version")]
    [InlineData("stride://home")]
    [InlineData("javascript:alert(1)")]
    [InlineData("extension://xyz123/options.html")]
    [InlineData("chrome-extension://xyz123/options.html")]
    [InlineData("internal://settings")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("just text")]
    public void IsCustomProtocol_IgnoresStandardAndInternalSchemes(string? uriString)
    {
        var result = NavigationPolicyEngine.IsCustomProtocol(uriString, out _);

        Assert.False(result);
    }

    [Fact]
    public void IsBlockedFocusHost_IdentifiesBlockedAndAllowedDomains()
    {
        var settings = new BrowserSettings
        {
            FocusLocked = true,
            FocusDomains = "twitter.com\nreddit.com\nyoutube.com"
        };
        var settingsStore = new FakeSettingsStore(settings);
        var focusService = new FocusBlocklistService(settingsStore);
        var policyEngine = new NavigationPolicyEngine(focusService);

        Assert.True(policyEngine.IsBlockedFocusHost("https://twitter.com/home", out var host1));
        Assert.Equal("twitter.com", host1);

        Assert.True(policyEngine.IsBlockedFocusHost("https://www.reddit.com/r/programming", out var host2));
        Assert.Equal("www.reddit.com", host2);

        Assert.False(policyEngine.IsBlockedFocusHost("https://github.com/microsoft/webview2", out _));
        Assert.False(policyEngine.IsBlockedFocusHost("https://wikipedia.org", out _));
        Assert.False(policyEngine.IsBlockedFocusHost(null, out _));
        Assert.False(policyEngine.IsBlockedFocusHost("not a url", out _));
    }
}
