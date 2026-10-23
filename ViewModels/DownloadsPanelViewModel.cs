using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmberBrowser.Engine;
using UmberBrowser.Models;
using UmberBrowser.Services;
using UmberBrowser.Services.Downloads;

namespace UmberBrowser.ViewModels;

public sealed partial class DownloadsPanelViewModel : ObservableObject, IDisposable
{
    private readonly IDownloadStore _store;
    private readonly TabEngine _engine;
    private readonly HashSet<DownloadItem> _observed = new();
    public ObservableCollection<DownloadItem> RecentDownloads { get; } = new();
    public DownloadsPanelViewModel(IDownloadStore store, TabEngine engine)
    { _store = store; _engine = engine; _store.Items.CollectionChanged += OnChanged; Refresh(); }
    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
    public void Refresh()
    {
        foreach (var removed in _observed.Where(item => !_store.Items.Contains(item)).ToArray())
        { removed.PropertyChanged -= ItemChanged; _observed.Remove(removed); }
        foreach (var item in _store.Items)
            if (_observed.Add(item)) item.PropertyChanged += ItemChanged;
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
        _store.Items.CollectionChanged -= OnChanged;
        foreach (var item in _observed) item.PropertyChanged -= ItemChanged;
        _observed.Clear();
    }
}
