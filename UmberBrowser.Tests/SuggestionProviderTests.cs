using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using UmberBrowser.Models;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

public class SuggestionProviderTests
{
    private static readonly HttpClient MockHttp = new(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("[]")
    }));

    private sealed class StubHistoryStore : IHistoryStore
    {
        private readonly List<HistoryEntry> _entries;
        public StubHistoryStore(List<HistoryEntry> entries) => _entries = entries;
        public void Add(string url, string title) => _entries.Add(new HistoryEntry { Url = url, Title = title, VisitedAt = DateTime.UtcNow });
        public List<HistoryEntry> Load() => _entries.ToList();
        public List<HistoryEntry> Search(string query) =>
            _entries.Where(e => (e.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                (e.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        public bool Clear() { _entries.Clear(); return true; }
        public List<HistoryEntry> GetFrequentSites(int count = 8) => _entries.Take(count).ToList();
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(response);
    }

    private static SuggestionProvider CreateProvider(BrowserSettings settings, List<HistoryEntry>? history = null) =>
        new(MockHttp, settings, new StubHistoryStore(history ?? []));

    [Fact]
    public async Task GetSuggestions_ReturnsLocalShortcutMatches()
    {
        var settings = new BrowserSettings();
        settings.NewTabShortcuts.Add(new ShortcutItem { Name = "GitHub", Url = "https://github.com" });
        settings.NewTabShortcuts.Add(new ShortcutItem { Name = "YouTube", Url = "https://youtube.com" });

        var provider = CreateProvider(settings);

        var results = await provider.GetSuggestionsAsync("git", CancellationToken.None);

        Assert.Contains(results, r => r.Url == "https://github.com");
        Assert.DoesNotContain(results, r => r.Url == "https://youtube.com");
    }

    [Fact]
    public async Task GetSuggestions_ReturnsEmptyForBlankQuery()
    {
        var settings = new BrowserSettings();
        var provider = CreateProvider(settings);
        var results = await provider.GetSuggestionsAsync("", CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public void GetLocalSuggestions_HistoryLeadsShortcuts()
    {
        var settings = new BrowserSettings();
        settings.NewTabShortcuts.Add(new ShortcutItem { Name = "GitHub Trending", Url = "https://github.com/trending" });
        var history = new List<HistoryEntry>
        {
            new() { Url = "https://github.com/danieldamilola/Umber", Title = "Umber", VisitedAt = DateTime.UtcNow }
        };
        var provider = CreateProvider(settings, history);

        var results = provider.GetLocalSuggestions("github");

        Assert.Equal(2, results.Count);
        Assert.Equal(SuggestionKind.History, results[0].Kind);
        Assert.Equal("Umber", results[0].DisplayText);
        Assert.Equal("https://github.com/danieldamilola/Umber", results[0].NavigateTarget);
        Assert.Equal("github.com", results[0].Suffix);
        Assert.Equal(SuggestionKind.Shortcut, results[1].Kind);
        Assert.Equal("https://github.com/trending", results[1].NavigateTarget);
    }

    [Fact]
    public async Task GetWebSuggestions_PhraseLeadsWithoutARepeatedSourceLabel()
    {
        // Every phrase row used to carry "DuckDuckGo Search" as its second text.
        // Repeated down the list it took enough width to trim the phrase itself
        // down to a few letters, so the row stopped showing what it suggested.
        var settings = new BrowserSettings { SearchSuggestionsEnabled = true };
        var body = """[{"phrase":"chrome extensions"},{"phrase":"chris plattey"}]""";
        var client = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }))
        { BaseAddress = new Uri("https://duckduckgo.com") };

        var provider = new SuggestionProvider(client, settings, new StubHistoryStore([]));

        var results = await provider.GetWebSuggestionsAsync("ch", CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(string.Empty, r.Suffix));
        Assert.Contains(results, r => r.DisplayText == "chrome extensions");
    }

    [Fact]
    public void GetLocalSuggestions_DedupesHistoryAndShortcutUrls()
    {
        var settings = new BrowserSettings();
        settings.NewTabShortcuts.Add(new ShortcutItem { Name = "GitHub", Url = "https://github.com" });
        var history = new List<HistoryEntry>
        {
            new() { Url = "https://github.com", Title = "GitHub", VisitedAt = DateTime.UtcNow }
        };
        var provider = CreateProvider(settings, history);

        var results = provider.GetLocalSuggestions("git");

        Assert.Single(results);
    }

    [Fact]
    public void GetLocalSuggestions_FallsBackToUrlWhenTitleMissing()
    {
        var settings = new BrowserSettings();
        var history = new List<HistoryEntry>
        {
            new() { Url = "https://github.com", Title = "", VisitedAt = DateTime.UtcNow }
        };
        var provider = CreateProvider(settings, history);

        var results = provider.GetLocalSuggestions("git");

        Assert.Single(results);
        Assert.Equal("https://github.com", results[0].DisplayText);
    }

    [Fact]
    public async Task GetWebSuggestions_ToggleOff_ReturnsEmptyWithoutNetwork()
    {
        var settings = new BrowserSettings { SearchSuggestionsEnabled = false };
        var provider = CreateProvider(settings);

        var results = await provider.GetWebSuggestionsAsync("git", CancellationToken.None);

        Assert.Empty(results);
    }
}
