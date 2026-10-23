using UmberBrowser.Engine;
using UmberBrowser.Models;
using UmberBrowser.Models.Reader;

namespace UmberBrowser.Services.Reader;

/// <summary>
/// Stores per-tab ReaderSession, owns truth, raises SessionChanged.
/// Continuations write only into the per-tab session keyed by the tabId the call was made with,
/// regardless of what is active when they return. ViewModel is state-driven and re-reads GetSession(ActiveTabId).
/// Lazy allocation: no per-tab WebView2 is created here. Exit and RemoveSession dispose what Enter created.
/// </summary>
public sealed class ReaderService : IReaderService
{
    private readonly BrowserSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly IReaderExtractor _extractor;
    private readonly IReaderSanitizer _sanitizer;
    private readonly IReaderTemplateRenderer _renderer;
    private readonly Func<Guid, string?> _getTabUrl;
    private readonly Dictionary<Guid, object> _requests = new();
    private readonly Dictionary<Guid, ReaderSession> _sessions = new();
    private readonly Dictionary<Guid, ArticleResult> _articleCache = new();
    private ReaderOptions _options;

    public ReaderService(
        BrowserSettings settings,
        ISettingsStore settingsStore,
        IReaderExtractor extractor,
        IReaderSanitizer sanitizer,
        IReaderTemplateRenderer renderer,
        TabEngine engine)
        : this(settings, settingsStore, extractor, sanitizer, renderer, engine.GetTabUrl) { }

