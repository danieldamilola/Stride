using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>Collection mutations and model reads belong to the UI context. Only immutable records reach the writer.</summary>
public sealed class DownloadStore : IDownloadStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, DownloadRecord> _records = [];
    private readonly Dictionary<string, DownloadItem> _byId = [];
    private readonly List<string> _order = [];
    private readonly ResetCollection _items = new();
    private readonly OrderedStorePersistence<List<DownloadRecord>> _persistence;
    private int _updateDepth;
    private bool _updateDirty;

    public ObservableCollection<DownloadItem> Items => _items;

    public DownloadStore() : this(null) { }

    public DownloadStore(string? filePath, Action<string, string>? write = null)
    {
        var path = filePath ?? AppPaths.DownloadsFile;
        _items.CollectionChanged += ItemsChanged;
        _persistence = new(_lock, () => _order.Select(id => _records[id]).ToList(),
            records => (write ?? AtomicFileWriter.WriteAllText)(path, JsonSerializer.Serialize(records, DownloadJson.Options)));
        try
        {
            if (!File.Exists(path)) return;
            var items = JsonSerializer.Deserialize<List<DownloadItem>>(File.ReadAllText(path), DownloadJson.Options);
            if (items is null) return;
            foreach (var item in items)
            {
                if (item.State == DownloadState.InProgress) item.State = DownloadState.Paused;
                _records[item.Id] = DownloadRecord.Capture(item);
                _byId[item.Id] = item;
                _order.Add(item.Id);
                item.PropertyChanged += ItemChanged;
                _items.Add(item);
            }
        }
        catch (Exception ex) { Trace.WriteLine($"DownloadStore load failed: {ex}"); }
    }

    private void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        lock (_lock)
        {
            if (args.Action == NotifyCollectionChangedAction.Move) return;
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                _byId.Clear();
                foreach (var item in _items) _byId[item.Id] = item;
                return;
            }
            if (args.OldItems is not null)
                foreach (DownloadItem item in args.OldItems)
                    if (_byId.TryGetValue(item.Id, out var current) && ReferenceEquals(item, current)) _byId.Remove(item.Id);
            if (args.NewItems is not null)
                foreach (DownloadItem item in args.NewItems) _byId[item.Id] = item;
        }
    }

    private void ItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not DownloadItem item) return;
        // Ignore computed presentation notifications. Progress updates refresh one cached record
        // without scheduling a write. The next state change or flush persists those bytes.
        var persist = args.PropertyName is nameof(DownloadItem.State) or nameof(DownloadItem.ErrorMessage)
            or nameof(DownloadItem.FilePath) or nameof(DownloadItem.FileName) or nameof(DownloadItem.RetryAllowed);
        if (!persist && args.PropertyName is not (nameof(DownloadItem.ReceivedBytes) or nameof(DownloadItem.TotalBytes)
            or nameof(DownloadItem.SpeedBytesPerSec) or nameof(DownloadItem.EstimatedTimeRemaining))) return;
        lock (_lock)
        {
            if (!_records.ContainsKey(item.Id)) return;
            _records[item.Id] = DownloadRecord.Capture(item);
            if (persist) Changed();
        }
    }

    private void Changed()
    {
        if (_updateDepth > 0) _updateDirty = true;
        else _persistence.Changed();
    }

    public Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        // Flush is called on the collection's owning context, before that dispatcher exits.
        // Capture fields without notifications, such as CompletedAt, once at this boundary.
        lock (_lock)
        {
            foreach (var item in Items) _records[item.Id] = DownloadRecord.Capture(item);
            _persistence.Changed();
        }
        return _persistence.FlushAsync(cancellationToken);
    }

    public IDisposable BeginUpdate()
    {
        lock (_lock) _updateDepth++;
        return new UpdateScope(this);
    }

    private sealed class UpdateScope(DownloadStore owner) : IDisposable
    {
        private DownloadStore? _owner = owner;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is not { } store) return;
            lock (store._lock)
            {
                if (--store._updateDepth != 0 || !store._updateDirty) return;
                store._updateDirty = false;
                store._persistence.Changed();
            }
        }
    }

    public void Add(DownloadItem item)
    {
        lock (_lock)
        {
            if (_byId.ContainsKey(item.Id)) return;
            _records[item.Id] = DownloadRecord.Capture(item);
            _byId[item.Id] = item;
            _order.Insert(0, item.Id);
            item.PropertyChanged += ItemChanged;
            Items.Insert(0, item);
            Changed();
        }
    }

    public void Remove(string id)
    {
        lock (_lock)
        {
            if (Get(id) is not { } item) return;
            item.PropertyChanged -= ItemChanged;
            _records.Remove(id);
            _byId.Remove(id);
            _order.Remove(id);
            Items.Remove(item);
            Changed();
        }
    }

    public void ClearCompleted() => ClearWhere(item => item.State is DownloadState.Completed or DownloadState.Cancelled or DownloadState.Failed);
    public void ClearAll() => ClearWhere(_ => true);

    private void ClearWhere(Func<DownloadItem, bool> remove)
    {
        lock (_lock)
        {
            var kept = new List<DownloadItem>();
            foreach (var item in Items)
            {
                if (!remove(item)) { kept.Add(item); continue; }
                item.PropertyChanged -= ItemChanged;
                _records.Remove(item.Id);
                _byId.Remove(item.Id);
            }
            if (kept.Count == Items.Count) return;
            _order.Clear();
            _order.AddRange(kept.Select(item => item.Id));
            _items.ReplaceWith(kept);
            Changed();
        }
    }

    public DownloadItem? Get(string id)
    {
        lock (_lock) return _byId.GetValueOrDefault(id);
    }

    private sealed class ResetCollection : ObservableCollection<DownloadItem>
    {
        public void ReplaceWith(List<DownloadItem> kept)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (var item in kept) Items.Add(item);
            OnPropertyChanged(new(nameof(Count)));
            OnPropertyChanged(new("Item[]"));
            OnCollectionChanged(new(NotifyCollectionChangedAction.Reset));
        }
    }

    private sealed record DownloadRecord(string Id, string FileName, string Url, string FilePath, DateTime StartedAt,
        DateTime? CompletedAt, string? ETag, string? LastModified, string ErrorMessage, bool RetryAllowed,
        long ReceivedBytes, long TotalBytes, long SpeedBytesPerSec, TimeSpan? EstimatedTimeRemaining, DownloadState State)
    {
        public static DownloadRecord Capture(DownloadItem item) => new(item.Id, item.FileName, item.Url, item.FilePath,
            item.StartedAt, item.CompletedAt, item.ETag, item.LastModified, item.ErrorMessage, item.RetryAllowed,
            item.ReceivedBytes, item.TotalBytes, item.SpeedBytesPerSec, item.EstimatedTimeRemaining, item.State);
    }
}
