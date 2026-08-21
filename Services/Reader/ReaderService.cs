using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Models.Reader;

namespace StrideBrowser.Services.Reader;

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
    private readonly TabEngine _engine;
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
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _extractor = extractor;
        _sanitizer = sanitizer;
        _renderer = renderer;
        _engine = engine;
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
        var url = _engine.GetTabUrl(tabId) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url) || InternalUrls.IsInternal(url))
        {
            throw new InvalidOperationException("Reader mode is not available for internal pages");
        }

        // If already in reader, return existing
        if (_sessions.TryGetValue(tabId, out var existing) && existing.IsInReader && existing.Current is not null)
        {
            return existing.Current;
        }

        var extractTask = _extractor.ExtractAsync(tabId);
        var timeoutTask = Task.Delay(4000);
        var completed = await Task.WhenAny(extractTask, timeoutTask);
        if (completed == timeoutTask)
            throw new TimeoutException("Reader extraction timed out. The page may have been hibernated or is not responding.");
        var article = await extractTask;
        var baseUrl = url;
        var sanitized = _sanitizer.Sanitize(article.ContentHtml ?? string.Empty, baseUrl);

        var rendered = _renderer.Render(article, sanitized, _options, baseUrl);
        var content = new ReaderContent(article.Title ?? "Reader", rendered, baseUrl, DateTime.UtcNow);

        // Write only into per-tab session keyed by the tabId the call was made with
        var session = new ReaderSession(tabId, IsInReader: true, Current: content, OriginalUrl: url, ScrollOffset: 0);
        _sessions[tabId] = session;
        _articleCache[tabId] = article;
        SessionChanged?.Invoke(this, tabId);
        return content;
    }

    public Task ExitReaderAsync(Guid tabId)
    {
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

        // If options changed, re-render from cached article without re-extracting
        if (_articleCache.TryGetValue(tabId, out var cached))
        {
            var url = _engine.GetTabUrl(tabId) ?? session.OriginalUrl ?? string.Empty;
            var sanitized = _sanitizer.Sanitize(cached.ContentHtml ?? string.Empty, url);
            var rendered = _renderer.Render(cached, sanitized, _options, url);
            var refreshed = new ReaderContent(cached.Title ?? session.Current.Title, rendered, url, DateTime.UtcNow);
            var updated = session with { Current = refreshed };
            _sessions[tabId] = updated;
            SessionChanged?.Invoke(this, tabId);
            return refreshed;
        }

        // Fallback: re-extract
        return await EnterReaderAsync(tabId);
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
        foreach (var id in activeIds)
        {
            _ = RefreshAsync(id);
        }

        return Task.CompletedTask;
    }

    public ReaderSession? GetSession(Guid tabId)
    {
        _sessions.TryGetValue(tabId, out var session);
        return session;
    }

    public void RemoveSession(Guid tabId)
    {
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
