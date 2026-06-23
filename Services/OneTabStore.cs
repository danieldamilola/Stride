using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SpurBrowser.Helpers;
using SpurBrowser.Models;

namespace SpurBrowser.Services;

/// <summary>
/// Persists <see cref="OneTabGroup"/> data to a local JSON file.
/// </summary>
public sealed class OneTabStore : IOneTabStore
{
    private static readonly string StorePath = AppPaths.OneTabFile;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly Lock _lock = new();

    public List<OneTabGroup> Load()
    {
        lock (_lock)
        {
            try { return LoadUnsafe(); }
            catch (Exception ex) { Trace.WriteLine($"OneTabStore.Load failed: {ex.Message}"); return []; }
        }
    }

    public void Save(List<OneTabGroup> groups)
    {
        lock (_lock)
        {
            try { SaveUnsafe(groups); }
            catch (Exception ex) { Trace.WriteLine($"OneTabStore.Save failed: {ex.Message}"); }
        }
    }

    public void AddGroup(OneTabGroup group)
    {
        lock (_lock)
        {
            try { var groups = LoadUnsafe(); groups.Add(group); SaveUnsafe(groups); }
            catch (Exception ex) { Trace.WriteLine($"OneTabStore.AddGroup failed: {ex.Message}"); }
        }
    }

    public void RemoveGroup(string groupId)
    {
        lock (_lock)
        {
            try { var groups = LoadUnsafe(); groups.RemoveAll(g => g.Id == groupId); SaveUnsafe(groups); }
            catch (Exception ex) { Trace.WriteLine($"OneTabStore.RemoveGroup failed: {ex.Message}"); }
        }
    }

    public void MoveTab(string fromGroupId, string toGroupId, int tabIndex)
    {
        lock (_lock)
        {
            try
            {
                var groups = LoadUnsafe();
                var from = groups.FirstOrDefault(g => g.Id == fromGroupId);
                var to = groups.FirstOrDefault(g => g.Id == toGroupId);
                if (from is null || to is null) { Trace.WriteLine("OneTabStore.MoveTab: source or target group not found."); return; }
                if (tabIndex < 0 || tabIndex >= from.Tabs.Count) { Trace.WriteLine($"OneTabStore.MoveTab: tabIndex {tabIndex} out of range."); return; }
                var tab = from.Tabs[tabIndex];
                from.Tabs.RemoveAt(tabIndex);
                to.Tabs.Add(tab);
                SaveUnsafe(groups);
            }
            catch (Exception ex) { Trace.WriteLine($"OneTabStore.MoveTab failed: {ex.Message}"); }
        }
    }

    public void RemoveTab(string groupId, string url)
    {
        lock (_lock)
        {
            try
            {
                var groups = LoadUnsafe();
                var group = groups.FirstOrDefault(g => g.Id == groupId);
                if (group is null) return;
                group.Tabs.RemoveAll(t => t.Url == url);
                if (group.Tabs.Count == 0) groups.Remove(group);
                SaveUnsafe(groups);
            }
            catch (Exception ex) { Trace.WriteLine($"OneTabStore.RemoveTab failed: {ex.Message}"); }
        }
    }

    public void ToggleStar(string groupId, int tabIndex)
    {
        lock (_lock)
        {
            try
            {
                var groups = LoadUnsafe();
                var group = groups.FirstOrDefault(g => g.Id == groupId);
                if (group is null || tabIndex < 0 || tabIndex >= group.Tabs.Count) return;
                group.Tabs[tabIndex].IsStarred = !group.Tabs[tabIndex].IsStarred;
                SaveUnsafe(groups);
            }
            catch (Exception ex) { Trace.WriteLine($"OneTabStore.ToggleStar failed: {ex.Message}"); }
        }
    }

    public void ReorderGroup(int oldIndex, int newIndex)
    {
        lock (_lock)
        {
            try
            {
                var groups = LoadUnsafe();
                if (oldIndex < 0 || oldIndex >= groups.Count || newIndex < 0 || newIndex >= groups.Count) return;
                var group = groups[oldIndex];
                groups.RemoveAt(oldIndex);
                groups.Insert(newIndex, group);
                SaveUnsafe(groups);
            }
            catch (Exception ex) { Trace.WriteLine($"OneTabStore.ReorderGroup failed: {ex.Message}"); }
        }
    }

    public void ReorderTab(string groupId, int oldIndex, int newIndex)
    {
        lock (_lock)
        {
            try
            {
                var groups = LoadUnsafe();
                var group = groups.FirstOrDefault(g => g.Id == groupId);
                if (group is null || oldIndex < 0 || oldIndex >= group.Tabs.Count || newIndex < 0 || newIndex >= group.Tabs.Count) return;
                var tab = group.Tabs[oldIndex];
                group.Tabs.RemoveAt(oldIndex);
                group.Tabs.Insert(newIndex, tab);
                SaveUnsafe(groups);
            }
            catch (Exception ex) { Trace.WriteLine($"OneTabStore.ReorderTab failed: {ex.Message}"); }
        }
    }

    // ── Internal helpers (caller must hold _lock) ────────────────────

    private List<OneTabGroup> LoadUnsafe()
    {
        if (!File.Exists(StorePath)) return [];
        var json = File.ReadAllText(StorePath);
        return JsonSerializer.Deserialize<List<OneTabGroup>>(json, JsonOptions) ?? [];
    }

    private void SaveUnsafe(List<OneTabGroup> groups)
    {
        var dir = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(groups, JsonOptions);
        AtomicFileWriter.WriteAllText(StorePath, json);
    }
}
