using System.Text.Json;
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
        return JsonSerializer.Serialize(items.Select(ToPageRow), Options);
    }

    public static DownloadPageRow ToPageRow(DownloadItem item) => new(
        item.Id, item.FileName, item.Url, item.StartedAt, item.CompletedAt,
        item.ReceivedBytes, item.TotalBytes, item.State, item.ErrorMessage,
        item.IsStarting, item.IsMissing, item.IsIndeterminate, item.CanPause,
        item.CanResume, item.CanRestart, item.CanCancel, item.CanOpen,
        item.RetryLabel, item.StatusText, item.SourceHost, item.SizeText,
        item.SpeedText, item.EtaText, item.FileIconDataUri);

    // A value record lets the bounded sync compare presentation before serializing.
    public sealed record DownloadPageRow(string Id, string FileName, string Url,
        DateTime StartedAt, DateTime? CompletedAt, long ReceivedBytes, long TotalBytes,
        DownloadState State, string ErrorMessage, bool IsStarting, bool IsMissing,
        bool IsIndeterminate, bool CanPause, bool CanResume, bool CanRestart,
        bool CanCancel, bool CanOpen, string RetryLabel, string StatusText,
        string SourceHost, string SizeText, string SpeedText, string EtaText, string FileIcon);

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
