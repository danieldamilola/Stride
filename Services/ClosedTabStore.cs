using System.Diagnostics;
using System.IO;
using System.Text.Json;
using StrideBrowser.Helpers;

namespace StrideBrowser.Services;

/// <summary>
/// Persists the recently-closed tab stack to disk so Ctrl+Shift+T keeps working across
/// restarts. Without this the stack lives only in memory and every closed tab is lost the
/// moment the browser exits, which matters most when session restore is off because nothing
/// else brings the tabs back.
/// </summary>
public sealed class ClosedTabStore : IClosedTabStore
{
    private readonly string _filePath;

    public ClosedTabStore() : this(AppPaths.ClosedTabsFile) { }

    /// <summary>Test seam so a round trip can run against a scratch file.</summary>
    public ClosedTabStore(string filePath) => _filePath = filePath;

    /// <summary>Matches the cap TabEngine keeps in memory.</summary>
    public const int MaxEntries = 50;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public List<ClosedTabEntry> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return [];

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<ClosedTabEntry>>(json, JsonOpts) ?? [];
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ClosedTabStore.Load failed: {ex.Message}");
            return [];
        }
    }

    public void Save(IEnumerable<ClosedTabEntry> tabs)
    {
        try
        {
            var entries = tabs.TakeLast(MaxEntries).ToList();
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(entries, JsonOpts);
            AtomicFileWriter.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ClosedTabStore.Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Drops the persisted stack. Used when clearing data on exit so a closed tab cannot
    /// be resurrected after the user asked for everything to go.
    /// </summary>
    public void Clear()
    {
        try { if (File.Exists(_filePath)) File.Delete(_filePath); }
        catch (Exception ex) { Trace.WriteLine($"ClosedTabStore.Clear failed: {ex.Message}"); }
    }
}