using System.Text.RegularExpressions;
using UmberBrowser.Models;
using UmberBrowser.Services.Pages;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class SettingsOrganizationTests
{
    [Theory]
    [InlineData("general", "restoreSession")]
    [InlineData("general", "hwAccel")]
    [InlineData("appearance", "zoom")]
    [InlineData("appearance", "sidebarPosition")]
    [InlineData("appearance", "darkMode")]
    [InlineData("appearance", "showExtensionsIcon")]
    [InlineData("search", "search")]
    [InlineData("search", "searchSuggestions")]
    [InlineData("tabs", "blockDupes")]
    [InlineData("tabs", "tabSleep")]
    [InlineData("focus", "unhookEnabled")]
    [InlineData("privacy", "clearOnExit")]
    [InlineData("privacy", "trackingPrevention")]
    [InlineData("system", "autoCheckForUpdates")]
    public void SettingAppearsOnceInItsCategory(string category, string key)
    {
        var html = new SettingsPage().Render(new BrowserSettings(), "test");
        var handler = $"onchange=\"s('{key}', this)\"";

        Assert.Contains(handler, Section(html, category));
        Assert.Single(Regex.Matches(html, Regex.Escape(handler)).Cast<Match>());
    }

    [Theory]
    [InlineData("general", "set-default-browser")]
    [InlineData("general", "reset-settings")]
    [InlineData("general", "open-onboarding")]
    [InlineData("system", "open-releasenotes")]
    public void BrowserActionAppearsInItsCategory(string category, string command)
    {
        var html = new SettingsPage().Render(new BrowserSettings(), "test");
        Assert.Contains($"__T + '{command}'", Section(html, category));
    }

    [Fact]
    public void EveryNavigationTargetHasOneNamedSection()
    {
        var html = new SettingsPage().Render(new BrowserSettings(), "test");
        var targets = Regex.Matches(html, "data-target=\"(section-[^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(8, targets.Length);
        Assert.DoesNotContain("section-extensions", targets);
        Assert.DoesNotContain("section-performance", targets);
        Assert.Equal(targets.Length, targets.Distinct().Count());
        foreach (var target in targets)
        {
            var category = target["section-".Length..];
            Assert.Single(Regex.Matches(html, $"id=\"{target}\"").Cast<Match>());
            Assert.Contains("data-search-title=\"", Section(html, category));
        }
    }

    private static string Section(string html, string category)
    {
        var match = Regex.Match(html, $"<div class=\"section(?: active)?\" id=\"section-{category}\".*?(?=<div class=\"section(?: active)?\"|$)", RegexOptions.Singleline);
        Assert.True(match.Success, $"Missing settings category {category}");
        return match.Value;
    }
}
