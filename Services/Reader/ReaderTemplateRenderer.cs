using System.IO;
using System.Net;
using System.Text;
using StrideBrowser.Helpers;
using StrideBrowser.Models.Reader;

namespace StrideBrowser.Services.Reader;

/// <summary>
/// Reads Resources/Reader/ReaderTemplate.html and ReaderTemplate.css as embedded resources,
/// inlines the CSS into a &lt;style&gt; block, substitutes theme variables,
/// injects sanitized HTML plus title, and emits the CSP meta:
/// default-src 'none'; img-src http: https:; style-src 'unsafe-inline'; font-src 'none'; connect-src 'none'; script-src 'none';
/// No &lt;link rel="stylesheet"&gt; is ever emitted.
/// </summary>
public sealed class ReaderTemplateRenderer : IReaderTemplateRenderer
{
    public string Render(ReaderContent content, ReaderOptions options, string sanitizedHtml)
    {
        return RenderInternal(content.Title, string.Empty, sanitizedHtml, options, content.Url);
    }

    public string Render(ArticleResult article, string sanitizedHtml, ReaderOptions options, string sourceUrl)
    {
        return RenderInternal(article.Title, article.Byline, sanitizedHtml, options, sourceUrl);
    }

    private static string RenderInternal(string title, string byline, string sanitizedHtml, ReaderOptions options, string sourceUrl)
    {
        var template = LoadTemplate();
        var css = LoadCss();

        var replacements = new Dictionary<string, string>
        {
            ["Title"] = WebUtility.HtmlEncode(title ?? string.Empty),
            ["Byline"] = WebUtility.HtmlEncode(byline ?? string.Empty),
            ["SanitizedContent"] = sanitizedHtml ?? string.Empty,
            ["InlinedCss"] = css,
            ["Theme"] = EscapeHtml(options.Theme ?? "system"),
            ["FontScale"] = options.FontScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            ["ContentWidth"] = options.ContentWidth.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
        };

        return System.Text.RegularExpressions.Regex.Replace(
            template,
            @"\{\{(\w+)\}\}",
            m => replacements.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    private static string LoadTemplate()
    {
        var t = ResourceLoader.TryLoad("Resources.Reader.ReaderTemplate.html");
        if (t is not null) return t;
        // Fallback for tests that run without embedded resources built
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Reader", "ReaderTemplate.html");
        if (File.Exists(path)) return File.ReadAllText(path, Encoding.UTF8);
#if DEBUG
        var alt = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Reader", "ReaderTemplate.html");
        if (File.Exists(alt)) return File.ReadAllText(alt, Encoding.UTF8);
#endif
        throw new FileNotFoundException("ReaderTemplate.html not found as embedded resource or file");
    }

    private static string LoadCss()
    {
        var css = ResourceLoader.TryLoad("Resources.Reader.ReaderTemplate.css");
        if (css is not null) return css;
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Reader", "ReaderTemplate.css");
        if (File.Exists(path)) return File.ReadAllText(path, Encoding.UTF8);
#if DEBUG
        var alt = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Reader", "ReaderTemplate.css");
        if (File.Exists(alt)) return File.ReadAllText(alt, Encoding.UTF8);
#endif
        return string.Empty;
    }

    private static string EscapeHtml(string value) => WebUtility.HtmlEncode(value);
}
