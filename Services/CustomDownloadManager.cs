using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

public sealed class CustomDownloadManager
{
    // In-memory tracker for download items - the Downloads page UI reads from
    // _downloadStore (persisted JSON), but the handler and UI can use this
    // to keep state in sync (pause/resume/cancel) without touching the file system.
    private readonly ConcurrentDictionary<string, DownloadItem> _items = new();

    public IReadOnlyDictionary<string, DownloadItem> Items => _items;

    // Add a download item to the tracker (called when a web page initiates a download)
    public void Add(DownloadItem item)
    {
        _items[item.Id] = item;
    }

    // Remove a download item from the tracker
    public void Remove(string id)
    {
        _items.TryRemove(id, out _);
    }

    // Mark a download as paused
    public void Pause(string id)
    {
        if (_items.TryGetValue(id, out var item) && item.State == DownloadState.InProgress)
        {
            item.State = DownloadState.Paused;
        }
    }

    // Mark a download as resumed
    public void Resume(string id)
    {
        if (_items.TryGetValue(id, out var item) && item.State == DownloadState.Paused)
        {
            item.State = DownloadState.InProgress;
        }
    }

    // Mark a download as canceled
    public void Cancel(string id)
    {
        if (_items.TryGetValue(id, out var item))
        {
            item.State = DownloadState.Cancelled;
            _items.TryRemove(id, out _);
        }
    }

    // Update received bytes and speed - called from WebView2 progress events or the handler
    public void UpdateProgress(string id, long receivedBytes, long totalBytes)
    {
        if (_items.TryGetValue(id, out var item))
        {
            item.ReceivedBytes = receivedBytes;
            item.TotalBytes = totalBytes;
            if (totalBytes > 0 && receivedBytes > 0)
            {
                // Speed can be calculated by the UI from receivedBytes over time;
                // this just stores the values for the UI to read.
            }
        }
    }
}
