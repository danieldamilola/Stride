using System.Collections.ObjectModel;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

public interface IDownloadStore
{
    ObservableCollection<DownloadItem> Items { get; }
    void Add(DownloadItem item);
    void Remove(string id);
    void ClearCompleted();
    void ClearAll();
    DownloadItem? Get(string id);
}
