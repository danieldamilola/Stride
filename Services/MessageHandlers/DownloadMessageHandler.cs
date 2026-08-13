using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;

namespace StrideBrowser.Services.MessageHandlers;

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

    public void Register(Dictionary<string, Func<string, Task>> prefixHandlers, Dictionary<string, Func<Task>> exactHandlers)
    {
        prefixHandlers[WebMessagePrefix.DownloadOpen] = HandleDownloadOpen;
        prefixHandlers[WebMessagePrefix.DownloadFolder] = HandleDownloadFolder;
        prefixHandlers[WebMessagePrefix.DownloadCancel] = HandleDownloadCancel;
        prefixHandlers[WebMessagePrefix.DownloadPause] = HandleDownloadPause;
        prefixHandlers[WebMessagePrefix.DownloadResume] = HandleDownloadResume;
        prefixHandlers[WebMessagePrefix.DownloadRequest] = HandleDownloadRequest;
        exactHandlers[WebMessagePrefix.DownloadClear] = HandleDownloadClear;
        exactHandlers[WebMessagePrefix.DownloadRequestSync] = HandleDownloadSync;
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
        
        try
        {
            using var client = new System.Net.Http.HttpClient();
            using var response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            
            item.TotalBytes = response.Content.Headers.ContentLength ?? 0;
            
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None, 8192, true);
            
            var buffer = new byte[8192];
            var isMoreToRead = true;
            long totalRead = 0;
            
            do
            {
                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    isMoreToRead = false;
                }
                else
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;
                    item.ReceivedBytes = totalRead;
                }
            }
            while (isMoreToRead);
            
            item.State = DownloadState.Completed;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Native download failed: {ex.Message}");
            item.State = DownloadState.Failed;
        }
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
