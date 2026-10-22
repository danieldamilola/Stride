namespace StrideBrowser.Services;

using StrideBrowser.Models;

/// <summary>
/// Fetches address bar suggestions from local history and shortcuts plus a web search API.
/// Handles debouncing, cancellation, and result merging internally.
/// </summary>
public interface ISuggestionProvider
{
    /// <summary>
    /// Returns up to 8 suggestions for the given query.
    /// Merges local history and shortcut matches with web API results.
    /// Safe to call rapidly; internally debounces and cancels stale requests.
    /// </summary>
    Task<List<SuggestionItem>> GetSuggestionsAsync(string query, CancellationToken ct);

    /// <summary>
    /// Returns up to 8 local suggestions synchronously: history matches first,
    /// then shortcut matches. No debounce, no network. Factors history and
    /// shortcuts once so the dropdown can render instantly.
    /// </summary>
    List<SuggestionItem> GetLocalSuggestions(string query);

    /// <summary>
    /// Returns web autocomplete phrases after a short debounce. Honors the
    /// online suggestions opt-in. Address and file path inputs stay local.
    /// </summary>
    Task<List<SuggestionItem>> GetWebSuggestionsAsync(string query, CancellationToken ct);
}
