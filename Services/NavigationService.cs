using StrideBrowser.Models;
using System.Net;

namespace StrideBrowser.Services;

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

    /// <summary>
    /// True when Enter should reload the current page: the user typed nothing
    /// new and the box still shows the recorded display text.
    /// </summary>
    public static bool ShouldReload(string typed, string lastDisplayed, bool edited, string currentUrl) =>
        !edited && !string.IsNullOrEmpty(currentUrl) &&
        string.Equals(typed?.Trim() ?? "", lastDisplayed?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Full address bar text: the bare domain for web pages, the friendly
    /// page name for internal pages, empty for blanks. Falls back to the tab
    /// title for internal pages without a mapped name.
    /// </summary>
    public static string ToDisplayText(string? url, string fallbackTitle = "")
    {
        if (string.IsNullOrWhiteSpace(url)) return fallbackTitle;
        if (InternalUrls.IsDataOrBlank(url)) return "";
        if (InternalUrls.IsInternal(url)) return FriendlyInternalName(url, fallbackTitle);
        return ToDisplayDomain(url);
    }

    private static string FriendlyInternalName(string url, string fallbackTitle) => url switch
    {
        InternalUrls.NewTab => "Search",
        InternalUrls.Settings => "Settings",
        InternalUrls.OneTab => "OneTab",
        InternalUrls.History => "History",
        InternalUrls.Downloads => "Downloads",
        InternalUrls.Focus => "Focus Locked",
        InternalUrls.Health => "Health",
        _ => fallbackTitle
    };

    /// <summary>
    /// Shortens a URL for address bar display: bare domain without a leading
    /// www, like standard browsers. Internal pages and blanks show empty.
    /// Anything without a usable host shows as-is.
    /// </summary>
    public static string ToDisplayDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || InternalUrls.IsInternal(url)) return "";
        var text = url.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return text;
        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        return string.IsNullOrEmpty(host) ? text : host;
    }

    /// <summary>Returns the names of all supported search engines.</summary>
    public static IReadOnlyList<string> AvailableSearchEngines => SearchEngines.Keys.ToList();

    private static bool IsAbsoluteWebUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
        (uri.Scheme is "http" or "https" || uri.Scheme == "internal" || uri.Scheme == "data");

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
