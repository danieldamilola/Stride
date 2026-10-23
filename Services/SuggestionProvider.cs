using System.Net.Http;
using System.Text.Json;
using UmberBrowser.Models;

namespace UmberBrowser.Services;

/// <summary>
/// Merges local history and shortcut matches with DuckDuckGo autocomplete results.
/// Debounces web requests by 150ms internally. History leads because a visited
/// page beats a guess, matching standard browser ranking.
/// </summary>
public sealed class SuggestionProvider : ISuggestionProvider
{
    internal const int MaxSuggestions = 8;

    private readonly HttpClient _http;
    private readonly BrowserSettings _settings;
    private readonly IHistoryStore _history;

    public SuggestionProvider(HttpClient http, BrowserSettings settings, IHistoryStore history)
    {
        _http = http;
        _settings = settings;
        _history = history;
    }

    public async Task<List<SuggestionItem>> GetSuggestionsAsync(string query, CancellationToken ct)
    {
        var results = GetLocalSuggestions(query);
        foreach (var item in await GetWebSuggestionsAsync(query, ct))
        {
            if (!results.Any(r => string.Equals(r.DisplayText, item.DisplayText, StringComparison.OrdinalIgnoreCase)))
                results.Add(item);
        }
        return results.Take(MaxSuggestions).ToList();
    }

    public List<SuggestionItem> GetLocalSuggestions(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var results = new List<SuggestionItem>();
        var lowerQuery = query.ToLowerInvariant();

        // Visited pages first, most recent first, shown by title.
        // Rows dedupe by display text so repeat titled visits collapse to one row.
        foreach (var entry in _history.Search(query).AsEnumerable().Reverse())
        {
            if (string.IsNullOrEmpty(entry.Url)) continue;
            var text = string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title;
            if (results.Any(r => string.Equals(r.Url, entry.Url, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(r.DisplayText, text, StringComparison.OrdinalIgnoreCase))) continue;
            results.Add(new SuggestionItem
            {
                DisplayText = text,
                Url = entry.Url,
                Suffix = HostOf(entry.Url),
                Kind = SuggestionKind.History
            });
            if (results.Count >= MaxSuggestions) return results;
        }

        // Then new-tab shortcuts, by name or URL.
        foreach (var shortcut in _settings.NewTabShortcuts)
        {
            if (shortcut.Name.ToLowerInvariant().Contains(lowerQuery) ||
                shortcut.Url.ToLowerInvariant().Contains(lowerQuery))
            {
                if (results.Any(r => string.Equals(r.Url, shortcut.Url, StringComparison.OrdinalIgnoreCase))) continue;
                results.Add(new SuggestionItem
                {
                    DisplayText = shortcut.Name,
                    Url = shortcut.Url,
                    Suffix = HostOf(shortcut.Url),
                    Kind = SuggestionKind.Shortcut
                });
                if (results.Count >= MaxSuggestions) break;
            }
        }

        return results;
    }

    public async Task<List<SuggestionItem>> GetWebSuggestionsAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        // Privacy: network autocomplete can be disabled. When off, nothing returns.
        if (!_settings.SearchSuggestionsEnabled)
            return [];

        // Debounce before hitting the network
        await Task.Delay(150, ct);

        var results = new List<SuggestionItem>();
        try
        {
            var url = "https://duckduckgo.com/ac/?q=" + Uri.EscapeDataString(query);
            var response = await _http.GetStringAsync(url, ct);

            using var doc = JsonDocument.Parse(response);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("phrase", out var phrase))
                {
                    var p = phrase.GetString();
                    if (!string.IsNullOrEmpty(p) && !results.Any(r => string.Equals(r.DisplayText, p, StringComparison.OrdinalIgnoreCase)))
                        results.Add(new SuggestionItem
                        {
                            // The phrase is the name and reads as the whole row. A
                            // "DuckDuckGo Search" label repeated on every row cost
                            // the name so much width it trimmed to a few letters.
                            DisplayText = p,
                            Kind = SuggestionKind.Search
                        });
                }
            }
        }
        catch (OperationCanceledException) { /* expected on rapid typing */ }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"SuggestionProvider error: {ex}");
        }

        return results.Take(MaxSuggestions).ToList();
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";
}
