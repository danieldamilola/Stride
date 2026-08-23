using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine.Handlers;

public class DownloadMessageHandler : IWebMessageHandler
{
    private readonly TabEngine _engine;
    private readonly IDownloadStore _downloadStore;
    private readonly CustomDownloadManager _customDownloadManager;

    public DownloadMessageHandler(TabEngine engine, IDownloadStore downloadStore, CustomDownloadManager customDownloadManager)
    {
        _engine = engine;
        _downloadStore = downloadStore;
        _customDownloadManager = customDownloadManager;
    }

    public IEnumerable<MessageRoute> GetRoutes()
    {
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadOpen, HandleDownloadOpen);
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadFolder, HandleDownloadFolder);
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadCancel, HandleDownloadCancel);
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadPause, HandleDownloadPause);
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadResume, HandleDownloadResume);
        yield return MessageRoute.Prefix(WebMessagePrefix.DownloadRequest, HandleDownloadRequest);
        yield return MessageRoute.Exact(WebMessagePrefix.DownloadClear, HandleDownloadClear);
        yield return MessageRoute.Exact(WebMessagePrefix.DownloadClearAll, HandleDownloadClearAll);
        yield return MessageRoute.Exact(WebMessagePrefix.DownloadRequestSync, HandleDownloadSync);
    }

    private Task HandleDownloadOpen(string id)
    {
        var item = _downloadStore.Get(id);
        if (item?.State == DownloadState.Completed && System.IO.File.Exists(item.FilePath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.FilePath) { UseShellExecute = true });
            }
            catch
            {
                // If there's no default application associated with the file or it fails to launch, fallback to opening the folder
                _ = HandleDownloadFolder(id);
            }
        }
        return Task.CompletedTask;
    }

    private Task HandleDownloadFolder(string id)
    {
        var item = _downloadStore.Get(id);
        if (item?.State == DownloadState.Completed && System.IO.File.Exists(item.FilePath))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
        }
        return Task.CompletedTask;
    }

    private Task HandleDownloadCancel(string id)
    {
        var item = _downloadStore.Items.FirstOrDefault(d => d.Id == id);
        if (item != null)
        {
            item.State = DownloadState.Cancelled;
        }
        return Task.CompletedTask;
    }

    private Task HandleDownloadRequest(string url)
    {
        var uri = new Uri(url);
        var fileName = System.IO.Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrEmpty(fileName)) fileName = "download";
        
        var downloadsFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var filePath = System.IO.Path.Combine(downloadsFolder, fileName);
        
        // Ensure unique filename
        int count = 1;
        while (System.IO.File.Exists(filePath))
        {
            var nameOnly = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var ext = System.IO.Path.GetExtension(fileName);
            filePath = System.IO.Path.Combine(downloadsFolder, $"{nameOnly} ({count}){ext}");
            count++;
        }
        
        var item = new DownloadItem
        {
            Url = url,
            FileName = System.IO.Path.GetFileName(filePath),
            FilePath = filePath,
            State = DownloadState.InProgress
        };
        _downloadStore.Add(item);
        _customDownloadManager.Add(item); // WebView2 handles the download natively;
        // the manager tracks the item and fires events; Downloads.html refreshes via sync.
        return Task.CompletedTask;
    }

    private Task HandleDownloadPause(string id)
    {
        var item = _downloadStore.Get(id);
        if (item?.State == DownloadState.InProgress)
        {
            item.State = DownloadState.Paused;
        }
        return Task.CompletedTask;
    }

    private Task HandleDownloadResume(string id)
    {
        var item = _downloadStore.Get(id);
        if (item?.State == DownloadState.Paused)
        {
            item.State = DownloadState.InProgress;
        }
        return Task.CompletedTask;
    }

    private Task HandleDownloadClear()
    {
        _downloadStore.ClearCompleted();
        return Task.CompletedTask;
    }

    private Task HandleDownloadClearAll()
    {
        // Cancel anything still running so WebView2 stops the transfer, then drop the whole list.
        foreach (var item in _downloadStore.Items.Where(i => i.State is DownloadState.InProgress or DownloadState.Paused))
        {
            item.State = DownloadState.Cancelled;
        }
        _downloadStore.ClearAll();
        return Task.CompletedTask;
    }

    private async Task HandleDownloadSync()
    {
        if (_engine.ActiveTab?.Url != InternalUrls.Downloads) return;
        var wv = _engine.GetCoreWebView2();
        if (wv == null) return;
        
        var json = JsonSerializer.Serialize(_downloadStore.Items.ToList(), DownloadJson.Options);
        // Frame the payload with the token so the page can verify it came from the host.
        wv.PostWebMessageAsString(_engine.IpcToken + ":downloads:" + json);
        await Task.CompletedTask;
    }
}