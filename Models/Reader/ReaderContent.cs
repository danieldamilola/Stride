namespace StrideBrowser.Models.Reader;

/// <summary>
/// Sanitized, template-wrapped content ready to render in the dedicated reader WebView2.
/// Script is never enabled in that view. This is display-safe HTML.
/// </summary>
public sealed record ReaderContent(
    string Title,
    string Html,
    string Url,
    DateTime ExtractedAt
);
