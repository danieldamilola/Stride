namespace StrideBrowser.Models.Reader;

/// <summary>
/// Raw result returned by the JS extractor running inside the page WebView2.
/// This is untrusted third-party content until it passes <see cref="Services.Reader.IReaderSanitizer"/>.
/// </summary>
public sealed record ArticleResult(
    string Title,
    string Byline,
    string Excerpt,
    string ContentHtml,
    string SiteName,
    int Length,
    bool IsReadable
);
