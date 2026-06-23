using SpurBrowser.Models;
using System.Net;

namespace SpurBrowser.Services;

/// <summary>Resolves raw user input into navigable URLs.</summary>
public sealed class NavigationService
{
    private readonly BrowserSettings _settings;

    private static readonly Dictionary<string, string> SearchEngines = new()
    {
        ["Google"] = "https://www.google.com/search?q={0}",
        ["DuckDuckGo"] = "https://duckduckgo.com/?q={0}",
        ["Brave"] = "https://search.brave.com/search?q={0}",
        ["Bing"] = "https://www.bing.com/search?q={0}",
        ["Yahoo"] = "https://search.yahoo.com/search?p={0}",
        ["Startpage"] = "https://www.startpage.com/do/search?q={0}",
    };

    public NavigationService(BrowserSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Converts user input (URL, domain, or search query) into a final URL.</summary>
    public string Resolve(string? input)
    {
        if (input is null) return InternalUrls.NewTab;

        var text = input.Trim();
        if (string.IsNullOrEmpty(text)) return InternalUrls.NewTab;

        if (IsAbsoluteWebUrl(text)) return text;
        if (LooksLikeNavigableHost(text)) return DefaultSchemeFor(text) + text;
        return BuildSearchUrl(text);
    }

    /// <summary>Builds a search URL for the given query using the active engine.</summary>
    public string BuildSearchUrl(string query)
    {
        var template = SearchEngines.GetValueOrDefault(_settings.SearchEngine, SearchEngines["DuckDuckGo"]);
        return string.Format(template, Uri.EscapeDataString(query));
    }

    /// <summary>Returns the names of all supported search engines.</summary>
    public static IReadOnlyList<string> AvailableSearchEngines => SearchEngines.Keys.ToList();

    private static bool IsAbsoluteWebUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https";

    private static bool LooksLikeNavigableHost(string text)
    {
        if (text.Contains(' '))
            return false;

        var host = text.Split(['/', ':'], 2)[0];
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IPAddress.TryParse(host, out _))
            return true;

        return host.Contains('.')
            && !double.TryParse(host, out _)
            && host.Split('.').Last().Length >= 2;
    }

    private static string DefaultSchemeFor(string text) =>
        text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith("127.", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith("[::1]", StringComparison.OrdinalIgnoreCase)
            ? "http://"
            : "https://";
}
