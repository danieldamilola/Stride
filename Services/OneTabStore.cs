using System.Diagnostics;
using System.IO;
using System.Text.Json;
using UmberBrowser.Helpers;
using UmberBrowser.Models;

namespace UmberBrowser.Services;

/// <summary>Owns saved tabs in memory and coalesces ordinary mutations on one disk writer.</summary>
public sealed class OneTabStore : IOneTabStore
{
    private readonly string _path;
    private readonly Action<string, string> _write;
    private readonly object _lock = new();
    private readonly OrderedStorePersistence<List<OneTabGroup>> _persistence;
    private List<OneTabGroup> _groups = [];
    private bool _loaded;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public OneTabStore() : this(null) { }

    public OneTabStore(string? filePath, Action<string, string>? write = null)
    {
        _path = filePath ?? AppPaths.OneTabFile;
        _write = write ?? AtomicFileWriter.WriteAllText;
        _persistence = new(_lock, () => _groups.Select(Clone).ToList(), Write);
    }

    private static OneTabGroup Clone(OneTabGroup group) => new()
    {
        Id = group.Id, Name = group.Name, SavedAt = group.SavedAt,
        Tabs = group.Tabs.Select(tab => new OneTabEntry(tab.Url, tab.Title, tab.FaviconUrl, tab.SavedAt)
        {
            IsStarred = tab.IsStarred
        }).ToList()
    };

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _groups = File.Exists(_path)
            ? JsonSerializer.Deserialize<List<OneTabGroup>>(File.ReadAllText(_path), JsonOptions) ?? []
            : [];
        _loaded = true;
    }

    private void Write(List<OneTabGroup> groups) => _write(_path, JsonSerializer.Serialize(groups, JsonOptions));

    public Task<bool> FlushAsync(CancellationToken cancellationToken = default) => _persistence.FlushAsync(cancellationToken);

    public List<OneTabGroup> Load()
    {
        lock (_lock)
        {
            try { EnsureLoaded(); return _groups.Select(Clone).ToList(); }
            catch (Exception ex) { Trace.WriteLine($"OneTabStore.Load failed: {ex}"); return []; }
        }
    }

    private void Mutate(Action mutation)
    {
        lock (_lock)
        {
            try
            {
                EnsureLoaded();
                mutation();
                _persistence.Changed();
            }
            catch (Exception ex) { Trace.WriteLine($"OneTabStore mutation failed: {ex}"); }
        }
    }

    public void Save(List<OneTabGroup> groups)
    {
        lock (_lock)
        {
            _groups = groups.Select(Clone).ToList();
            _loaded = true;
            _persistence.Changed();
        }
    }
    public void AddGroup(OneTabGroup group) => Mutate(() => _groups.Add(Clone(group)));
    public void RemoveGroup(string groupId) => Mutate(() => _groups.RemoveAll(group => group.Id == groupId));

    public void RenameGroup(string groupId, string name) => Mutate(() =>
    {
        if (_groups.FirstOrDefault(group => group.Id == groupId) is { } group) group.Name = name;
    });

    public void RemoveTab(string groupId, string url) => Mutate(() =>
    {
        var group = _groups.FirstOrDefault(group => group.Id == groupId);
        if (group is null) return;
        var index = group.Tabs.FindIndex(tab => tab.Url == url);
        if (index >= 0) group.Tabs.RemoveAt(index);
        if (group.Tabs.Count == 0) _groups.Remove(group);
    });

    public void ToggleStar(string groupId, int tabIndex) => Mutate(() =>
    {
        var group = _groups.FirstOrDefault(group => group.Id == groupId);
        if (group is null || tabIndex < 0 || tabIndex >= group.Tabs.Count) return;
        group.Tabs[tabIndex].IsStarred = !group.Tabs[tabIndex].IsStarred;
    });

    public void ReorderGroup(int oldIndex, int newIndex) => Mutate(() =>
    {
        if (oldIndex < 0 || oldIndex >= _groups.Count || newIndex < 0 || newIndex >= _groups.Count) return;
        var group = _groups[oldIndex];
        _groups.RemoveAt(oldIndex);
        _groups.Insert(newIndex, group);
    });

    public void ReorderTab(string groupId, int oldIndex, int newIndex) => Mutate(() =>
    {
        var group = _groups.FirstOrDefault(group => group.Id == groupId);
        if (group is null || oldIndex < 0 || oldIndex >= group.Tabs.Count || newIndex < 0 || newIndex >= group.Tabs.Count) return;
        var tab = group.Tabs[oldIndex];
        group.Tabs.RemoveAt(oldIndex);
        group.Tabs.Insert(newIndex, tab);
    });

    /// <summary>Returns a group only after its atomic write succeeds, before callers close live tabs.</summary>
    public OneTabGroup? SaveAll(IEnumerable<BrowserTab> tabs)
    {
        var saveable = tabs
            .Where(tab => !string.IsNullOrEmpty(tab.Url) && !InternalUrls.IsInternal(tab.Url)
                && !tab.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                && !tab.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            .Select(tab => new OneTabEntry(tab.Url, string.IsNullOrEmpty(tab.Title) ? tab.Url : tab.Title, null, DateTime.UtcNow))
            .ToList();
        if (saveable.Count == 0) return null;
        var now = DateTime.UtcNow;
        var group = new OneTabGroup
        {
            Name = now.ToLocalTime().ToString("MMM d, h:mm tt"), SavedAt = now, Tabs = saveable
        };
        var saved = _persistence.WriteNow(() =>
        {
            try
            {
                EnsureLoaded();
                _groups.Add(group);
                Write(_groups);
                return true;
            }
            catch (Exception ex)
            {
                _groups.Remove(group);
                Trace.WriteLine($"OneTabStore.SaveAll failed: {ex}");
                return false;
            }
        });
        return saved ? Clone(group) : null;
    }
}
