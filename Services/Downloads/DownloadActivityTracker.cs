using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using StrideBrowser.Models;

namespace StrideBrowser.Services.Downloads;

/// <summary>Keeps toolbar progress limited to active transfers rather than completed history.</summary>
internal sealed class DownloadActivityTracker : IDisposable
{
    private readonly ObservableCollection<DownloadItem> _source;
    private readonly HashSet<DownloadItem> _active = [];
    private readonly DownloadCollectionObserver _observer;
    public IReadOnlyCollection<DownloadItem> ActiveItems => _active;
    public event Action? Changed;

    public DownloadActivityTracker(ObservableCollection<DownloadItem> source)
    {
        _source = source;
        foreach (var item in source) Update(item);
        _observer = new(source, ItemChanged, CollectionChanged);
    }

    private static bool IsActive(DownloadItem item) => item.State == DownloadState.InProgress || item.IsStarting;
    private void Update(DownloadItem item)
    {
        if (IsActive(item)) _active.Add(item);
        else _active.Remove(item);
    }

    private void CollectionChanged(NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Move) return;
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            _active.Clear();
            foreach (var item in _source) Update(item);
        }
        else
        {
            if (args.OldItems is not null) foreach (DownloadItem item in args.OldItems) _active.Remove(item);
            if (args.NewItems is not null) foreach (DownloadItem item in args.NewItems) Update(item);
        }
        Changed?.Invoke();
    }

    private void ItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not DownloadItem item) return;
        if (args.PropertyName is not (nameof(DownloadItem.State) or nameof(DownloadItem.IsStarting)
            or nameof(DownloadItem.ReceivedBytes) or nameof(DownloadItem.TotalBytes) or null or "")) return;
        var wasActive = _active.Contains(item);
        Update(item);
        if (wasActive || _active.Contains(item)) Changed?.Invoke();
    }

    public void Dispose()
    {
        _observer.Dispose();
        _active.Clear();
        Changed = null;
    }
}
