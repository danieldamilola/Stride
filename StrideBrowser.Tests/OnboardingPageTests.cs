using System.Text.Json;
using System.Text.RegularExpressions;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Pages;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class OnboardingPageTests
{
    [Fact]
    public void RenderStartsFromActualSettingsIncludingNonDefaultChoices()
    {
        var settings = new BrowserSettings
        {
            AppTheme = AppThemeMode.Light, SearchEngine = "Brave",
            AdBlockEnabled = false, ForceHttps = false, SmartScreenEnabled = false,
            TabSleepEnabled = false, TabHibernationEnabled = false, ClearDataOnExit = true, ShowTabNames = true
        };
        var html = new OnboardingPage().Render(settings, "test");
        var payload = Regex.Match(html, "<script type=\"application/json\" id=\"onboardingData\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        Assert.Equal("light", root.GetProperty("theme").GetString());
        Assert.False(root.TryGetProperty("accent", out _));
        Assert.DoesNotContain("data-setting=\"accentColor\"", html);
        Assert.Equal("brave", root.GetProperty("engine").GetString());
        Assert.False(root.GetProperty("httpsForce").GetBoolean());
        Assert.False(root.GetProperty("adBlock").GetBoolean());
        Assert.False(root.GetProperty("smartScreen").GetBoolean());
        Assert.False(root.GetProperty("hibernate").GetBoolean());
        Assert.False(root.GetProperty("tabSleep").GetBoolean());
        Assert.True(root.GetProperty("clearOnExit").GetBoolean());
        Assert.True(root.GetProperty("showTabNames").GetBoolean());
        Assert.DoesNotContain("{{ONBOARDING_DATA}}", html);
        Assert.DoesNotContain("{{APP_ICON}}", html);
    }

    [Theory]
    [InlineData("DuckDuckGo", "ddg")]
    [InlineData("Brave", "brave")]
    [InlineData("Startpage", "start")]
    [InlineData("Google", "google")]
    [InlineData("Bing", "bing")]
    public void SharedSnapshotMapsSearchEngineChoices(string setting, string choice)
    {
        using var json = JsonDocument.Parse(OnboardingPage.SerializeSettings(new BrowserSettings { SearchEngine = setting }));
        Assert.Equal(choice, json.RootElement.GetProperty("engine").GetString());
    }

    [Fact]
    public void SetupCheckboxesUseValidPersistedSettingsAndMatchTheSnapshot()
    {
        var settings = new BrowserSettings();
        using var json = JsonDocument.Parse(OnboardingPage.SerializeSettings(settings));
        var html = new OnboardingPage().Render(settings, "test");
        var inputs = Regex.Matches(html, "<input[^>]*type=\"checkbox\"[^>]*data-field=\"([^\"]+)\"[^>]*data-setting=\"([^\"]+)\"[^>]*>");
        Assert.Equal(6, inputs.Count);
        foreach (Match input in inputs)
        {
            Assert.True(json.RootElement.TryGetProperty(input.Groups[1].Value, out var value));
            Assert.True(value.ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.True(SettingsChangePolicy.TryApply(settings, input.Groups[2].Value, "true", out var error), error);
            Assert.True(SettingsChangePolicy.TryApply(settings, input.Groups[2].Value, "false", out error), error);
        }
    }
}
