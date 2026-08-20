using StrideBrowser.Models.Reader;

namespace StrideBrowser.Services.Reader;

/// <summary>
/// Scaffold. Real renderer reads Resources/Reader/ReaderTemplate.html and ReaderTemplate.css as embedded resources,
/// inlines the CSS into a &lt;style&gt; block with {{FontScale}} and {{Theme}} substituted,
/// injects sanitized HTML plus title, and emits the CSP meta:
/// default-src 'none'; img-src http: https:; style-src 'unsafe-inline'; font-src 'none'; connect-src 'none'; script-src 'none';
/// No &lt;link rel="stylesheet"&gt; is ever emitted.
/// </summary>
public sealed class ReaderTemplateRenderer : IReaderTemplateRenderer
{
    public string Render(ReaderContent content, ReaderOptions options, string sanitizedHtml)
    {
        throw new NotImplementedException("ReaderTemplateRenderer scaffold - not implemented");
    }

    public string Render(ArticleResult article, string sanitizedHtml, ReaderOptions options, string sourceUrl)
    {
        throw new NotImplementedException("ReaderTemplateRenderer scaffold - not implemented");
    }
}
