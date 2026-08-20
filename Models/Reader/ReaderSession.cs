namespace StrideBrowser.Models.Reader;

/// <summary>
/// Per-tab reader state. Owned by <see cref="Services.Reader.IReaderService"/>.
/// ViewModel mirrors the active tab session, never owns it.
/// </summary>
public sealed record ReaderSession(
    Guid TabId,
    bool IsInReader,
    ReaderContent? Current,
    string? OriginalUrl,
    int ScrollOffset
);
