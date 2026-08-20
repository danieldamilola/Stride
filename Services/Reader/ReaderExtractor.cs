using StrideBrowser.Models.Reader;

namespace StrideBrowser.Services.Reader;

/// <summary>
/// Scaffold. Real extraction runs as JS inside the page WebView2 via ExecuteScriptAsync.
/// This stub exists so DI can resolve IReaderExtractor before the WebView2 wiring is filled in.
/// </summary>
public sealed class ReaderExtractor : IReaderExtractor
{
    public Task<bool> CanExtractAsync(Guid tabId)
    {
        throw new NotImplementedException("ReaderExtractor scaffold - not implemented");
    }

    public Task<ArticleResult> ExtractAsync(Guid tabId)
    {
        throw new NotImplementedException("ReaderExtractor scaffold - not implemented");
    }
}
