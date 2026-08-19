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
            _customDownloadManager.CancelDownload(item);
        }
        return Task.CompletedTask;
    }

    private async Task HandleDownloadRequest(string url)
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

        // Transfer runs in CustomDownloadManager so Pause/Cancel can abort it.
        await _customDownloadManager.StartDownloadAsync(item);
    }

    private Task HandleDownloadPause(string id)
    {
        var item = _downloadStore.Get(id);
        if (item?.State == DownloadState.InProgress)
        {
            _customDownloadManager.PauseDownload(item);
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
        if (_engine.ActiveTab?.Url == InternalUrls.Downloads) _engine.Reload();
        return Task.CompletedTask;
    }

    private async Task HandleDownloadSync()
    {
        if (_engine.ActiveTab?.Url != InternalUrls.Downloads) return;
        var wv = _engine.GetCoreWebView2();
        if (wv == null) return;
        
        var items = _downloadStore.Items.ToList();
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { PropertyNamingPolicy = null });
        json = json.Replace("\\", "\\\\").Replace("'", "\\'");
        
        await wv.ExecuteScriptAsync($"if (typeof updateDownloads === 'function') updateDownloads('{json}');");
    }
}
