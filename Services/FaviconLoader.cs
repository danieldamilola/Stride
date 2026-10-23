using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using UmberBrowser.Helpers;
using UmberBrowser.Models;

namespace UmberBrowser.Services;

/// <summary>Where a favicon is being asked for, which decides if the network is allowed.</summary>
public enum FaviconLoadScope
{
    /// <summary>A page tab, including a restored tab. May use the third-party fallback.</summary>
    PageNavigation,

    /// <summary>
    /// A history, autocomplete or new tab shortcut row. These may use the memory and disk
    /// caches and nothing else. Typing is not consent to publish every host the user has
    /// ever visited, so these rows never reach the network path.
    /// </summary>
    LocalListRow,
}

/// <summary>
/// Loads and caches favicons using WebView2's native API (primary)
/// with an opt-in DuckDuckGo fallback. Persists to disk for cross-session caching.
///
/// Chrome/Firefox approach:
///   1. Browser engine detects &lt;link rel="icon"&gt; or /favicon.ico
///   2. Downloads + decodes the image
///   3. Stores in a persistent SQLite DB (Favicons / favicons.sqlite)
///   4. Loads from cache first on revisit
///
/// Umber approach (this class):
///   1. WebView2 FaviconChanged event → GetFaviconAsync() for PNG data
///   2. Saves PNG to disk cache (%LocalAppData%\UmberBrowser\favicons\{host}.png)
///   3. On next visit, loads from disk first (no network call)
///   4. Asks the DuckDuckGo icon service only when the user opted in, only for a page
///      navigation, and only for a host <see cref="FaviconRequestPolicy"/> allows
/// </summary>
public sealed class FaviconLoader
{
    internal const int MaxCacheEntries = 200;

    private readonly BrowserSettings _settings;
    private readonly HttpClient _http;
    private readonly string _cacheDir;

    public FaviconLoader(BrowserSettings settings)
        : this(settings, new HttpClient { Timeout = TimeSpan.FromSeconds(8) }, AppPaths.FaviconCacheDir) { }

    /// <summary>
    /// Test seam: an isolated cache folder and an HTTP client the test controls, so a
    /// real request path can be exercised without a network or the real profile.
    /// </summary>
    public FaviconLoader(BrowserSettings settings, HttpClient http, string cacheDir)
    {
        _settings = settings;
        _http = http;
        _cacheDir = cacheDir;
    }

    // In-memory LRU cache for the current session
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, (LinkedListNode<string> node, BitmapImage? bitmap)> _cache = new();
    private readonly Lock _cacheLock = new();

    // Prevents duplicate disk reads / downloads. Keyed by scope and by whether this caller
    // could make a request at all, so a cache-only row lookup or a non-web URL never
    // joins a network-capable task, and never hands one its answer.
    private readonly Dictionary<LoadKey, Task<BitmapImage?>> _inflightLoads = new();
    private readonly Lock _inflightLock = new();

    /// <summary>Identifies a load well enough that two loads may share work only if both could send.</summary>
    private readonly record struct LoadKey(string Host, FaviconLoadScope Scope, bool NetworkCapable);

