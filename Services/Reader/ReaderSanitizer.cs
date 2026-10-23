using System.Diagnostics;
using System.Net;
using System.Text;

namespace UmberBrowser.Services.Reader;

/// <summary>Rebuilds untrusted article HTML using only allowed tags and attributes.</summary>
public sealed class ReaderSanitizer : IReaderSanitizer
{
    internal const int MaxInputCharacters = 2_000_000;
    private static readonly TimeSpan ExecutionBudget = TimeSpan.FromMilliseconds(500);
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "h1", "h2", "h3", "blockquote", "ul", "ol", "li", "pre", "code", "figure", "figcaption", "img", "a", "strong", "em", "hr", "br"
    };
    private static readonly HashSet<string> DropEntirelyTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "form", "link", "meta", "noscript", "template", "svg", "math", "canvas", "video", "audio", "picture", "source", "track", "frame", "frameset", "applet", "base",
        "nav", "aside", "header", "footer", "button", "select", "textarea", "input", "label", "menu", "dialog"
    };
    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "embed", "link", "meta", "source", "track", "frame", "base", "input", "img", "hr", "br"
    };

    public string Sanitize(string untrustedHtml, string documentBaseUrl)
    {
        if (string.IsNullOrEmpty(untrustedHtml)) return string.Empty;
        if (untrustedHtml.Length > MaxInputCharacters)
            throw new InvalidOperationException("Reader article exceeds the sanitization size limit.");
        var started = Stopwatch.GetTimestamp();
        var output = new StringBuilder(untrustedHtml.Length);
        var dropped = new Stack<string>();
        var position = 0;
        while (position < untrustedHtml.Length)
        {
            CheckBudget(started);
            if (untrustedHtml[position] != '<')
            {
                var end = untrustedHtml.IndexOf('<', position);
                if (end < 0) end = untrustedHtml.Length;
                if (dropped.Count == 0) output.Append(untrustedHtml, position, end - position);
                position = end;
                continue;
            }
            if (untrustedHtml.AsSpan(position).StartsWith("<!--"))
            {
                var end = untrustedHtml.IndexOf("-->", position + 4, StringComparison.Ordinal);
                position = end < 0 ? untrustedHtml.Length : end + 3;
                continue;
            }
            var cursor = position + 1;
            SkipWhiteSpace(untrustedHtml, ref cursor);
            var closing = cursor < untrustedHtml.Length && untrustedHtml[cursor] == '/';
            if (closing) { cursor++; SkipWhiteSpace(untrustedHtml, ref cursor); }
            var nameStart = cursor;
            while (cursor < untrustedHtml.Length && char.IsAsciiLetterOrDigit(untrustedHtml[cursor])) cursor++;
            if (cursor == nameStart)
            {
                if (dropped.Count == 0) output.Append("&lt;");
                position++;
                continue;
            }
            var name = untrustedHtml[nameStart..cursor].ToLowerInvariant();
            var attrStart = cursor;
            char quote = '\0';
            // Consume each character once, including malformed attributes. A missing quote
            // discards the unfinished token rather than exposing its contents as markup.
            for (; cursor < untrustedHtml.Length; cursor++)
            {
                if ((cursor & 1023) == 0) CheckBudget(started);
                var c = untrustedHtml[cursor];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c is '\'' or '"') quote = c;
                else if (c == '>') break;
            }
            if (cursor == untrustedHtml.Length) break;
            var selfClosing = cursor > attrStart && untrustedHtml[cursor - 1] == '/';
            position = cursor + 1;
            if (dropped.Count > 0)
            {
                if (closing && name == dropped.Peek()) dropped.Pop();
                else if (!closing && DropEntirelyTags.Contains(name) && !VoidTags.Contains(name) && !selfClosing) dropped.Push(name);
                continue;
            }
            if (DropEntirelyTags.Contains(name))
            {
                if (!closing && !VoidTags.Contains(name) && !selfClosing) dropped.Push(name);
                continue;
            }
            if (!AllowedTags.Contains(name)) continue;
            if (closing) { output.Append("</").Append(name).Append('>'); continue; }
            output.Append('<').Append(name);
            AppendAttributes(output, untrustedHtml, attrStart, cursor, documentBaseUrl, started);
            output.Append(selfClosing && VoidTags.Contains(name) ? " />" : ">");
        }
        CheckBudget(started);
        return output.ToString();
    }

    private static void AppendAttributes(StringBuilder output, string html, int position, int end, string baseUrl, long started)
    {
        while (position < end)
        {
            CheckBudget(started);
            SkipWhiteSpace(html, ref position);
            if (position >= end) break;
            var nameStart = position;
            if (!(char.IsAsciiLetter(html[position]) || html[position] is '_' or ':')) { position++; continue; }
            while (position < end && (char.IsAsciiLetterOrDigit(html[position]) || html[position] is '_' or ':' or '-' or '.')) position++;
            var name = html[nameStart..position].ToLowerInvariant();
            SkipWhiteSpace(html, ref position);
            if (position >= end || html[position] != '=') continue;
            position++;
            SkipWhiteSpace(html, ref position);
            if (position >= end) break;
            var quote = html[position] is '\'' or '"' ? html[position++] : '\0';
            var valueStart = position;
            if (quote != '\0') while (position < end && html[position] != quote) position++;
            else while (position < end && !char.IsWhiteSpace(html[position]) && html[position] is not ('"' or '\'' or '`' or '=' or '<' or '>')) position++;
            var value = WebUtility.HtmlDecode(html[valueStart..position]);
            if (quote != '\0' && position < end) position++;
            if (position == valueStart && quote == '\0') { position++; continue; }
            if (name is "href" or "src" or "cite" or "poster" or "data")
            {
                var url = SanitizeUrl(value, baseUrl);
                if (url is null || (name == "src" && Helpers.NetworkTargetGuard.IsPrivateHost(new Uri(url).Host))) continue;
                value = url;
            }
            else if (name is not ("alt" or "title")) continue;
            output.Append(' ').Append(name).Append("=\"").Append(EscapeAttribute(value)).Append('"');
        }
    }

    private static void SkipWhiteSpace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
    }
    private static void CheckBudget(long started)
    {
        if (Stopwatch.GetElapsedTime(started) > ExecutionBudget)
            throw new TimeoutException("Reader sanitization exceeded its execution budget.");
    }
    private static string? SanitizeUrl(string raw, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal)) trimmed = "https:" + trimmed;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
            return absolute.Scheme is "http" or "https" ? absolute.ToString() : null;
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) && Uri.TryCreate(origin, trimmed, out var resolved))
            return resolved.Scheme is "http" or "https" ? resolved.ToString() : null;
        return null;
    }
    private static string EscapeAttribute(string value) => value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
}
