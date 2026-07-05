using System.Collections.ObjectModel;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

public sealed class DownloadStore : IDownloadStore
{
    public ObservableCollection<DownloadItem> Items { get; } = new();

    public void Add(DownloadItem item)
    {
        Items.Insert(0, item);
    }

    public void Remove(string id)
    {
        var item = Get(id);
        if (item != null)
        {
            Items.Remove(item);
        }
    }

    public void ClearCompleted()
    {
        var toRemove = Items.Where(i => i.State == DownloadState.Completed || i.State == DownloadState.Cancelled || i.State == DownloadState.Failed).ToList();
        foreach (var item in toRemove)
        {
            Items.Remove(item);
        }
    }

    public DownloadItem? Get(string id) => Items.FirstOrDefault(i => i.Id == id);
}