    internal ReaderService(
        BrowserSettings settings, ISettingsStore settingsStore, IReaderExtractor extractor,
        IReaderSanitizer sanitizer, IReaderTemplateRenderer renderer, Func<Guid, string?> getTabUrl)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _extractor = extractor;
        _sanitizer = sanitizer;
        _renderer = renderer;
        _getTabUrl = getTabUrl;
        _options = new ReaderOptions(
            FontScale: _settings.ReaderFontScale,
            Theme: _settings.ReaderTheme ?? "system",
            ContentWidth: _settings.ReaderContentWidth
        );
        if (_options.FontScale <= 0) _options = ReaderOptions.Default;
        if (string.IsNullOrWhiteSpace(_options.Theme)) _options = _options with { Theme = "system" };
        if (_options.ContentWidth <= 0) _options = _options with { ContentWidth = 720 };
    }

    public ReaderOptions Options => _options;

    /// <summary>
    /// Hands a tab's reader session to the reader service of the window it moved to.
    /// Each window owns its own service, so without this the tab would come back from a
    /// tear looking normal while the overlay no longer matches the page under it.
    /// </summary>
    public void TransferSessionTo(ReaderService destination, Guid tabId)
    {
        if (destination is null || ReferenceEquals(destination, this)) return;
        _requests.Remove(tabId);
        destination._requests.Remove(tabId);
        if (_sessions.Remove(tabId, out var session)) destination._sessions[tabId] = session;
        if (_articleCache.Remove(tabId, out var article)) destination._articleCache[tabId] = article;
    }

    public event EventHandler<Guid>? SessionChanged;

    public async Task<bool> CanEnterReaderAsync(Guid tabId)
    {
        var task = _extractor.CanExtractAsync(tabId);
        var delay = Task.Delay(3000);
        var completed = await Task.WhenAny(task, delay);
        if (completed == delay) return false;
        return await task;
    }

    public async Task<ReaderContent> EnterReaderAsync(Guid tabId)
    {
        var url = _getTabUrl(tabId) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url) || InternalUrls.IsInternal(url))
        {
            throw new InvalidOperationException("Reader mode is not available for internal pages");
        }

        // If already in reader, return existing
        if (_sessions.TryGetValue(tabId, out var existing) && existing.IsInReader && existing.Current is not null)
        {
            return existing.Current;
        }

        var request = new object();
        _requests[tabId] = request;
        try
        {
            var article = await _extractor.ExtractAsync(tabId).WaitAsync(TimeSpan.FromSeconds(4));
            if (!IsCurrentRequest(tabId, request, url)) return AbortedContent(article.Title, url);
            var content = await PrepareContentAsync(tabId, request, article, url);
            if (!IsCurrentRequest(tabId, request, url)) return AbortedContent(article.Title, url);
            _sessions[tabId] = new ReaderSession(tabId, true, content, url, 0);
            _articleCache[tabId] = article;
            SessionChanged?.Invoke(this, tabId);
            return content;
        }
        finally
        {
            CompleteRequest(tabId, request);
        }
    }

    private bool IsCurrentRequest(Guid tabId, object request, string url) =>
        _requests.TryGetValue(tabId, out var current) && ReferenceEquals(current, request) && _getTabUrl(tabId) == url;

    private void CompleteRequest(Guid tabId, object request)
    {
        if (_requests.TryGetValue(tabId, out var current) && ReferenceEquals(current, request)) _requests.Remove(tabId);
    }

    private static ReaderContent AbortedContent(string? title, string url) => new(title ?? "Reader", string.Empty, url, DateTime.UtcNow);

    private async Task<ReaderContent> PrepareContentAsync(Guid tabId, object request, ArticleResult article, string url)
    {
        var sanitized = await Task.Run(() => _sanitizer.Sanitize(article.ContentHtml ?? string.Empty, url));
        while (IsCurrentRequest(tabId, request, url))
        {
            var options = _options;
            var rendered = await Task.Run(() => _renderer.Render(article, sanitized, options, url));
            if (!IsCurrentRequest(tabId, request, url)) break;
            // An option update can arrive while a worker renders. Never publish the old theme.
            if (options != _options) continue;
            return new ReaderContent(article.Title ?? "Reader", rendered, url, DateTime.UtcNow);
        }
        return AbortedContent(article.Title, url);
    }

    public Task ExitReaderAsync(Guid tabId)
    {
        _requests.Remove(tabId);
        if (_sessions.TryGetValue(tabId, out var session) && session.IsInReader)
        {
            var exited = session with { IsInReader = false };
            _sessions[tabId] = exited;
            SessionChanged?.Invoke(this, tabId);
        }

        return Task.CompletedTask;
    }

    public async Task<ReaderContent> RefreshAsync(Guid tabId)
    {
        if (!_sessions.TryGetValue(tabId, out var session) || session.Current is null)
        {
            // Not in reader, treat as enter
            return await EnterReaderAsync(tabId);
        }

        if (!_articleCache.TryGetValue(tabId, out var cached) || !session.IsInReader)
            return session.Current;
        var url = _getTabUrl(tabId);
        if (url is null || url != session.OriginalUrl) return session.Current;
        var request = new object();
        _requests[tabId] = request;
        try
        {
            var refreshed = await PrepareContentAsync(tabId, request, cached, url);
            if (!IsCurrentRequest(tabId, request, url) || !_sessions.TryGetValue(tabId, out var current) || !current.IsInReader)
                return session.Current;
            _sessions[tabId] = current with { Current = refreshed };
            SessionChanged?.Invoke(this, tabId);
            return refreshed;
        }
        finally { CompleteRequest(tabId, request); }
    }

    public Task UpdateOptionsAsync(ReaderOptions options)
    {
        var normalized = options;
        if (normalized.FontScale <= 0) normalized = normalized with { FontScale = 1.0 };
        if (string.IsNullOrWhiteSpace(normalized.Theme)) normalized = normalized with { Theme = "system" };
        if (normalized.ContentWidth <= 0) normalized = normalized with { ContentWidth = 720 };

        _options = normalized;
        _settings.ReaderFontScale = normalized.FontScale;
        _settings.ReaderTheme = normalized.Theme;
        _settings.ReaderContentWidth = normalized.ContentWidth;
        _settingsStore.Save(_settings);

        // Re-render all active reader sessions
        var activeIds = _sessions.Where(kvp => kvp.Value.IsInReader).Select(kvp => kvp.Key).ToList();
        return Task.WhenAll(activeIds.Select(RefreshAsync));
    }

    public ReaderSession? GetSession(Guid tabId)
    {
        _sessions.TryGetValue(tabId, out var session);
        return session;
    }

    public void RemoveSession(Guid tabId)
    {
        _requests.Remove(tabId);
        var removed = _sessions.Remove(tabId);
        _articleCache.Remove(tabId);
        if (removed)
        {
            SessionChanged?.Invoke(this, tabId);
        }
    }

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
