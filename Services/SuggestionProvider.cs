using System.Net.Http;
using System.Text.Json;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// Merges local shortcut matches with DuckDuckGo autocomplete results.
/// Debounces web requests by 150ms internally.
/// </summary>
public sealed class SuggestionProvider : ISuggestionProvider
{
    private readonly HttpClient _http;
    private readonly BrowserSettings _settings;

    public SuggestionProvider(HttpClient http, BrowserSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<List<string>> GetSuggestionsAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var results = new List<string>();
        var lowerQuery = query.ToLowerInvariant();

        // Local shortcuts first (instant, no network)
        foreach (var shortcut in _settings.NewTabShortcuts)
        {
            if (shortcut.Name.ToLowerInvariant().Contains(lowerQuery) ||
                shortcut.Url.ToLowerInvariant().Contains(lowerQuery))
            {
                results.Add(shortcut.Url);
            }
        }

        // Debounce before hitting the network
        await Task.Delay(150, ct);

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
                    if (!string.IsNullOrEmpty(p) && !results.Contains(p))
                        results.Add(p);
                }
            }
        }
        catch (OperationCanceledException) { /* expected on rapid typing */ }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"SuggestionProvider error: {ex}");
        }

        return results.Take(8).ToList();
    }
}
