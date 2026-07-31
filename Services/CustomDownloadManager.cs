using System;
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

    public CustomDownloadManager()
    {
        // Simple HttpClient instance; cookies are attached per request
        var handler = new HttpClientHandler { UseCookies = false };
        _httpClient = new HttpClient(handler);
    }

    /// <summary>
    /// Resumes a download using an HTTP Range request, extracting cookies from the WebView2 environment.
    /// </summary>
    public async Task ResumeDownloadAsync(DownloadItem item, CoreWebView2CookieManager cookieManager)
    {
        try
        {
            item.State = DownloadState.InProgress;
            
            long existingBytes = 0;
            if (File.Exists(item.FilePath))
            {
                var fileInfo = new FileInfo(item.FilePath);
                existingBytes = fileInfo.Length;
            }

            var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
            
            // Set the Range header for resuming
            if (existingBytes > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
            }

            // Extract cookies from WebView2 for this domain
            var cookies = await cookieManager.GetCookiesAsync(item.Url);
            if (cookies != null && cookies.Count > 0)
            {
                var cookieString = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);
            }

            // Execute the request
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            // Update TotalBytes if unknown (some servers omit it on range requests, so we add existingBytes)
            if (item.TotalBytes <= 0 && response.Content.Headers.ContentLength.HasValue)
            {
                item.TotalBytes = existingBytes + response.Content.Headers.ContentLength.Value;
            }

            using var contentStream = await response.Content.ReadAsStreamAsync();
            
            // Offload the heavy stream reading to a background thread to prevent UI thread lockups
            await Task.Run(async () => 
            {
                using var fileStream = new FileStream(item.FilePath, FileMode.Append, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920]; // 80KB buffer
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    if (item.State != DownloadState.InProgress)
                    {
                        break;
                    }

                    await fileStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                    existingBytes += bytesRead;
                    
                    // Safe to update because DownloadItem properties now check for changes 
                    // and WPF safely marshals PropertyChanged for simple bindings
                    item.ReceivedBytes = existingBytes;
                }
            });

            if (item.State == DownloadState.InProgress)
            {
                // Finished normally
                item.State = DownloadState.Completed;
            }
        }
        catch (Exception)
        {
            // If anything fails during the manual stream, mark it failed.
            if (item.State == DownloadState.InProgress)
            {
                item.State = DownloadState.Failed;
            }
        }
    }
}
