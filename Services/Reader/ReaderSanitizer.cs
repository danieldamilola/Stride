namespace StrideBrowser.Services.Reader;

/// <summary>
/// Scaffold. Contract is pinned, body is not implemented.
/// Allow-list tags only, drop style/srcset/on*/xmlns, validate href/src to http or https only, block javascript: and data:.
/// CSP is emitted by the template renderer, not here.
/// </summary>
public sealed class ReaderSanitizer : IReaderSanitizer
{
    public string Sanitize(string untrustedHtml, string documentBaseUrl)
    {
        throw new NotImplementedException("ReaderSanitizer scaffold - not implemented");
    }
}
