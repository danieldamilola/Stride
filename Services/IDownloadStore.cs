using System.Collections.ObjectModel;
using UmberBrowser.Models;

namespace UmberBrowser.Services;

public interface IDownloadStore
{
    ObservableCollection<DownloadItem> Items { get; }
    void Add(DownloadItem item);
    void Remove(string id);
    void ClearCompleted();
    void ClearAll();
    DownloadItem? Get(string id);
    Task<bool> FlushAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    IDisposable BeginUpdate() => EmptyDownloadUpdate.Instance;
}

internal sealed class EmptyDownloadUpdate : IDisposable
{
    public static readonly EmptyDownloadUpdate Instance = new();
    public void Dispose() { }
}
