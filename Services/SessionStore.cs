using System.Diagnostics;
using System.IO;
using System.Text.Json;
using StrideBrowser.Helpers;

namespace StrideBrowser.Services;

/// <summary>
/// Persists session state (open tabs) to disk for restore-on-startup.
/// </summary>
public sealed class SessionStore : ISessionStore
{
    private static readonly string FilePath = AppPaths.SessionFile;

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

    public void Save(IEnumerable<(string Url, string Title, bool IsPinned, bool IsActive)> tabs)
    {
        try
        {
            var entries = tabs.Select(t => new SessionEntry(t.Url, t.Title, t.IsPinned, t.IsActive)).ToList();
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(entries, JsonOpts);
            AtomicFileWriter.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SessionStore.Save failed: {ex.Message}");
        }
    }
}
