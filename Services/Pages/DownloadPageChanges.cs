using UmberBrowser.Models;
using UmberBrowser.Services;

namespace UmberBrowser.Services.Pages;

/// <summary>Compares only the visible page and emits changed presentation rows.</summary>
public sealed class DownloadPageChanges
{
    private Dictionary<string, DownloadJson.DownloadPageRow> _rows = [];
    private string[] _ids = [];
    private string? _request;
    private int _total = -1;
    private int _page = -1;

    public Update? Create(string request, DownloadPageQuery.Selection selection)
    {
        var reset = request != _request;
        var rows = selection.Items.Select(DownloadJson.ToPageRow).ToArray();
        var ids = rows.Select(row => row.Id).ToArray();
        var changed = rows.Where(row => reset || !_rows.TryGetValue(row.Id, out var previous) || row != previous).ToArray();
        var update = reset || changed.Length != 0 || !_ids.SequenceEqual(ids) || _total != selection.Total || _page != selection.Page;
        _request = request;
        _rows = rows.ToDictionary(row => row.Id);
        _ids = ids;
        _total = selection.Total;
        _page = selection.Page;
        return update ? new(request, selection.Page, selection.Total, ids, changed,
            selection.Items.Any(item => item.IsStarting || item.State == DownloadState.InProgress)) : null;
    }

    public sealed record Update(string Request, int Page, int Total, string[] Ids,
        DownloadJson.DownloadPageRow[] Changed, bool Active);
}