    /// <summary>
    /// Loads a favicon for the given URL.
    /// Priority: memory cache → disk cache → the opt-in DuckDuckGo fallback, which is
    /// only reached from a page navigation.
    /// WebView2's native API is used separately via HandleFaviconChangedAsync.
    /// </summary>
    public async Task<BitmapImage?> LoadAsync(string url, FaviconLoadScope scope = FaviconLoadScope.PageNavigation)
    {
        if (InternalUrls.IsInternal(url) || InternalUrls.IsDataOrBlank(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return null;

        var host = uri.Host;

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

        // The scheme is decided on, not thrown away with the rest of the URL. A file,
        // ftp, ws or gopher URL carrying a hostname is not a page on the internet, so it
        // must never reach the network path, and it must not share one either.
        var decision = FaviconRequestPolicy.Evaluate(uri);
        var networkCapable = scope == FaviconLoadScope.PageNavigation
            && _settings.FaviconProxyEnabled
            && decision.Allowed;

        // 2. Deduplicate concurrent loads. The key carries whether this caller could make
        //    a request at all, so a cache-only caller cannot join a network-capable task
        //    and then receive its answer, and a network-capable caller cannot be handed a
        //    cache-only answer that happened to start first.
        var key = new LoadKey(host, scope, networkCapable);
        Task<BitmapImage?> loadTask;
        lock (_inflightLock)
        {
            if (!_inflightLoads.TryGetValue(key, out loadTask!))
            {
                loadTask = LoadFromDiskOrFallbackAsync(host, scope, networkCapable, decision);
                _inflightLoads[key] = loadTask;
            }
        }

        var bitmap = await loadTask;

        lock (_inflightLock) { _inflightLoads.Remove(key); }

        // A miss is not remembered. Caching the empty answer would make one cache-only
        // lookup suppress every later native or network load for that host.
        if (bitmap is not null) AddToMemoryCache(host, bitmap);
        return bitmap;
    }

    /// <summary>
    /// Called when WebView2 fires FaviconChanged. Uses GetFaviconAsync()
    /// to get the real favicon directly from the browser engine, then
    /// saves it to disk and updates the in-memory cache.
    /// This is the Chrome/Firefox approach - engine-provided, not 3rd party.
    /// </summary>
    public async Task<BitmapImage?> HandleFaviconChangedAsync(CoreWebView2 core, string url)
    {
        if (core is null) return null;
        if (InternalUrls.IsInternal(url) || InternalUrls.IsDataOrBlank(url))
            return null;

        var host = ExtractHost(url);
        if (host is null) return null;

        var faviconUri = core.FaviconUri;
        if (string.IsNullOrEmpty(faviconUri)) return null;

        try
        {
            using var stream = await core.GetFaviconAsync(
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
            var bitmap = await DecodeOnUiThreadAsync(bytes);
            if (bitmap is null) return null;

            AddToMemoryCache(host, bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Tries disk cache first, then the opt-in third-party fallback.</summary>
    private async Task<BitmapImage?> LoadFromDiskOrFallbackAsync(
        string host, FaviconLoadScope scope, bool networkCapable, FaviconRequestDecision decision)
    {
        // 1. Try disk cache
        var cached = await LoadFromDiskAsync(host);
        // A host whose icon is already on disk never needs a third-party request, even
        // when the bytes could not be decoded this time. Treating a decode failure as a
        // cache miss is what would turn a warm cache back into a disclosure.
        if (cached.Found) return cached.Bitmap;

        // 2. Rows and non-web schemes stop here. Both are decided by the key this task was
        //    created under, so neither can start a request by joining someone else's task.
        if (!networkCapable)
        {
            if (scope != FaviconLoadScope.PageNavigation)
                return null;
            System.Diagnostics.Trace.WriteLine(
                $"FaviconLoader no third-party icon request for '{host}': {decision.Reason}");
            return null;
        }

        // 3. The send boundary. Everything below this line sends a host to a third party,
        //    so the user's consent is re-read here rather than earlier. The disk read
        //    above awaited, and the user may have turned the fallback off in that time.
        if (!_settings.FaviconProxyEnabled) return null;

        // The request happens before any WPF decoding, so it does not depend on a running
        // Application, and the bytes go to the disk cache the way a native icon does.
        var bytes = await FetchIconBytesAsync(decision.Host);
        if (bytes is null || bytes.Length == 0) return null;

        await SaveToDiskAsync(host, bytes);
        return (await LoadFromDiskAsync(host)).Bitmap;
    }

    /// <summary>
    /// Reads the cached file for a host. Found says whether the file was there, which is
    /// a different question from whether it decoded.
    /// </summary>
    private async Task<(bool Found, BitmapImage? Bitmap)> LoadFromDiskAsync(string host)
    {
        var path = Path.Combine(_cacheDir, $"{host}.png");
        if (!File.Exists(path)) return (false, null);

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.Length == 0) return (true, null);
            return (true, await DecodeOnUiThreadAsync(bytes));
        }
        catch { return (true, null); }
    }

    private async Task<byte[]?> FetchIconBytesAsync(string allowedHost)
    {
        try
        {
            return await _http.GetByteArrayAsync(FaviconRequestPolicy.BuildIconUrl(allowedHost));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"FaviconLoader icon request failed: {ex.Message}");
            return null;
        }
    }

    private async Task SaveToDiskAsync(string host, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var path = Path.Combine(_cacheDir, $"{host}.png");
            await File.WriteAllBytesAsync(path, data);
        }
        catch { /* Non-critical */ }
    }

    /// <summary>
    /// Decodes PNG bytes into a frozen bitmap on the UI thread. Returns null when there is
    /// no Application, which is the case in tests and in a headless run. The caller has
    /// already saved the bytes by this point, so nothing is lost.
    /// </summary>
    private static async Task<BitmapImage?> DecodeOnUiThreadAsync(byte[] bytes)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return null;
        return await app.Dispatcher.InvokeAsync(() => CreateBitmap(bytes));
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
        if (bitmap is null) return;

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

    /// <summary>
    /// The host an engine supplied icon is filed under. This path never contacts the
    /// third party, so it needs only the host and not the scheme.
    /// </summary>
    private static string? ExtractHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return null;
        return uri.Host;
    }
}
