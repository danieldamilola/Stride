using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

public class CustomDownloadManager
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _ctsMap = new();

    public CustomDownloadManager()
    {
        var handler = new HttpClientHandler { UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.All };
        _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void PauseDownload(DownloadItem item)
    {
        if (item.State == DownloadState.InProgress)
        {
            item.State = DownloadState.Paused;
            if (_ctsMap.TryRemove(item.Id, out var cts))
            {
                cts.Cancel();
            }
        }
    }

    public void CancelDownload(DownloadItem item)
    {
        item.State = DownloadState.Cancelled;
        if (_ctsMap.TryRemove(item.Id, out var cts))
        {
            cts.Cancel();
        }
    }

    public async Task ResumeDownloadAsync(DownloadItem item, CoreWebView2CookieManager cookieManager)
    {
        if (item.State == DownloadState.InProgress) return;
        
        item.State = DownloadState.InProgress;
        var cts = new CancellationTokenSource();
        _ctsMap[item.Id] = cts;

        try
        {
            long existingBytes = 0;
            if (File.Exists(item.FilePath))
            {
                existingBytes = new FileInfo(item.FilePath).Length;
            }

            var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
            
            if (existingBytes > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
                if (!string.IsNullOrEmpty(item.ETag))
                    request.Headers.IfRange = new System.Net.Http.Headers.RangeConditionHeaderValue(item.ETag);
                else if (!string.IsNullOrEmpty(item.LastModified) && DateTimeOffset.TryParse(item.LastModified, out var lastMod))
                    request.Headers.IfRange = new System.Net.Http.Headers.RangeConditionHeaderValue(lastMod);
            }

            var cookies = await cookieManager.GetCookiesAsync(item.Url);
            if (cookies != null && cookies.Count > 0)
            {
                var cookieString = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            // If server returned 200 instead of 206, it means range request failed (not supported or ETags mismatched)
            if (response.StatusCode == System.Net.HttpStatusCode.OK && existingBytes > 0)
            {
                existingBytes = 0;
                File.Delete(item.FilePath);
            }

            item.ETag = response.Headers.ETag?.Tag;
            item.LastModified = response.Content.Headers.LastModified?.ToString();

            if (item.TotalBytes <= 0 && response.Content.Headers.ContentLength.HasValue)
            {
                item.TotalBytes = existingBytes + response.Content.Headers.ContentLength.Value;
            }

            using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token);
            
            await Task.Run(async () => 
            {
                using var fileStream = new FileStream(item.FilePath, existingBytes > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920];
                int bytesRead;
                
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                long lastBytes = existingBytes;
                
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false)) > 0)
                {
                    if (item.State != DownloadState.InProgress) break;

                    await fileStream.WriteAsync(buffer, 0, bytesRead, cts.Token).ConfigureAwait(false);
                    existingBytes += bytesRead;
                    item.ReceivedBytes = existingBytes;
                    
                    if (stopwatch.ElapsedMilliseconds >= 1000)
                    {
                        long bytesSinceLast = existingBytes - lastBytes;
                        item.SpeedBytesPerSec = (long)(bytesSinceLast / stopwatch.Elapsed.TotalSeconds);
                        
                        if (item.TotalBytes > 0 && item.SpeedBytesPerSec > 0)
                        {
                            long remainingBytes = item.TotalBytes - existingBytes;
                            item.EstimatedTimeRemaining = TimeSpan.FromSeconds(remainingBytes / (double)item.SpeedBytesPerSec);
                        }
                        else
                        {
                            item.EstimatedTimeRemaining = null;
                        }
                        
                        lastBytes = existingBytes;
                        stopwatch.Restart();
                    }
                }
            }, cts.Token);

            if (item.State == DownloadState.InProgress)
            {
                item.State = DownloadState.Completed;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when user pauses or cancels
        }
        catch (Exception)
        {
            if (item.State == DownloadState.InProgress)
            {
                item.State = DownloadState.Failed;
            }
        }
        finally
        {
            _ctsMap.TryRemove(item.Id, out _);
        }
    }
}
