using System.Text.Json;
using System.Text.Json.Nodes;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// Shared JSON contract for download items. The persisted store, the page
/// renderer, and the live sync must serialize with the same casing so the
/// page JS and the on-disk format never disagree.
/// </summary>
public static class DownloadJson
{
    public static string SerializeForPage(IEnumerable<DownloadItem> items)
    {
        var rows = new JsonArray();
        foreach (var item in items)
        {
            var row = JsonSerializer.SerializeToNode(item, Options)!.AsObject();
            row["fileIcon"] = item.FileIconDataUri;
            row["isMissing"] = item.IsMissing;
            rows.Add(row);
        }
        return rows.ToJsonString();
    }

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
