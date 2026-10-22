using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Downloads;

namespace StrideBrowser.Engine.Handlers;

public static class TabDownloadHandler
{
    public static void Wire(CoreWebView2 core, Dispatcher dispatcher, IDownloadStore downloadStore,
        Dictionary<string, CoreWebView2DownloadOperation> activeNativeDownloads,
        Services.LinkPreview.ILinkPreviewDownloadSuppressor downloadSuppressor,
        DownloadCoordinator? coordinator = null, Guid originTabId = default)
    {
        var actions = coordinator ?? new DownloadCoordinator(downloadStore);
        core.IsDefaultDownloadDialogOpenChanged += (_, _) =>
        {
            try { if (core.IsDefaultDownloadDialogOpen) core.CloseDefaultDownloadDialog(); }
            catch (Exception ex) { Trace.WriteLine(ex); }
        };
        core.DownloadStarting += (_, e) =>
        {
            try
            {
                e.Handled = true;
                var op = e.DownloadOperation;
                if (downloadSuppressor.ShouldSuppress(op.Uri)) { e.Cancel = true; return; }
                e.ResultFilePath = DownloadDestination.Reserve(e.ResultFilePath, downloadStore);
                var item = CreateDownloadItem(e.ResultFilePath, op.Uri, op.TotalBytesToReceive);
                Track(op, item, dispatcher, downloadStore, activeNativeDownloads, actions, originTabId);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DownloadStarting failed: {ex}");
                try { e.Cancel = true; e.Handled = true; } catch (Exception cancel) { Trace.WriteLine(cancel); }
            }
        };
    }
    public static string ResolveDownloadFileName(string? resultFilePath, string? url)
    {
        var file = SafeFileName(resultFilePath);
        if (!string.IsNullOrWhiteSpace(file)) return file;
        if (DownloadItem.IsRestartableUrl(url)) return SafeFileName(new Uri(url!).LocalPath) is { Length: > 0 } name ? name : "download";
        return "download";
    }
    private static string? SafeFileName(string? path)
    {
        try { return string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path); }
        catch (Exception ex) { Trace.WriteLine(ex); return null; }
    }
    public static DownloadItem CreateDownloadItem(string? resultFilePath, string? url, ulong? totalBytesToReceive)
        => new() { FileName = ResolveDownloadFileName(resultFilePath, url), FilePath = resultFilePath ?? "", Url = url ?? "",
            TotalBytes = totalBytesToReceive is <= long.MaxValue ? (long)totalBytesToReceive.Value : 0 };

    public static void Track(CoreWebView2DownloadOperation op, DownloadItem item, Dispatcher dispatcher,
        IDownloadStore store, Dictionary<string, CoreWebView2DownloadOperation> native, DownloadCoordinator actions, Guid originTabId)
    {
        native[item.Id] = op;
        var clock = Stopwatch.StartNew();
        var tracker = new DownloadProgressTracker();
        tracker.Reset(op.BytesReceived, clock.Elapsed);
        item.ReceivedBytes = op.BytesReceived;
        item.CompletedAt = null;
        actions.Attach(item, new NativeTransfer(op), originTabId);
        bool OwnsOperation() => native.TryGetValue(item.Id, out var current) && ReferenceEquals(current, op) && store.Get(item.Id) == item;
        void UpdateProgress()
        {
            item.ReceivedBytes = op.BytesReceived;
            if (op.TotalBytesToReceive is <= long.MaxValue and > 0) item.TotalBytes = (long)op.TotalBytesToReceive.Value;
            var sample = tracker.Sample(item.ReceivedBytes, item.TotalBytes, clock.Elapsed);
            item.SpeedBytesPerSec = sample.Speed; item.EstimatedTimeRemaining = sample.Remaining;
        }
        EventHandler<object>? progress = null;
        EventHandler<object>? state = null;
        void Finish()
        {
            op.BytesReceivedChanged -= progress;
            op.StateChanged -= state;
            if (native.TryGetValue(item.Id, out var current) && ReferenceEquals(current, op))
            { native.Remove(item.Id); actions.Detach(item.Id); }
        }
        progress = (_, _) => dispatcher.InvokeAsync(() =>
        {
            try { if (OwnsOperation()) UpdateProgress(); }
            catch (Exception ex) { Trace.WriteLine(ex); }
        });
        state = (_, _) => dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (!OwnsOperation()) { Finish(); return; }
                UpdateProgress();
                item.CanResumeNative = op.CanResume;
                if (op.State == CoreWebView2DownloadState.InProgress)
                {
                    item.ErrorMessage = ""; item.State = DownloadState.InProgress;
                    tracker.Reset(item.ReceivedBytes, clock.Elapsed);
                }
                else if (op.State == CoreWebView2DownloadState.Completed)
                {
                    item.CompletedAt = DateTime.UtcNow; item.CanResumeNative = false;
                    item.State = DownloadState.Completed; Finish();
                }
                else
                {
                    item.SpeedBytesPerSec = 0; item.EstimatedTimeRemaining = null;
                    if (op.InterruptReason == CoreWebView2DownloadInterruptReason.UserPaused)
                        item.State = DownloadState.Paused;
                    else
                    {
                        item.ErrorMessage = op.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled ? "" : DescribeFailure(op.InterruptReason.ToString());
                        item.RetryAllowed = !IsSecurityBlock(op.InterruptReason.ToString());
                        item.CanResumeNative = false;
                        item.State = op.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled ? DownloadState.Cancelled : DownloadState.Failed;
                        Finish();
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex); item.ErrorMessage = "The download stopped. Try the website's download link again.";
                item.State = DownloadState.Failed; Finish();
            }
        });
        op.BytesReceivedChanged += progress;
        op.StateChanged += state;
    }
    public static bool IsSecurityBlock(string reason) => reason is "FileMalicious" or "FileBlocked" or "FileSecurityCheckFailed" or "FileVirusInfected";
    public static string DescribeFailure(string reason) => reason switch
    {
        "FileAccessDenied" or "FileFailed" => "Stride could not save the file. Check that the download folder is available and writable.",
        "FileNoSpace" => "There is not enough free space to save this file.",
        "FileNameTooLong" => "The file name or download folder path is too long.",
        "NetworkDisconnected" or "NetworkFailed" or "NetworkServerDown" => "The connection was lost. Check your internet connection and retry.",
        "NetworkTimeout" => "The connection timed out. Retry the download.",
        "ServerUnauthorized" or "ServerForbidden" => "This download needs permission. Sign in on the website and download the file again.",
        "ServerBadContent" or "ServerFailed" or "ServerNoRange" => "The server could not provide the file. Try the website's download link again.",
        "FileMalicious" or "FileVirusInfected" => "Windows blocked this file because it may be harmful.",
        "FileBlocked" or "FileSecurityCheckFailed" => "Windows blocked this file during its security check.",
        _ => "The download was interrupted. Retry, or use the download link on the website again."
    };
    private sealed class NativeTransfer(CoreWebView2DownloadOperation op) : IDownloadTransfer
    {
        public bool CanResume => op.State == CoreWebView2DownloadState.Interrupted && op.CanResume;
        public void Pause() { if (op.State == CoreWebView2DownloadState.InProgress) op.Pause(); }
        public void Resume() { if (CanResume) op.Resume(); }
        public void Cancel() { if (op.State != CoreWebView2DownloadState.Completed) op.Cancel(); }
    }
}
