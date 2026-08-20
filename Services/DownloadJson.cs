using System.Text.Json;

namespace StrideBrowser.Services;

/// <summary>
/// Shared JSON contract for download items. The persisted store, the page
/// renderer, and the live sync must serialize with the same casing so the
/// page JS and the on-disk format never disagree.
/// </summary>
public static class DownloadJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
