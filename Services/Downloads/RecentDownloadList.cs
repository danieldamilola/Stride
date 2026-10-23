using UmberBrowser.Models;

namespace UmberBrowser.Services.Downloads;

public static class RecentDownloadList
{
    public static IReadOnlyList<DownloadItem> Select(IEnumerable<DownloadItem> items, DateTime utcNow)
    {
        var cutoff = utcNow.AddHours(-24);
        var ordered = items.OrderByDescending(item => item.CompletedAt ?? item.StartedAt).ToArray();
        var recent = ordered.Where(item => (item.CompletedAt ?? item.StartedAt) >= cutoff).Take(5).ToHashSet();
        // An ongoing transfer remains reachable even if it started before the recent window.
        return ordered.Where(item => recent.Contains(item) || item.IsStarting || item.State == DownloadState.InProgress ||
            item.State == DownloadState.Paused && item.CanResumeNative).ToArray();
    }
}
