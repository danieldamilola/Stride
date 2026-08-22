using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StrideBrowser.Models.Reader;

namespace StrideBrowser.Services.Reader;

public sealed class ReaderSessionStore : IReaderStateQuery
{
    private readonly Dictionary<Guid, ReaderSession> _sessions = new();
    private readonly Dictionary<Guid, ArticleResult> _articleCache = new();

    public event EventHandler<Guid>? SessionChanged;

    public bool IsActive(Guid tabId)
    {
        return _sessions.TryGetValue(tabId, out var s) && s.IsInReader;
    }

    public Task ExitAsync(Guid tabId)
    {
        if (_sessions.TryGetValue(tabId, out var session) && session.IsInReader)
        {
            var exited = session with { IsInReader = false };
            _sessions[tabId] = exited;
            SessionChanged?.Invoke(this, tabId);
        }
        return Task.CompletedTask;
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

    public ReaderSession? GetSession(Guid tabId)
    {
        _sessions.TryGetValue(tabId, out var session);
        return session;
    }

    public void SetSession(Guid tabId, ReaderSession session, ArticleResult article)
    {
        _sessions[tabId] = session;
        _articleCache[tabId] = article;
        SessionChanged?.Invoke(this, tabId);
    }

    public bool TryGetCachedArticle(Guid tabId, out ArticleResult article)
    {
        return _articleCache.TryGetValue(tabId, out article!);
    }

    public IReadOnlyList<Guid> GetActiveReaderTabIds()
    {
        return _sessions.Where(kvp => kvp.Value.IsInReader).Select(kvp => kvp.Key).ToList();
    }
}
