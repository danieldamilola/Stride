using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using UmberBrowser.Models;

namespace UmberBrowser.Services.Downloads;

/// <summary>Tracks item subscriptions from collection changes without searching retained history.</summary>
internal sealed class DownloadCollectionObserver : IDisposable
{
    private readonly ObservableCollection<DownloadItem> _source;
    private readonly HashSet<DownloadItem> _observed = [];
    private readonly PropertyChangedEventHandler _itemChanged;
    private readonly Action<NotifyCollectionChangedEventArgs> _collectionChanged;
    private bool _disposed;

    public DownloadCollectionObserver(ObservableCollection<DownloadItem> source,
        PropertyChangedEventHandler itemChanged, Action<NotifyCollectionChangedEventArgs> collectionChanged)
    {
        _source = source;
        _itemChanged = itemChanged;
        _collectionChanged = collectionChanged;
        foreach (var item in source) Observe(item);
        source.CollectionChanged += Changed;
    }

    private void Observe(DownloadItem item)
    {
        if (_observed.Add(item)) item.PropertyChanged += _itemChanged;
    }

    private void Forget(DownloadItem item)
    {
        if (_observed.Remove(item)) item.PropertyChanged -= _itemChanged;
    }

    private void Changed(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_disposed) return;
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in _observed.ToArray()) Forget(item);
            foreach (var item in _source) Observe(item);
        }
        else if (args.Action != NotifyCollectionChangedAction.Move)
        {
            if (args.OldItems is not null) foreach (DownloadItem item in args.OldItems) Forget(item);
            if (args.NewItems is not null) foreach (DownloadItem item in args.NewItems) Observe(item);
        }
        _collectionChanged(args);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.CollectionChanged -= Changed;
        foreach (var item in _observed) item.PropertyChanged -= _itemChanged;
        _observed.Clear();
    }
}


