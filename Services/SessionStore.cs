using System.Diagnostics;
using System.IO;
using System.Text.Json;
using UmberBrowser.Helpers;

namespace UmberBrowser.Services;

/// <summary>
/// Persists session state (open tabs) to disk for restore-on-startup.
/// </summary>
public sealed class SessionStore : ISessionStore
{
    private static readonly string DefaultFilePath = AppPaths.SessionFile;

    private readonly string _filePath;

    /// <summary>
    /// Replaces the durable write when set. Null, the default, writes for real.
    /// This exists so a test can make a write fail, or fail once and then succeed,
    /// without needing a full disk. The product never sets it.
    /// </summary>
    private readonly Action<string, string>? _write;

    /// <summary>Test seam: pass an explicit path to keep a run off the real profile.</summary>
    public SessionStore() : this(null) { }

    public SessionStore(string? filePath) : this(filePath, null) { }

    /// <summary>Full test seam, including a controllable writer fault.</summary>
    public SessionStore(string? filePath, Action<string, string>? write)
    {
        _filePath = filePath ?? DefaultFilePath;
        _write = write;
    }

    private string FilePath => _filePath;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public List<SessionEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<SessionEntry>>(json, JsonOpts) ?? [];
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SessionStore.Load failed: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Index of the tab to activate on restore: the saved active entry,
    /// clamped into range. Falls back to the first tab when none is marked
    /// (e.g. sessions written before the active tab was persisted).
    /// Pure logic, covered by unit tests.
    /// </summary>
    public static int ResolveActiveIndex(IReadOnlyList<SessionEntry> entries)
    {
        if (entries.Count == 0) return 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].IsActive) return i;
        }
        return 0;
    }

    /// <summary>
    /// Replaces the snapshot on disk and reports whether the file now holds these
    /// rows. The answer is the write's own: a locked file, a full disk or a path
    /// that cannot be written returns false, and the previous snapshot is left on
    /// disk because the atomic write only renames over the target once the new
    /// bytes are down. Never throws, because a window close cannot be taken down
    /// by a snapshot that could not be written.
    /// </summary>
    public bool TrySave(IEnumerable<(string Url, string Title, bool IsPinned, bool IsActive)> tabs)
    {
        try
        {
            var entries = tabs.Select(t => new SessionEntry(t.Url, t.Title, t.IsPinned, t.IsActive)).ToList();
            var json = JsonSerializer.Serialize(entries, JsonOpts);
            WriteText(json);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SessionStore snapshot write failed: {ex.Message}");
            return false;
        }
    }

    public void Save(IEnumerable<(string Url, string Title, bool IsPinned, bool IsActive)> tabs)
        => TrySave(tabs);

    /// <summary>
    /// Empties the snapshot. Used on exit when restore on startup is off, so
    /// turning it back on later does not bring back a session the user already
    /// ended. Only this file is written; the other stores are untouched.
    /// </summary>
    /// <returns>True when the file now holds no rows.</returns>
    public bool TryClear()
    {
        try
        {
            WriteText("[]");
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SessionStore snapshot clear failed: {ex.Message}");
            return false;
        }
    }

    public void Clear() => TryClear();

    /// <summary>
    /// Empties the snapshot and deletes it, for a clear-on-exit request. Only this
    /// file is affected; history, downloads, the one tab list and the closed tab
    /// history are separate files and are not touched.
    /// </summary>
    public void ClearAndDelete()
    {
        TryClear();
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            var leftover = FilePath + ".tmp";
            if (File.Exists(leftover)) File.Delete(leftover);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SessionStore.ClearAndDelete failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The one place bytes reach the file, so every write is judged on the same
    /// boundary: the target is only replaced after the new contents are written.
    /// </summary>
    private void WriteText(string json)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        if (_write is not null) _write(FilePath, json);
        else AtomicFileWriter.WriteAllText(FilePath, json);
    }
}
