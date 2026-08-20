using StrideBrowser.Models.Reader;

namespace StrideBrowser.Services.Reader;

/// <summary>
/// Scaffold implementation. Stores per-tab ReaderSession, owns truth, raises SessionChanged.
/// Continuations write only into the per-tab session keyed by the tabId the call was made with,
/// regardless of what is active when they return. ViewModel is state-driven and re-reads GetSession(ActiveTabId).
/// Lazy allocation: no per-tab WebView2 is created here. Exit and RemoveSession dispose what Enter created.
/// </summary>
public sealed class ReaderService : IReaderService
{
    private readonly ISettingsStore _settingsStore;
    private readonly IReaderExtractor _extractor;
    private readonly IReaderSanitizer _sanitizer;
    private readonly IReaderTemplateRenderer _renderer;
    private readonly Dictionary<Guid, ReaderSession> _sessions = new();
    private ReaderOptions _options;

    public ReaderService(
        ISettingsStore settingsStore,
        IReaderExtractor extractor,
        IReaderSanitizer sanitizer,
        IReaderTemplateRenderer renderer)
    {
        _settingsStore = settingsStore;
        _extractor = extractor;
        _sanitizer = sanitizer;
        _renderer = renderer;
        _options = ReaderOptions.Default;
    }

    public ReaderOptions Options => _options;

    public event EventHandler<Guid>? SessionChanged;

    public Task<bool> CanEnterReaderAsync(Guid tabId)
    {
        throw new NotImplementedException("ReaderService scaffold - not implemented");
    }

    public Task<ReaderContent> EnterReaderAsync(Guid tabId)
    {
        throw new NotImplementedException("ReaderService scaffold - not implemented");
    }

    public Task ExitReaderAsync(Guid tabId)
    {
        throw new NotImplementedException("ReaderService scaffold - not implemented");
    }

    public Task<ReaderContent> RefreshAsync(Guid tabId)
    {
        throw new NotImplementedException("ReaderService scaffold - not implemented");
    }

    public Task UpdateOptionsAsync(ReaderOptions options)
    {
        throw new NotImplementedException("ReaderService scaffold - not implemented");
    }

    public ReaderSession? GetSession(Guid tabId)
    {
        _sessions.TryGetValue(tabId, out var session);
        return session;
    }

    public void RemoveSession(Guid tabId)
    {
        if (_sessions.Remove(tabId))
        {
            SessionChanged?.Invoke(this, tabId);
        }
    }

    // Called from host side NavigationStarting / NewWindowRequested handlers.
    // Must exit reader and stay safe if the tab was already closed.
    public async Task HandleReaderLinkNavigationAsync(Guid tabId, string uri, Func<Guid, string, Task> navigatePage)
    {
        try
        {
            await navigatePage(tabId, uri);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Reader link navigation failed: {ex.Message}");
        }

        try
        {
            await ExitReaderAsync(tabId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Reader exit after link nav failed: {ex.Message}");
        }
    }
}
