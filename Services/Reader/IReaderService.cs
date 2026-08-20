using StrideBrowser.Models.Reader;

namespace StrideBrowser.Services.Reader;

/// <summary>
/// Tab-scoped reader coordinator. Service owns per-tab truth via <see cref="ReaderSession"/>.
/// ViewModel mirrors the active tab session and never writes it directly.
/// Extraction itself runs as JS inside the page WebView2; this interface stays implementation agnostic
/// so the Candidate A (JS heuristics) to B (C# parse) swap does not break callers.
/// </summary>
public interface IReaderService
{
    /// <summary>Whether the page in the given tab is classified as readable.</summary>
    Task<bool> CanEnterReaderAsync(Guid tabId);

    /// <summary>Extract, sanitize, template, store session, and return display content.</summary>
    Task<ReaderContent> EnterReaderAsync(Guid tabId);

    /// <summary>Exit reader for the given tab. Restores original URL and scroll offset visibility.</summary>
    Task ExitReaderAsync(Guid tabId);

    /// <summary>
    /// Re-render or re-extract for the given tab.
    /// Triggered by manual refresh button or by ReaderOptions change while in reader.
    /// If options changed, re-uses cached sanitized content with new theme variables.
    /// If re-extract requested, re-runs JS extractor and replaces cache.
    /// </summary>
    Task<ReaderContent> RefreshAsync(Guid tabId);

    /// <summary>Current presentation options persisted via ISettingsStore.</summary>
    ReaderOptions Options { get; }

    /// <summary>Persist new presentation options and re-render active reader sessions.</summary>
    Task UpdateOptionsAsync(ReaderOptions options);

    /// <summary>Per-tab session, or null if tab has never entered reader.</summary>
    ReaderSession? GetSession(Guid tabId);

    /// <summary>Remove session when tab closes. Called from TabEngine.TabClosed.</summary>
    void RemoveSession(Guid tabId);

    /// <summary>Raised whenever any tab session changes. Payload is the affected tabId.</summary>
    event EventHandler<Guid> SessionChanged;
}
