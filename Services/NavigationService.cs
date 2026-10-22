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
    /// True when a tab event should refresh the address display. A moved
    /// selection always refreshes. A focused box keeps the user's typed input
    /// and skips, but display text that the user never typed is safe to
    /// replace: keeping it would present one tab's domain as another tab's
    /// query and pop suggestions the user never asked for.
    /// </summary>
    public static bool ShouldRefreshAddressDisplay(bool selectionChanged, bool boxFocused, bool editedSinceDisplay) =>
        selectionChanged || !boxFocused || !editedSinceDisplay;

    /// <summary>
    /// True when Enter should reload the current page: the user typed nothing
    /// new and the box still shows the recorded display text.
    /// </summary>
    public static bool ShouldReload(string typed, string lastDisplayed, bool edited, string currentUrl) =>
        !edited && !string.IsNullOrEmpty(currentUrl) &&
        string.Equals(typed?.Trim() ?? "", lastDisplayed?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Full address bar text: the bare domain for web pages, the friendly
    /// page name for internal pages that have one, empty for blanks and for
    /// the new tab. Empty lets the address field draw its own hint instead of
    /// dressing a label up as typed text. Falls back to the tab title for
    /// internal pages without a mapped name.
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
        // The new tab has no page identity worth printing. Empty text keeps the
        // field genuinely empty so the empty-input hint can draw and the caret
        // lands at the start, the way a browser omnibox behaves.
        InternalUrls.NewTab => "",
        InternalUrls.Settings => "Settings",
        InternalUrls.OneTab => "OneTab",
        InternalUrls.History => "History",
        InternalUrls.Extensions => "Extensions",
        InternalUrls.Downloads => "Downloads",
        InternalUrls.Focus => "Focus Locked",
        InternalUrls.Health => "Health",
        _ => fallbackTitle
    };

    /// <summary>
    /// Returns the canonical ASCII hostname for DNS hosts, converting valid internationalized
    /// domain names to Punycode. Preserves bracketed IPv6 literals, IPv4 addresses,
    /// and fallback representations for malformed or unconvertible hosts. Never throws.
    /// </summary>
    public static string ToAsciiHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return "";
        var trimmed = host.Trim();

        // Preserve bracketed IPv6 literals
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            return trimmed;

        if (IPAddress.TryParse(trimmed, out var ip))
            return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{ip}]" : ip.ToString();

        // If the host is already pure ASCII, return as-is
        if (IsPureAscii(trimmed))
            return trimmed;

        try
        {
            var mapping = new System.Globalization.IdnMapping();
            return mapping.GetAscii(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    /// <summary>
    /// Safely obtains the canonical ASCII hostname for a URI. Returns the Punycode host for valid IDNs,
    /// preserves bracketed IPv6 literals, IPv4 literals, and falls back to the unencoded host or empty string
    /// if IDN normalization fails. Never throws.
    /// </summary>
    public static string ToSafeAsciiHost(Uri? uri)
    {
        if (uri == null) return "";
        if (uri.HostNameType == UriHostNameType.IPv6) return uri.Host;

        try
        {
            return ToAsciiHost(uri.IdnHost);
        }
        catch
        {
            try
            {
                return ToAsciiHost(uri.Host);
            }
            catch
            {
                return "";
            }
        }
    }

    /// <summary>
    /// Shortens a URL for address bar display: bare domain without a leading
    /// www, like standard browsers. Valid internationalized domains display
    /// as canonical ASCII Punycode. Internal pages and blanks show empty.
    /// Non-hierarchical opaque URLs and invalid hosts show safe raw text. Never throws.
    /// </summary>
    public static string ToDisplayDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || InternalUrls.IsInternal(url)) return "";
        var text = url.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return text;

        // Opaque non-hierarchical URLs (like mailto:, javascript:, data:) have no address bar domain
        if (!text.StartsWith(uri.Scheme + "://", StringComparison.OrdinalIgnoreCase))
            return text;

        string host;
        try
        {
            if (uri.HostNameType == UriHostNameType.IPv6)
            {
                host = uri.Host; // [::1]
            }
            else if (uri.HostNameType == UriHostNameType.Dns)
            {
                host = ToSafeAsciiHost(uri);
            }
            else
            {
                host = uri.Host;
            }
        }
        catch
        {
            host = "";
        }

        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        return string.IsNullOrEmpty(host) ? text : host;
    }

    /// <summary>
    /// Formats a full URL for display by normalizing any valid internationalized DNS host
    /// to its canonical ASCII Punycode form while strictly preserving scheme, credentials,
    /// port, path, query, fragment, and original escaping. Non-hierarchical, opaque,
    /// or unconvertible URLs return safe original text. Never throws.
    /// </summary>
    public static string FormatDisplayUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var text = url.Trim();
        if (InternalUrls.IsInternal(text)) return text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return text;

        // Opaque / non-hierarchical URLs (like mailto:, javascript:, data:) leave unchanged
        if (!text.StartsWith(uri.Scheme + "://", StringComparison.OrdinalIgnoreCase))
            return text;

        if (uri.HostNameType != UriHostNameType.Dns)
            return text;

        try
        {
            if (string.IsNullOrEmpty(uri.Host))
                return text;
        }
        catch
        {
            return text;
        }

        int authorityStart = uri.Scheme.Length + 3; // index immediately following "scheme://"
        int authorityEnd = text.Length;
        for (int i = authorityStart; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '/' || c == '?' || c == '#')
            {
                authorityEnd = i;
                break;
            }
        }

        if (authorityEnd <= authorityStart)
            return text;

        string authority = text.Substring(authorityStart, authorityEnd - authorityStart);
        int atIndex = authority.LastIndexOf('@');
        int hostPortStart = atIndex >= 0 ? authorityStart + atIndex + 1 : authorityStart;
        string hostPort = atIndex >= 0 ? authority.Substring(atIndex + 1) : authority;

        // IPv6 bracketed hosts are not IDN
        if (hostPort.StartsWith('['))
            return text;

        int colonIndex = hostPort.IndexOf(':');
        int hostLength = colonIndex >= 0 ? colonIndex : hostPort.Length;
        if (hostLength == 0)
            return text;

        string rawHost = hostPort.Substring(0, hostLength);
        if (IsPureAscii(rawHost))
            return text;

        string asciiHost;
        try
        {
            var mapping = new System.Globalization.IdnMapping();
            asciiHost = mapping.GetAscii(rawHost);
        }
        catch
        {
            // If the host contains invalid IDN characters, return original text safely
            return text;
        }

        if (string.Equals(asciiHost, rawHost, StringComparison.Ordinal))
            return text;

        return string.Concat(text.AsSpan(0, hostPortStart), asciiHost, text.AsSpan(hostPortStart + hostLength));
    }

    private static bool IsPureAscii(string value)
    {
        foreach (var c in value)
        {
            if (c > 127) return false;
        }
        return true;
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
