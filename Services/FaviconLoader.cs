using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using SpurBrowser.Models;

namespace SpurBrowser.Services;

/// <summary>Loads and caches favicons from DuckDuckGo's icon service.</summary>
public sealed class FaviconLoader
{
    private const string IconBaseUrl = "https://icons.duckduckgo.com/ip3/";
    internal const int MaxCacheEntries = 200;

    // LRU cache: LinkedList tracks access order, Dictionary provides O(1) lookup
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, (LinkedListNode<string> node, BitmapImage? bitmap)> _cache = new();
    private readonly Lock _cacheLock = new();

    // Prevents duplicate downloads for the same host
    private readonly ConcurrentDictionary<string, Task<BitmapImage?>> _inflightDownloads = new();

    /// <summary>Loads a favicon for the given URL, returning a cached copy if available.</summary>
    public async Task<BitmapImage?> LoadAsync(string url)
    {
        if (InternalUrls.IsInternal(url) || InternalUrls.IsDataOrBlank(url))
            return null;

        var host = ExtractHost(url);
        if (host is null) return null;

        // Check cache first
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(host, out var entry))
            {
                // Move to front (most recently used)
                _lruOrder.Remove(entry.node);
                _lruOrder.AddFirst(entry.node);
                return entry.bitmap;
            }
        }

        // Deduplicate in-flight downloads for the same host
        var bitmap = await _inflightDownloads.GetOrAdd(host, h => DownloadFaviconAsync(h));
        _inflightDownloads.TryRemove(host, out _);

        AddToCache(host, bitmap);
        return bitmap;
    }

    private static string? ExtractHost(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            return string.IsNullOrEmpty(host) ? null : host;
        }
        catch (UriFormatException) { return null; }
    }

    private static readonly System.Net.Http.HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static async Task<BitmapImage?> DownloadFaviconAsync(string host)
    {
        try
        {
            var app = System.Windows.Application.Current;
            if (app is null) return null;

            var bytes = await HttpClient.GetByteArrayAsync($"{IconBaseUrl}{host}.ico");

            return await app.Dispatcher.InvokeAsync(() =>
            {
                var bmp = new BitmapImage();
                using var ms = new System.IO.MemoryStream(bytes);
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = 32;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            });
        }
        catch { return null; }
    }

    private void AddToCache(string host, BitmapImage? bitmap)
    {
        lock (_cacheLock)
        {
            // If already cached (race condition), just update access order
            if (_cache.ContainsKey(host))
                return;

            // Evict least recently used entries if at capacity
            while (_cache.Count >= MaxCacheEntries && _lruOrder.Last is not null)
            {
                var lruKey = _lruOrder.Last.Value;
                _lruOrder.RemoveLast();
                _cache.Remove(lruKey);
            }

            var node = _lruOrder.AddFirst(host);
            _cache[host] = (node, bitmap);
        }
    }
}
