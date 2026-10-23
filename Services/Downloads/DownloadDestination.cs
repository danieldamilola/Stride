using System.IO;
using UmberBrowser.Models;
namespace UmberBrowser.Services.Downloads;

public static class DownloadDestination
{
    public static string Choose(string proposed, Func<string, bool> unavailable)
    {
        var directory = Path.GetDirectoryName(proposed) ?? throw new ArgumentException("Download needs a folder.");
        var name = Path.GetFileNameWithoutExtension(proposed);
        var extension = Path.GetExtension(proposed);
        var candidate = proposed;
        for (var suffix = 1; unavailable(candidate); suffix++)
            candidate = Path.Combine(directory, $"{name} ({suffix}){extension}");
        return candidate;
    }
    public static string Reserve(string proposed, IDownloadStore store, string? restartingId = null)
        => Choose(proposed, candidate => File.Exists(candidate) || File.Exists(candidate + ".crdownload") ||
            store.Items.Any(item => item.Id != restartingId && item.CanCancel &&
                string.Equals(item.FilePath, candidate, StringComparison.OrdinalIgnoreCase)));
}
