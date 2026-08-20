using StrideBrowser.Models.Reader;

namespace StrideBrowser.Services.Reader;

/// <summary>
/// Abstraction over the JS extraction that runs inside the page WebView2.
/// Kept behind an interface so the real WebView2 path and a test path can be swapped
/// without touching IReaderService callers.
/// </summary>
public interface IReaderExtractor
{
    /// <summary>Fast check whether the current document looks like an article.</summary>
    Task<bool> CanExtractAsync(Guid tabId);

    /// <summary>Clone, strip nav and ads and scripts, and return raw article HTML.</summary>
    Task<ArticleResult> ExtractAsync(Guid tabId);
}
