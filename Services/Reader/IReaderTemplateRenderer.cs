using StrideBrowser.Models.Reader;

namespace StrideBrowser.Services.Reader;

/// <summary>
/// Wraps sanitized HTML into the display document.
/// ReaderTemplate.css is not linked. It is read as a string resource and inlined
/// into a &lt;style&gt; block at render time with theme variables substituted.
/// CSP is emitted as &lt;meta http-equiv="Content-Security-Policy"&gt; at that time.
/// </summary>
public interface IReaderTemplateRenderer
{
    string Render(ReaderContent content, ReaderOptions options, string sanitizedHtml);
    string Render(ArticleResult article, string sanitizedHtml, ReaderOptions options, string sourceUrl);
}
