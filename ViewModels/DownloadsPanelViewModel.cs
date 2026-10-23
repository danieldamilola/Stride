using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Downloads;

namespace StrideBrowser.ViewModels;

public sealed partial class DownloadsPanelViewModel : ObservableObject, IDisposable
{
    private readonly IDownloadStore _store;
    private readonly TabEngine _engine;
    private readonly DownloadCollectionObserver _observer;
    public ObservableCollection<DownloadItem> RecentDownloads { get; } = new();
    public DownloadsPanelViewModel(IDownloadStore store, TabEngine engine)
    {
        _store = store;
        _engine = engine;
        _observer = new(store.Items, ItemChanged, _ => Refresh());
        Refresh();
    }
    public void Refresh()
    {
        var desired = RecentDownloadList.Select(_store.Items, DateTime.UtcNow);
        _ = _engine.Downloads.FileDetails.RefreshAsync(desired);
        foreach (var removed in RecentDownloads.Where(item => !desired.Contains(item)).ToArray()) RecentDownloads.Remove(removed);
        for (var index = 0; index < desired.Count; index++)
        {
            var current = RecentDownloads.IndexOf(desired[index]);
            if (current < 0) RecentDownloads.Insert(index, desired[index]);
            else if (current != index) RecentDownloads.Move(current, index);
        }
    }
    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadItem.State) or nameof(DownloadItem.IsStarting) or nameof(DownloadItem.CanResumeNative)) Refresh();
    }
    [RelayCommand] private void Pause(DownloadItem item) => _engine.Downloads.Pause(item.Id);
    [RelayCommand] private void Resume(DownloadItem item) => _engine.Downloads.Resume(item.Id);
    [RelayCommand] private void Cancel(DownloadItem item) => _engine.Downloads.Cancel(item.Id);
    [RelayCommand(AllowConcurrentExecutions = true)] private Task RetryAsync(DownloadItem item) => _engine.RetryDownloadAsync(item.Id);
    [RelayCommand] private void Open(DownloadItem item) => _engine.Downloads.Open(item.Id);
    [RelayCommand] private void Folder(DownloadItem item) => _engine.Downloads.ShowInFolder(item.Id);
    public void Dispose()
    {
        _observer.Dispose();
    }
}
