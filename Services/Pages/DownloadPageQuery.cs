using UmberBrowser.Models;
using UmberBrowser.Services;

namespace UmberBrowser.Services.Pages;

/// <summary>Caches searchable metadata and keeps page selection separate from progress updates.</summary>
public sealed class DownloadPageQuery
{
    public const int PageSize = 50;
    private sealed record Entry(DownloadItem Item, string Search);
    private Entry[] _index = [];
    private DownloadItem[] _matches = [];
    private string? _query;
    private bool _dirty = true;

    public void Invalidate() => _dirty = true;

    public Selection Select(IEnumerable<DownloadItem> source, string query, int page)
    {
        query = query.Trim().ToLowerInvariant();
        if (_dirty)
        {
            _index = source.OrderByDescending(item => item.StartedAt)
                .Select(item => new Entry(item, (item.FileName + " " + item.Url).ToLowerInvariant())).ToArray();
        }
        if (_dirty || _query != query)
        {
            _matches = _index.Where(entry => query.Length == 0 || entry.Search.Contains(query, StringComparison.Ordinal))
                .Select(entry => entry.Item).ToArray();
            _query = query;
            _dirty = false;
        }
        page = Math.Clamp(page, 0, Math.Max(0, (_matches.Length - 1) / PageSize));
        return new(page, _matches.Length, _matches.Skip(page * PageSize).Take(PageSize).ToArray());
    }

    public sealed record Selection(int Page, int Total, DownloadItem[] Items);
}




