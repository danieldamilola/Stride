using System.Diagnostics;
using System.IO;
using System.Text.Json;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>Persists browsing history to disk.</summary>
public sealed class HistoryStore : IHistoryStore
{
    private readonly string FilePath;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly object _lock = new();
    private readonly OrderedStorePersistence<List<HistoryEntry>> _persistence;
    private List<HistoryEntry> _entries = [];
    private bool _loaded;

    public HistoryStore() : this(null) { }

    public HistoryStore(string? filePath, Action<string, string>? write = null)
    {
        FilePath = filePath ?? AppPaths.HistoryFile;
        _persistence = new(_lock, () => _entries.Select(Clone).ToList(),
            entries => (write ?? AtomicFileWriter.WriteAllText)(FilePath, JsonSerializer.Serialize(entries, JsonOpts)));
    }

    private static HistoryEntry Clone(HistoryEntry entry) => new()
    {
        Url = entry.Url, Title = entry.Title, VisitedAt = entry.VisitedAt
    };

    public Task<bool> FlushAsync(CancellationToken cancellationToken = default) => _persistence.FlushAsync(cancellationToken);

    private void EnsureLoaded()
    {
        if (_loaded) return;
        if (!File.Exists(FilePath)) { _loaded = true; return; }
        var json = File.ReadAllText(FilePath);
        _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? [];
        _loaded = true;
    }

    /// <summary>Adds a history entry, caps at 10 000 entries, and auto-saves.</summary>
    public void Add(string url, string title)
    {
        lock (_lock)
        {
            try
            {
                EnsureLoaded();

                _entries.Add(new HistoryEntry
                {
                    Url = url,
                    Title = title,
                    VisitedAt = DateTime.UtcNow
                });

                if (_entries.Count > 10_000)
                    _entries.RemoveRange(0, _entries.Count - 10_000);

                _persistence.Changed();
            }
            catch (Exception ex) { Trace.WriteLine($"HistoryStore.Add failed: {ex.Message}"); }
        }
    }

    /// <summary>Loads history from disk, sorted by most recent first.</summary>
    public List<HistoryEntry> Load()
    {
        lock (_lock)
        {
            try
            {
                EnsureLoaded();
                return _entries.OrderByDescending(e => e.VisitedAt).Select(Clone).ToList();
            }
            catch (Exception ex) { Trace.WriteLine($"HistoryStore.Load failed: {ex.Message}"); return []; }
        }
    }

    /// <summary>Searches history by URL or title (case-insensitive).</summary>
    public List<HistoryEntry> Search(string query)
    {
        lock (_lock)
        {
            try
            {
                EnsureLoaded();
                return _entries
                    .Where(e => (e.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                (e.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Select(Clone).ToList();
            }
            catch (Exception ex) { Trace.WriteLine($"HistoryStore.Search failed: {ex.Message}"); return []; }
        }
    }

    /// <summary>Deletes the history file and clears in-memory entries.</summary>
    /// <returns>True when the file no longer exists and in-memory entries were cleared.</returns>
    public bool Clear()
    {
        return _persistence.WriteNow(() =>
        {
            try
            {
                _entries.Clear();
                _loaded = true;
                if (File.Exists(FilePath)) File.Delete(FilePath);
                return !File.Exists(FilePath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"HistoryStore.Clear failed: {ex.Message}");
                return false;
            }
        }, persistFailure: true);
    }

    /// <summary>Returns the top N most visited URLs grouped by domain.</summary>
    public List<HistoryEntry> GetFrequentSites(int count = 8)
    {
        lock (_lock)
        {
            try
            {
                EnsureLoaded();
                return _entries
                    .Where(e => Uri.TryCreate(e.Url, UriKind.Absolute, out _))
                    .GroupBy(e => new Uri(e.Url).Host, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .Take(count)
                    .Select(g => Clone(g.OrderByDescending(e => e.VisitedAt).First()))
                    .ToList();
            }
            catch (Exception ex) { Trace.WriteLine($"HistoryStore.GetFrequentSites failed: {ex.Message}"); return []; }
        }
    }

}
