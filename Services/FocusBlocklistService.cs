using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// Downloads, caches, and parses massive blocklists (500k+ domains) for Focus Mode.
/// Optimized to run in the background and minimize memory overhead via HashSet.
/// </summary>
public sealed class FocusBlocklistService
{
    private static readonly HttpClient _http = new();

    // Hardcoded URLs provided by user context
    private static readonly string[] ListUrls =
    {
        "https://raw.githubusercontent.com/blocklistproject/Lists/master/porn.txt",
        "https://raw.githubusercontent.com/Telegreat/blocklist/master/telegram.txt"
    };

    private readonly ISettingsStore _settingsStore;
    private HashSet<string> _compiledDomains = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoaded;
    private readonly object _loadLock = new();

    public FocusBlocklistService(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;

        // Load domains asynchronously if Focus mode is currently locked
        var settings = _settingsStore.Load();
        if (settings.FocusLocked)
        {
            _ = InitializeAsync();
        }
    }

    /// <summary>
    /// Checks if a domain or its parent domains are blocked.
    /// Fast O(1) lookups taking less than 1 microsecond.
    /// </summary>
    public bool IsBlocked(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;

        var settings = _settingsStore.Load();
        if (!settings.FocusLocked) return false;

        // Check custom user domains first (from settings)
        if (!string.IsNullOrWhiteSpace(settings.FocusDomains))
        {
            var custom = settings.FocusDomains
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim());
            if (FocusDomainMatcher.MatchesCustomDomain(host, custom))
                return true;
        }

        // Trigger initialization if locked but not loaded
        if (!_isLoaded)
        {
            _ = InitializeAsync();
            return false; // Will block on subsequent requests once loaded
        }

        // Fast host splitting lookup against the massive 500k HashSet
        if (FocusDomainMatcher.MatchesBlockedDomain(host, _compiledDomains, out var matchedSuffix))
        {
            Trace.WriteLine($"FocusBlocklistService: Blocked {host} because of match {matchedSuffix}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Forces a background initialize if Focus Mode is locked.
    /// Safe to call multiple times.
    /// </summary>
    private bool _isInitializing = false;

    public Task InitializeAsync()
    {
        return Task.Run(async () =>
        {
            lock (_loadLock)
            {
                if (_isLoaded || _isInitializing) return;
                _isInitializing = true;
            }

            try
            {
                await EnsureCacheAsync();
                LoadFromCache();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"FocusBlocklistService Init error: {ex.Message}");
            }
            finally
            {
                lock (_loadLock)
                {
                    _isInitializing = false;
                }
            }
        });
    }

    private void LoadFromCache()
    {
        var tempSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in ListUrls)
        {
            var fileName = GetCacheFileName(url);
            var filePath = Path.Combine(AppPaths.FocusCacheDir, fileName);

            if (File.Exists(filePath))
            {
                var lines = File.ReadLines(filePath);
                foreach (var line in lines)
                {
                    var clean = line.Trim();
                    // Skip comments and empty lines
                    if (string.IsNullOrEmpty(clean) || clean.StartsWith('#')) continue;

                    // Some lists like BlocklistProject have "0.0.0.0 badsite.com" format sometimes,
                    // or just "badsite.com". Handle simple cases:
                    var parts = clean.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    var domain = parts.LastOrDefault();

                    if (!string.IsNullOrEmpty(domain) && !domain.StartsWith('#'))
                    {
                        tempSet.Add(domain);
                    }
                }
            }
        }

        lock (_loadLock)
        {
            _compiledDomains = tempSet;
            _isLoaded = true;
        }

        Trace.WriteLine($"Loaded {_compiledDomains.Count} focus domains into memory.");
    }

    private async Task EnsureCacheAsync()
    {
        // 7-day cache expiration
        var expiration = TimeSpan.FromDays(7);

        foreach (var url in ListUrls)
        {
            var fileName = GetCacheFileName(url);
            var filePath = Path.Combine(AppPaths.FocusCacheDir, fileName);

            var needsDownload = !File.Exists(filePath) ||
                                (DateTime.UtcNow - File.GetLastWriteTimeUtc(filePath)) > expiration;

            if (needsDownload)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.FocusCacheDir);
                    Trace.WriteLine($"Downloading focus list: {url}");
                    var content = await _http.GetStringAsync(url);
                    await File.WriteAllTextAsync(filePath, content);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Failed to download {url}: {ex.Message}");
                }
            }
        }
    }

    private static string GetCacheFileName(string url)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(url);
        var hash = md5.ComputeHash(bytes);
        return Convert.ToHexString(hash) + ".txt";
    }
}
