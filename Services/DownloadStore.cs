using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

public sealed class DownloadStore : IDownloadStore
{
    private static readonly string FilePath = AppPaths.DownloadsFile;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ObservableCollection<DownloadItem> Items { get; } = new();

    public DownloadStore()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var items = JsonSerializer.Deserialize<List<DownloadItem>>(json, JsonOpts);
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        // WebView2 cannot resume downloads after the app is completely closed.
                        // Mark any incomplete downloads (InProgress or Paused) as Failed.
                        if (item.State == DownloadState.InProgress || item.State == DownloadState.Paused)
                            item.State = DownloadState.Failed;
                        
                        item.PropertyChanged += Item_PropertyChanged;
                        Items.Add(item);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"DownloadStore Load failed: {ex.Message}");
        }
    }

    private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadItem.State))
        {
            Save();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(Items.ToList(), JsonOpts);
            AtomicFileWriter.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"DownloadStore Save failed: {ex.Message}");
        }
    }

    public void Add(DownloadItem item)
    {
        item.PropertyChanged += Item_PropertyChanged;
        Items.Insert(0, item);
        Save();
    }

    public void Remove(string id)
    {
        var item = Get(id);
        if (item != null)
        {
            item.PropertyChanged -= Item_PropertyChanged;
            Items.Remove(item);
            Save();
        }
    }

    public void ClearCompleted()
    {
        var toRemove = Items.Where(i => i.State == DownloadState.Completed || i.State == DownloadState.Cancelled || i.State == DownloadState.Failed).ToList();
        foreach (var item in toRemove)
        {
            item.PropertyChanged -= Item_PropertyChanged;
            Items.Remove(item);
        }
        if (toRemove.Any())
            Save();
    }

    public DownloadItem? Get(string id) => Items.FirstOrDefault(i => i.Id == id);
}
