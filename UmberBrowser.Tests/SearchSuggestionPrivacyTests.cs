using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using UmberBrowser.Models;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class SearchSuggestionPrivacyTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("umber-suggestions");

    public void Dispose() => _root.Delete(true);

    private sealed class History : IHistoryStore
    {
        public void Add(string url, string title) { }
        public List<HistoryEntry> Load() => [];
        public List<HistoryEntry> Search(string query) =>
            [new() { Url = "https://example.org/notes", Title = "Private notes" }];
        public bool Clear() => true;
        public List<HistoryEntry> GetFrequentSites(int count = 8) => [];
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldResponse { get; init; }
        public CancellationToken RequestToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            RequestToken = ct;
            Started.TrySetResult();
            // Deliberately ignore cancellation to exercise late response handling.
            if (HoldResponse) await Release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"phrase":"private notes app"}]""")
            };
        }
    }

    [Fact]
    public async Task DefaultsKeepHistoryAndShortcutsWithoutSendingAnything()
    {
        var settings = new BrowserSettings();
        settings.NewTabShortcuts.Add(new() { Name = "Notes", Url = "https://notes.example.org" });
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var provider = new SuggestionProvider(client, settings, new History());

        var results = await provider.GetSuggestionsAsync("notes", CancellationToken.None);

        Assert.False(settings.SearchSuggestionsEnabled);
        Assert.Contains(results, item => item.Kind == SuggestionKind.History);
        Assert.Contains(results, item => item.Kind == SuggestionKind.Shortcut);
        Assert.Empty(handler.Requests);
        settings.SearchSuggestionsEnabled = true;
        settings.ResetToDefaults();
        Assert.False(settings.SearchSuggestionsEnabled);
    }

    [Theory]
    [InlineData("https://example.org/private?token=secret")]
    [InlineData("example.org/private")]
    [InlineData("example.org")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("file:///C:/private/notes.txt")]
    [InlineData("C:\\Private notes\\notes.txt")]
    [InlineData("\\\\server\\private")]
    [InlineData("/home/private notes")]
    [InlineData("person@example.org")]
    [InlineData("internal://settings")]
    [InlineData("read https://example.org/private")]
    [InlineData("   ")]
    public async Task AddressAndPathInputsStayLocalEvenAfterOptIn(string query)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var provider = new SuggestionProvider(client, new() { SearchSuggestionsEnabled = true }, new History());

        Assert.Empty(await provider.GetWebSuggestionsAsync(query, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExplicitOptInSendsSearchTextAndReturnsPhrases()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var provider = new SuggestionProvider(client, new() { SearchSuggestionsEnabled = true }, new History());

        var results = await provider.GetWebSuggestionsAsync("private notes", CancellationToken.None);

        Assert.Equal("https://duckduckgo.com/ac/?q=private%20notes", Assert.Single(handler.Requests).AbsoluteUri);
        Assert.Equal("private notes app", Assert.Single(results).DisplayText);
    }

    [Fact]
    public async Task OptingOutDuringDebouncePreventsTheRequest()
    {
        var settings = new BrowserSettings { SearchSuggestionsEnabled = true };
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var provider = new SuggestionProvider(client, settings, new History());

        var pending = provider.GetWebSuggestionsAsync("notes", CancellationToken.None);
        settings.SearchSuggestionsEnabled = false;

        Assert.Empty(await pending);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OptingOutCancelsAnActiveRequestAndDiscardsItsLateResponse()
    {
        var settings = new BrowserSettings { SearchSuggestionsEnabled = true };
        using var handler = new RecordingHandler { HoldResponse = true };
        using var client = new HttpClient(handler);
        var provider = new SuggestionProvider(client, settings, new History());

        var pending = provider.GetWebSuggestionsAsync("notes", CancellationToken.None);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        settings.SearchSuggestionsEnabled = false;
        var cancelled = handler.RequestToken.IsCancellationRequested;
        handler.Release.SetResult();

        Assert.Empty(await pending);
        Assert.True(cancelled);
        await provider.GetWebSuggestionsAsync("other notes", CancellationToken.None);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"searchSuggestionsEnabled\":true,\"faviconProxyEnabled\":true}")]
    [InlineData("{\"settingsSchemaVersion\":1,\"searchSuggestionsEnabled\":true,\"faviconProxyEnabled\":true}")]
    [InlineData("{\"settingsSchemaVersion\":1,\"searchSuggestionsEnabled\":false}")]
    public void OlderProfilesSwitchOffOnceAndKeepALaterOptIn(string json)
    {
        var file = Path.Combine(_root.FullName, "settings.json");
        File.WriteAllText(file, json);
        var store = new SettingsStore(file);

        var loaded = store.Load();

        Assert.False(loaded.SearchSuggestionsEnabled);
        Assert.Equal(BrowserSettings.CurrentSchemaVersion, loaded.SettingsSchemaVersion);
        using var saved = JsonDocument.Parse(File.ReadAllText(file));
        Assert.False(saved.RootElement.GetProperty("searchSuggestionsEnabled").GetBoolean());
        Assert.Equal(json.Contains("\"settingsSchemaVersion\":1") && json.Contains("\"faviconProxyEnabled\":true"),
            loaded.FaviconProxyEnabled);

        loaded.SearchSuggestionsEnabled = true;
        store.Save(loaded);
        Assert.True(store.Load().SearchSuggestionsEnabled);
        Assert.True(store.Load().SearchSuggestionsEnabled);
    }
}
