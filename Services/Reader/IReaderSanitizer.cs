namespace StrideBrowser.Services.Reader;

/// <summary>
/// Sanitizes untrusted ArticleResult.ContentHtml into display-safe HTML.
///
/// Contract pinned in ADR v4:
/// - Allow-list tags only: p, h1, h2, h3, blockquote, ul, ol, li, pre, code, figure, figcaption, img, a, strong, em, hr
/// - Drop style tags and all style attributes. Drop script, iframe, object, embed, form, link, meta, srcdoc.
/// - Drop all on* attributes, xmlns.
/// - Drop srcset entirely (list, not single URL, not needed for reader).
/// - Validate every URL-bearing attribute: href, src, cite, poster, data
///   Allow only http or https after resolving to absolute. Block javascript:.
///   For img src, allow only http or https. Block data: like everywhere else.
/// - Relative URLs are resolved to absolute after scheme check and rejected if not http or https.
/// </summary>
public interface IReaderSanitizer
{
    string Sanitize(string untrustedHtml, string documentBaseUrl);
}
