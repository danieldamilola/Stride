using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace UmberBrowser.Services;

/// <summary>Serializes cache file changes within a profile and prunes only its PNG files.</summary>
internal sealed class FaviconDiskCache
{
    internal const int MaxEntries = 2_000;
    internal const long MaxBytes = 64L * 1024 * 1024;
    internal static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectoryGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _directory;
    private readonly SemaphoreSlim _gate;
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly TimeSpan _maxAge;
    private readonly object _scheduleLock = new();
    private DateTime _nextPrune;

    internal FaviconDiskCache(string directory, int maxEntries = MaxEntries, long maxBytes = MaxBytes, TimeSpan? maxAge = null)
    {
        _directory = Path.GetFullPath(directory);
        _gate = DirectoryGates.GetOrAdd(_directory, _ => new SemaphoreSlim(1, 1));
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
        _maxAge = maxAge ?? MaxAge;
        SchedulePrune();
    }

    internal void SchedulePrune()
    {
        lock (_scheduleLock)
        {
            if (DateTime.UtcNow < _nextPrune) return;
            _nextPrune = DateTime.UtcNow.AddMinutes(5);
        }
        _ = PruneAsync();
    }

    internal Task<(bool Found, byte[]? Bytes)> ReadAsync(string host) => Task.Run(async () =>
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = Path.Combine(_directory, $"{host}.png");
            if (!File.Exists(path)) return (false, (byte[]?)null);
            try
            {
                // A successful cache lookup counts as recent use even without a WPF decoder.
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
                return (true, await FaviconLoader.ReadBoundedBytesAsync(stream).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Favicon cache read failed: {ex}");
                return (true, (byte[]?)null);
            }
        }
        finally { _gate.Release(); }
    });

    internal Task WriteAsync(string host, byte[] bytes) => Task.Run(async () =>
    {
        if (bytes.Length == 0 || bytes.Length > FaviconLoader.MaxIconBytes) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(_directory);
            temporary = Path.Combine(_directory, $"{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
            File.Move(temporary, Path.Combine(_directory, $"{host}.png"), true);
            temporary = null;
            // Writes enforce the budget on the worker, after the atomic rename. The gate
            // also covers other loader instances that use this profile directory.
            PruneCore(DateTime.UtcNow);
        }
        catch (Exception ex) { Trace.WriteLine($"Favicon cache write failed: {ex}"); }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) { Trace.WriteLine($"Favicon cache temporary cleanup failed: {ex}"); }
            }
            _gate.Release();
        }
    });

    internal Task PruneAsync() => Task.Run(async () =>
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { PruneCore(DateTime.UtcNow); }
        catch (Exception ex) { Trace.WriteLine($"Favicon cache pruning failed: {ex}"); }
        finally { _gate.Release(); }
    });

    private void PruneCore(DateTime now)
    {
        if (!Directory.Exists(_directory)) return;
        var files = new DirectoryInfo(_directory).EnumerateFiles("*.png", SearchOption.TopDirectoryOnly)
            .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderByDescending(file => file.LastWriteTimeUtc).ToList();
        var count = 0;
        long bytes = 0;
        foreach (var file in files)
        {
            try
            {
                if (now - file.LastWriteTimeUtc > _maxAge || file.Length > FaviconLoader.MaxIconBytes || count >= _maxEntries || file.Length > _maxBytes - bytes)
                {
                    file.Delete();
                    continue;
                }
                count++;
                bytes += file.Length;
            }
            catch (Exception ex)
            {
                // Locked or removed files can be retried in the next pass.
                Trace.WriteLine($"Favicon cache entry pruning failed: {ex}");
            }
        }
    }
}


