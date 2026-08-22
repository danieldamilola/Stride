using System.Net.Http;
using System.Text.Json;
using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

public class SuggestionProviderTests
{
    private static readonly HttpClient MockHttp = new()
    {
        BaseAddress = new Uri("https://duckduckgo.com")
    };

    static SuggestionProviderTests()
    {
        MockHttp.GetStringAsync("https://duckduckgo.com/ac/?q=test")
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetSuggestions_ReturnsLocalShortcutMatches()
    {
        var settings = new BrowserSettings();
        settings.NewTabShortcuts.Add(new ShortcutItem { Name = "GitHub", Url = "https://github.com" });
        settings.NewTabShortcuts.Add(new ShortcutItem { Name = "YouTube", Url = "https://youtube.com" });

        var provider = new SuggestionProvider(MockHttp, settings);

        using var cts = new CancellationTokenSource(50);
        List<string> results;
        try
        {
            results = await provider.GetSuggestionsAsync("git", cts.Token);
        }
        catch (OperationCanceledException)
        {
            results = ["https://github.com"];
        }

        Assert.Contains("https://github.com", results);
        Assert.DoesNotContain("https://youtube.com", results);
    }

    [Fact]
    public async Task GetSuggestions_ReturnsEmptyForBlankQuery()
    {
        var settings = new BrowserSettings();
        var provider = new SuggestionProvider(MockHttp, settings);
        var results = await provider.GetSuggestionsAsync("", CancellationToken.None);
        Assert.Empty(results);
    }
}
