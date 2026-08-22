namespace StrideBrowser.Services;

/// <summary>
/// Fetches address bar suggestions from local shortcuts and a web search API.
/// Handles debouncing, cancellation, and result merging internally.
/// </summary>
public interface ISuggestionProvider
{
    /// <summary>
    /// Returns up to 8 suggestions for the given query.
    /// Merges local shortcut matches with web API results.
    /// Safe to call rapidly; internally debounces and cancels stale requests.
    /// </summary>
    Task<List<string>> GetSuggestionsAsync(string query, CancellationToken ct);
}
