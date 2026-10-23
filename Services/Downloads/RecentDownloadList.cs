using UmberBrowser.Models;

namespace UmberBrowser.Services.Downloads;

public static class RecentDownloadList
{
    public static IReadOnlyList<DownloadItem> Select(IEnumerable<DownloadItem> items, DateTime utcNow)
    {
        var cutoff = utcNow.AddHours(-24);
        var recent = new List<DownloadItem>(6);
        var ongoing = new List<DownloadItem>();
        foreach (var item in items)
        {
            var date = item.CompletedAt ?? item.StartedAt;
            if (date >= cutoff)
            {
                var index = recent.FindIndex(existing => (existing.CompletedAt ?? existing.StartedAt) < date);
                if (index < 0) recent.Add(item);
                else recent.Insert(index, item);
                if (recent.Count > 5) recent.RemoveAt(5);
            }
            // An ongoing transfer remains reachable even outside the recent window.
            if (item.IsStarting || item.State == DownloadState.InProgress || item.State == DownloadState.Paused && item.CanResumeNative)
                ongoing.Add(item);
        }
        return recent.Concat(ongoing).Distinct().OrderByDescending(item => item.CompletedAt ?? item.StartedAt).ToArray();
    }
}
