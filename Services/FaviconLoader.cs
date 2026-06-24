using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// Loads and caches favicons using WebView2's native API (primary)
/// with DuckDuckGo fallback. Persists to disk for cross-session caching.
///
/// Chrome/Firefox approach:
///   1. Browser engine detects &lt;link rel="icon"&gt; or /favicon.ico
///   2. Downloads + decodes the image
///   3. Stores in a persistent SQLite DB (Favicons / favicons.sqlite)
///   4. Loads from cache first on revisit
///
/// Stride approach (this class):
///   1. WebView2 FaviconChanged event → GetFaviconAsync() for PNG data
///   2. Saves PNG to disk cache (%LocalAppData%\StrideBrowser\favicons\{host}.png)
///   3. On next visit, loads from disk first (no network call)
///   4. Falls back to DuckDuckGo API only when WebView2 has no favicon
/// </summary>
public sealed class FaviconLoader
{
    internal const int MaxCacheEntries = 200;

    // In-memory LRU cache for the current session
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, (LinkedListNode<string> node, BitmapImage? bitmap)> _cache = new();
    private readonly Lock _cacheLock = new();

    // Prevents duplicate disk reads / downloads for the same host
    private readonly Dictionary<string, Task<BitmapImage?>> _inflightLoads = new();
    private readonly Lock _inflightLock = new();

    private static readonly System.Net.Http.HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Disk cache directory for persisted favicon PNGs.</summary>
    private static string CacheDir => AppPaths.FaviconCacheDir;

    /// <summary>
    /// Loads a favicon for the given URL.
    /// Priority: memory cache → disk cache → DuckDuckGo fallback.
    /// WebView2's native API is used separately via HandleFaviconChanged.
    /// </summary>
    public async Task<BitmapImage?> LoadAsync(string url)
    {
        if (InternalUrls.IsInternal(url) || InternalUrls.IsDataOrBlank(url))
            return null;

        var host = ExtractHost(url);
        if (host is null) return null;

        // 1. Check memory cache
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(host, out var entry))
            {
                _lruOrder.Remove(entry.node);
                _lruOrder.AddFirst(entry.node);
                return entry.bitmap;
            }
        }

        // 2. Deduplicate concurrent loads for the same host
        Task<BitmapImage?> loadTask;
        lock (_inflightLock)
        {
            if (!_inflightLoads.TryGetValue(host, out loadTask!))
            {
                loadTask = LoadFromDiskOrFallbackAsync(host);
                _inflightLoads[host] = loadTask;
            }
        }

        var bitmap = await loadTask;

        lock (_inflightLock) { _inflightLoads.Remove(host); }

        AddToMemoryCache(host, bitmap);
        return bitmap;
    }

    /// <summary>
    /// Called when WebView2 fires FaviconChanged. Uses GetFaviconAsync()
    /// to get the real favicon directly from the browser engine, then
    /// saves it to disk and updates the in-memory cache.
    /// This is the Chrome/Firefox approach — engine-provided, not 3rd party.
    /// </summary>
    public async Task<BitmapImage?> HandleFaviconChangedAsync(WebView2 wv, string url)
    {
        if (wv.CoreWebView2 is null) return null;
        if (InternalUrls.IsInternal(url) || InternalUrls.IsDataOrBlank(url))
            return null;

        var host = ExtractHost(url);
        if (host is null) return null;

        var faviconUri = wv.CoreWebView2.FaviconUri;
        if (string.IsNullOrEmpty(faviconUri)) return null;

        try
        {
            using var stream = await wv.CoreWebView2.GetFaviconAsync(
                CoreWebView2FaviconImageFormat.Png);
            if (stream is null || stream.Length == 0) return null;

            // Read into byte array
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (bytes.Length == 0) return null;

            // Save to disk cache
            await SaveToDiskAsync(host, bytes);

            // Create BitmapImage on UI thread
            var app = System.Windows.Application.Current;
            if (app is null) return null;

            var bitmap = await app.Dispatcher.InvokeAsync(() => CreateBitmap(bytes));

            AddToMemoryCache(host, bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Tries disk cache first, then falls back to DuckDuckGo.</summary>
    private async Task<BitmapImage?> LoadFromDiskOrFallbackAsync(string host)
    {
        // 2. Try disk cache
        var diskBitmap = await LoadFromDiskAsync(host);
        if (diskBitmap is not null) return diskBitmap;

        // 3. Fallback: DuckDuckGo icon service
        return await DownloadFromDuckDuckGoAsync(host);
    }

    private static async Task<BitmapImage?> LoadFromDiskAsync(string host)
    {
        var path = Path.Combine(CacheDir, $"{host}.png");
        if (!File.Exists(path)) return null;

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.Length == 0) return null;

            var app = System.Windows.Application.Current;
            if (app is null) return null;

            return await app.Dispatcher.InvokeAsync(() => CreateBitmap(bytes));
        }
        catch { return null; }
    }

    private static async Task<BitmapImage?> DownloadFromDuckDuckGoAsync(string host)
    {
        try
        {
            var app = System.Windows.Application.Current;
            if (app is null) return null;

            var bytes = await HttpClient.GetByteArrayAsync(
                $"https://icons.duckduckgo.com/ip3/{host}.ico");

            // Persist to disk for next launch
            await SaveToDiskAsync(host, bytes);

            return await app.Dispatcher.InvokeAsync(() => CreateBitmap(bytes));
        }
        catch { return null; }
    }

    private static async Task SaveToDiskAsync(string host, byte[] data)
    {
        try
        {
            var path = Path.Combine(CacheDir, $"{host}.png");
            await File.WriteAllBytesAsync(path, data);
        }
        catch { /* Non-critical */ }
    }

    private static BitmapImage CreateBitmap(byte[] bytes)
    {
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        bmp.BeginInit();
        bmp.StreamSource = ms;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.DecodePixelWidth = 32;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void AddToMemoryCache(string host, BitmapImage? bitmap)
    {
        lock (_cacheLock)
        {
            if (_cache.ContainsKey(host)) return;

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

    private static string? ExtractHost(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            return string.IsNullOrEmpty(host) ? null : host;
        }
        catch (UriFormatException) { return null; }
    }
}
