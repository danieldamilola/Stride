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
        if (!_settings.SearchSuggestionsEnabled || !IsSearchText(query))
            return [];

        using var pending = CancellationTokenSource.CreateLinkedTokenSource(ct);
        void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(BrowserSettings.SearchSuggestionsEnabled) && !_settings.SearchSuggestionsEnabled)
                pending.Cancel();
        }
        _settings.PropertyChanged += OnSettingsChanged;

        var results = new List<SuggestionItem>();
        try
        {
            await Task.Delay(150, pending.Token);
            if (!_settings.SearchSuggestionsEnabled) return [];

            var url = "https://duckduckgo.com/ac/?q=" + Uri.EscapeDataString(query);
            var response = await _http.GetStringAsync(url, pending.Token);
            if (pending.IsCancellationRequested || !_settings.SearchSuggestionsEnabled) return [];

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
            // Exceptions from HTTP requests can contain the query URL.
            System.Diagnostics.Trace.WriteLine($"SuggestionProvider error: {ex.GetType().Name}");
        }
        finally
        {
            _settings.PropertyChanged -= OnSettingsChanged;
        }

        return results.Take(MaxSuggestions).ToList();
    }

    private static bool IsSearchText(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var text = query.Trim();
        // Keep schemes, paths, email addresses and credentials on the machine.
        // This is deliberately conservative, including for searches containing a URL.
        if (text.IndexOfAny([':', '/', '\\', '@']) >= 0) return false;
        if (Uri.TryCreate(text, UriKind.Absolute, out _)) return false;
        if (!text.Any(char.IsWhiteSpace) &&
            (text.Contains('.') || text.StartsWith('[') ||
             string.Equals(text, "localhost", StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";
}
