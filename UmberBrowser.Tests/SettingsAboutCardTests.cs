using System;
using UmberBrowser.Models;
using UmberBrowser.Services.Pages;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Renders the real Settings page and checks the About card, its embedded icon and the
/// bindings it shares with the update controls and the extension toggles.
/// </summary>
public sealed class SettingsAboutCardTests
{
    private static string Render(BrowserSettings settings) =>
        new SettingsPage().Render(settings, "test-token");

    [Fact]
    public void AboutCard_RendersIdentityRuntimeAndCredits()
    {
        var html = Render(new BrowserSettings { AppTheme = AppThemeMode.Dark });

        Assert.Contains("id=\"section-system\"", html);
        Assert.Contains("about-identity", html);
        Assert.Contains("about-logo", html);
        Assert.Contains("WebView2 SDK", html);
        Assert.Contains("Copyright 2026 The Umber Authors", html);
        Assert.Contains("THIRD-PARTY-NOTICES.md", html);
    }

    [Fact]
    public void AboutCard_EmbedsTheAppIconAsADataUri()
    {
        var html = Render(new BrowserSettings());

        // A missing embedded PNG would leave an empty src and fail this.
        Assert.Contains("src=\"data:image/png;base64,", html);
    }

    [Fact]
    public void AboutCard_LeavesNoUnresolvedTokens()
    {
        var html = Render(new BrowserSettings());

        Assert.DoesNotContain("{{", html);
        Assert.DoesNotContain("}}", html);
    }

    [Fact]
    public void AboutCard_DoesNotClaimUpToDateOrOfficialBuild()
    {
        var html = Render(new BrowserSettings());

        // The update script may set "up to date" only after a real server reply. The About
        // card itself must not state it before any check ran, and must not claim a release build.
        var start = html.IndexOf("id=\"section-system\"", StringComparison.Ordinal);
        var end = html.IndexOf("id=\"updateStatus\"", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "could not locate the About card region");
        var card = html[start..end];

        Assert.DoesNotContain("Umber is up to date", card);
        Assert.DoesNotContain("Official Build", card);
    }

    [Fact]
    public void AboutCard_SingleDownloadUrlDeclarationAndLiveUpdateControls()
    {
        var html = Render(new BrowserSettings());

        Assert.Equal(1, CountOccurrences(html, "let currentDownloadUrl"));
        Assert.Contains("id=\"updateStatus\"", html);
        Assert.Contains("id=\"checkUpdateBtn\"", html);
    }

    [Theory]
    [InlineData(AppThemeMode.Light, "data-theme=\"light\"")]
    [InlineData(AppThemeMode.Dark, "data-theme=\"dark\"")]
    public void Settings_RendersThemeAttribute(AppThemeMode theme, string expected)
    {
        Assert.Contains(expected, Render(new BrowserSettings { AppTheme = theme }));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Settings_RendersSearchSuggestionsAndFaviconProxySwitches(bool searchSuggestions, bool faviconProxy)
    {
        var html = Render(new BrowserSettings
        {
            SearchSuggestionsEnabled = searchSuggestions,
            FaviconProxyEnabled = faviconProxy
        });

        Assert.Contains("onchange=\"s('searchSuggestions', this)\"", html);
        Assert.Contains("onchange=\"s('faviconProxy', this)\"", html);
        Assert.Equal(searchSuggestions ? 1 : 0,
            CountOccurrences(html, "checked onchange=\"s('searchSuggestions', this)\""));
        Assert.Equal(faviconProxy ? 1 : 0,
            CountOccurrences(html, "checked onchange=\"s('faviconProxy', this)\""));
    }

    [Fact]
    public void Settings_ExplainsUnavailablePlaybackControlsAndKeepsUnhook()
    {
        var html = Render(new BrowserSettings());
        Assert.Contains("YouTube playback controls are unavailable", html);
        Assert.DoesNotContain("onchange=\"s('ytEnhancerEnabled', this)\"", html);
        Assert.Contains("onchange=\"s('unhookEnabled', this)\"", html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Settings_KeepsAdBlockAndUnhookBindings(bool adBlockEnabled)
    {
        var html = Render(new BrowserSettings { AdBlockEnabled = adBlockEnabled });

        Assert.Contains("onchange=\"s('adBlock', this)\"", html);
        Assert.Contains("onchange=\"s('unhookEnabled', this)\"", html);
        Assert.Equal(adBlockEnabled ? 1 : 0,
            CountOccurrences(html, "checked onchange=\"s('adBlock', this)\""));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
