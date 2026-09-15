using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine.Handlers;

public static class TabDownloadHandler
{
    public static void Wire(
        CoreWebView2 core, 
        Dispatcher dispatcher, 
        IDownloadStore downloadStore,
        Dictionary<string, CoreWebView2DownloadOperation> activeNativeDownloads,
        Services.LinkPreview.ILinkPreviewDownloadSuppressor downloadSuppressor)
    {
        core.IsDefaultDownloadDialogOpenChanged += (_, _) =>
        {
            try
            {
                if (core.IsDefaultDownloadDialogOpen) core.CloseDefaultDownloadDialog();
            }
            catch (Exception ex) { Trace.WriteLine($"CloseDefaultDownloadDialog failed: {ex.Message}"); }
        };

        // Sync lambda on purpose. An async void handler turns any slip into a
        // process kill, while a sync handler with guards keeps a bad download
        // from closing the browser. Every COM read below can throw once the
        // tab is closing or the WebView is disposed, so each one is guarded.
        core.DownloadStarting += (_, e) =>
        {
            try { Trace.WriteLine("DownloadStarting enter"); Trace.Flush(); }
            catch { }
            try
            {
                string? url = null;
                try { url = e.DownloadOperation.Uri; }
                catch (Exception ex) { Trace.WriteLine($"DownloadStarting Uri read failed: {ex.Message}"); }
                try { Trace.WriteLine($"DownloadStarting url={url}"); Trace.Flush(); }
                catch { }

                if (!string.IsNullOrWhiteSpace(url) && downloadSuppressor.ShouldSuppress(url))
                {
                    e.Cancel = true;
                    e.Handled = true;
                    return;
                }
                e.Handled = true;
                var op = e.DownloadOperation;

                string? resultPath = null;
                try { resultPath = op.ResultFilePath; }
                catch (Exception ex) { Trace.WriteLine($"DownloadStarting ResultFilePath read failed: {ex.Message}"); }

                ulong? totalBytes = null;
                try { totalBytes = op.TotalBytesToReceive; }
                catch (Exception ex) { Trace.WriteLine($"DownloadStarting TotalBytes read failed: {ex.Message}"); }

                var item = CreateDownloadItem(resultPath, url, totalBytes);
                try { Trace.WriteLine($"DownloadStarting item={item.Id} file={item.FileName} path={resultPath}"); Trace.Flush(); }
                catch { }
                activeNativeDownloads[item.Id] = op;
                dispatcher.InvokeAsync(() =>
                {
                    try { downloadStore.Add(item); try { Trace.WriteLine($"Add done id={item.Id}"); Trace.Flush(); } catch { } }
                    catch (Exception ex) { Trace.WriteLine($"DownloadStore Add failed: {ex.Message}"); }
                });

                WireProgressEvents(op, item, dispatcher);
                WireStateEvents(op, item, dispatcher, activeNativeDownloads);
                WireUICommands(op, item);
                try { Trace.WriteLine($"DownloadStarting wired done id={item.Id}"); Trace.Flush(); }
                catch { }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DownloadStarting failed: {ex}");
                Trace.Flush();
                try { e.Cancel = true; e.Handled = true; }
                catch (Exception cancelEx) { Trace.WriteLine($"DownloadStarting cancel failed: {cancelEx.Message}"); }
            }
        };
    }

    /// <summary>Resolves a safe file name from the native result path, falling back to the URL.</summary>
    public static string ResolveDownloadFileName(string? resultFilePath, string? url)
    {
        var fromPath = SafeGetFileName(resultFilePath);
        if (!string.IsNullOrWhiteSpace(fromPath))
            return fromPath;

        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var fromUrl = SafeGetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fromUrl))
                return fromUrl;
        }

        return "download";
    }

    private static string? SafeGetFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return System.IO.Path.GetFileName(path); }
        catch (Exception ex)
        {
            Trace.WriteLine($"GetFileName failed for path: {ex.Message}");
            return null;
        }
    }

    /// <summary>Builds a tracked download item from plain values, never touching the native op.</summary>
    public static DownloadItem CreateDownloadItem(string? resultFilePath, string? url, ulong? totalBytesToReceive)
    {
        var fileName = ResolveDownloadFileName(resultFilePath, url);
        long totalBytes = 0;
        if (totalBytesToReceive.HasValue && totalBytesToReceive.Value <= long.MaxValue)
            totalBytes = (long)totalBytesToReceive.Value;

        return new DownloadItem
        {
            FileName = fileName,
            Url = url ?? "",
            FilePath = resultFilePath ?? "",
            State = DownloadState.InProgress,
            TotalBytes = totalBytes,
            ReceivedBytes = 0
        };
    }

    private static void WireProgressEvents(CoreWebView2DownloadOperation op, DownloadItem item, Dispatcher dispatcher)
    {
        op.BytesReceivedChanged += (s, args) =>
        {
            dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var before = item.ReceivedBytes;
                    item.ReceivedBytes = op.BytesReceived;
                    if (before == 0 && item.ReceivedBytes > 0)
                    { try { Trace.WriteLine($"first bytes id={item.Id} n={item.ReceivedBytes}"); Trace.Flush(); } catch { } }
                    if (item.TotalBytes <= 0 && op.TotalBytesToReceive.HasValue && op.TotalBytesToReceive.Value > 0
                        && op.TotalBytesToReceive.Value <= long.MaxValue)
                        item.TotalBytes = (long)op.TotalBytesToReceive.Value;
                }
                catch (Exception ex) { Trace.WriteLine($"Download progress read failed: {ex.Message}"); }
            });
        };
    }

    private static void WireStateEvents(CoreWebView2DownloadOperation op, DownloadItem item, Dispatcher dispatcher, Dictionary<string, CoreWebView2DownloadOperation> activeNativeDownloads)
    {
        op.StateChanged += (s, args) =>
        {
            dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var old = item.State;
                    switch (op.State)
                    {
                        case CoreWebView2DownloadState.InProgress:
                            item.State = DownloadState.InProgress;
                            break;
                        case CoreWebView2DownloadState.Interrupted:
                            item.State = op.InterruptReason switch
                            {
                                CoreWebView2DownloadInterruptReason.UserPaused => DownloadState.Paused,
                                CoreWebView2DownloadInterruptReason.UserCanceled => DownloadState.Cancelled,
                                _ => DownloadState.Failed
                            };
                            if (item.State == DownloadState.Failed || item.State == DownloadState.Cancelled)
                            {
                                activeNativeDownloads.Remove(item.Id);
                            }
                            break;
                        case CoreWebView2DownloadState.Completed:
                            item.State = DownloadState.Completed;
                            activeNativeDownloads.Remove(item.Id);
                            break;
                    }
                    try { Trace.WriteLine($"state id={item.Id} {old}->{item.State} native={op.State}"); Trace.Flush(); } catch { }
                }
                catch (Exception ex) { Trace.WriteLine($"Download state read failed: {ex.Message}"); }
            });
        };
    }

    private static void WireUICommands(CoreWebView2DownloadOperation op, DownloadItem item)
    {
        item.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(DownloadItem.State))
            {
                try 
                {
                    if (item.State == DownloadState.Cancelled) op.Cancel();
                    else if (item.State == DownloadState.Paused) op.Pause();
                    else if (item.State == DownloadState.InProgress) op.Resume();
                } 
                catch (Exception ex) { Trace.WriteLine(ex); }
            }
        };
    }
}
