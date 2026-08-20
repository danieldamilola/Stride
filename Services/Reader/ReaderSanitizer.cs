using System.Text;
using System.Text.RegularExpressions;

namespace StrideBrowser.Services.Reader;

/// <summary>
/// Sanitizes untrusted article HTML. Contract pinned in ADR v4.
/// Allow-list tags only, drop style/srcset/on*/xmlns, validate href and src to http or https only, block javascript: and data:.
/// CSP is emitted by the template renderer, not here.
/// </summary>
public sealed class ReaderSanitizer : IReaderSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "h1", "h2", "h3", "blockquote", "ul", "ol", "li", "pre", "code", "figure", "figcaption", "img", "a", "strong", "em", "hr", "br"
    };

    private static readonly HashSet<string> DropEntirelyTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "form", "link", "meta", "noscript", "template", "svg", "math", "canvas", "video", "audio", "picture", "source", "track", "frame", "frameset", "applet", "base",
        "nav", "aside", "header", "footer", "button", "select", "textarea", "input", "label", "menu", "dialog"
    };

    private static readonly Regex TagRegex = new(@"<\s*(/?)\s*([a-zA-Z0-9]+)([^>]*?)(/?)\s*>", RegexOptions.Compiled);
    private static readonly Regex AttrRegex = new(@"([a-zA-Z_:][a-zA-Z0-9:_\-\.]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'`=<>]+))", RegexOptions.Compiled);
    private static readonly Regex CommentRegex = new(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex DropEntirelyRegex = new(@"<\s*(script|style|iframe|object|embed|form|link|meta|noscript|template|svg|math|canvas|video|audio|picture|source|track|frame|frameset|applet|base|nav|aside|header|footer|button|select|textarea|input|label|menu|dialog)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    // Self-closing drop-entirely without closing tag, e.g. <link ...> or <meta ...>
    private static readonly Regex DropEntirelySelfClosingRegex = new(@"<\s*(link|meta|base|source|track|frame)\b[^>]*/?\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Sanitize(string untrustedHtml, string documentBaseUrl)
    {
        if (string.IsNullOrEmpty(untrustedHtml)) return string.Empty;

        var html = untrustedHtml;

        html = CommentRegex.Replace(html, string.Empty);
        html = DropEntirelyRegex.Replace(html, string.Empty);
        html = DropEntirelySelfClosingRegex.Replace(html, string.Empty);

        var sb = new StringBuilder(html.Length);
        var lastIndex = 0;

        foreach (Match match in TagRegex.Matches(html))
        {
            sb.Append(html, lastIndex, match.Index - lastIndex);
            lastIndex = match.Index + match.Length;

            var isClosing = match.Groups[1].Value == "/";
            var tagName = match.Groups[2].Value;
            var attrText = match.Groups[3].Value;
            var selfClose = match.Groups[4].Value == "/";

            var lowerTag = tagName.ToLowerInvariant();

            if (DropEntirelyTags.Contains(lowerTag))
            {
                continue;
            }

            if (isClosing)
            {
                if (AllowedTags.Contains(lowerTag))
                {
                    sb.Append("</").Append(lowerTag).Append('>');
                }

                continue;
            }

            if (!AllowedTags.Contains(lowerTag))
            {
                continue;
            }

            var filteredAttrs = FilterAttributes(lowerTag, attrText, documentBaseUrl);

            sb.Append('<').Append(lowerTag);
            if (filteredAttrs.Length > 0)
            {
                sb.Append(' ').Append(filteredAttrs);
            }

            if (selfClose && (lowerTag == "img" || lowerTag == "hr" || lowerTag == "br"))
            {
                sb.Append(" />");
            }
            else
            {
                sb.Append('>');
            }
        }

        sb.Append(html, lastIndex, html.Length - lastIndex);

        var result = sb.ToString();
        result = CommentRegex.Replace(result, string.Empty);
        return result;
    }

    private static string FilterAttributes(string tag, string attrText, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(attrText)) return string.Empty;

        var parts = new List<string>();

        foreach (Match m in AttrRegex.Matches(attrText))
        {
            var name = m.Groups[1].Value;
            var value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
            var lowerName = name.ToLowerInvariant();

            if (lowerName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                continue;
            if (lowerName is "style" or "srcset" or "xmlns" or "srcdoc")
                continue;

            if (lowerName is "href" or "src" or "cite" or "poster" or "data")
            {
                var sanitized = SanitizeUrl(value, baseUrl);
                if (sanitized is null) continue;
                parts.Add($"{lowerName}=\"{EscapeAttribute(sanitized)}\"");
                continue;
            }

            if (lowerName is "alt" or "title")
            {
                parts.Add($"{lowerName}=\"{EscapeAttribute(value)}\"");
                continue;
            }

            // Drop all other attributes including class, id, width, height, target, rel, etc.
        }

        return string.Join(" ", parts);
    }

    private static string? SanitizeUrl(string raw, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var trimmed = raw.Trim();

        if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            return null;
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;
        if (trimmed.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase))
            return null;
        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return null;

        // Protocol-relative //example.com/foo
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            trimmed = "https:" + trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var abs))
        {
            if (abs.Scheme is "http" or "https")
                return abs.ToString();
            return null;
        }

        if (!string.IsNullOrEmpty(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            if (Uri.TryCreate(baseUri, trimmed, out var resolved))
            {
                if (resolved.Scheme is "http" or "https")
                    return resolved.ToString();
            }
        }

        return null;
    }

    private static string EscapeAttribute(string value)
    {
        return value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
