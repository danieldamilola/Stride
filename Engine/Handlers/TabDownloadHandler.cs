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
        HashSet<string> activeNativeDownloads)
    {
        core.IsDefaultDownloadDialogOpenChanged += (_, _) =>
        {
            if (core.IsDefaultDownloadDialogOpen) core.CloseDefaultDownloadDialog();
        };

        core.DownloadStarting += async (_, e) =>
        {
            e.Handled = true;
            var op = e.DownloadOperation;

            var item = CreateDownloadItem(op);
            activeNativeDownloads.Add(item.Id);
            dispatcher.Invoke(() => downloadStore.Add(item));

            WireProgressEvents(op, item, dispatcher);
            WireStateEvents(op, item, dispatcher);
            WireUICommands(op, item);
        };
    }

    private static DownloadItem CreateDownloadItem(CoreWebView2DownloadOperation op)
    {
        return new DownloadItem
        {
            FileName = System.IO.Path.GetFileName(op.ResultFilePath),
            Url = op.Uri,
            FilePath = op.ResultFilePath,
            State = DownloadState.InProgress,
            TotalBytes = op.TotalBytesToReceive.HasValue ? (long)op.TotalBytesToReceive.Value : 0,
            ReceivedBytes = 0
        };
    }

    private static void WireProgressEvents(CoreWebView2DownloadOperation op, DownloadItem item, Dispatcher dispatcher)
    {
        op.BytesReceivedChanged += (s, args) =>
        {
            dispatcher.Invoke(() =>
            {
                item.ReceivedBytes = op.BytesReceived;
                if (item.TotalBytes <= 0 && op.TotalBytesToReceive.HasValue && op.TotalBytesToReceive.Value > 0)
                    item.TotalBytes = (long)op.TotalBytesToReceive.Value;
            });
        };
    }

    private static void WireStateEvents(CoreWebView2DownloadOperation op, DownloadItem item, Dispatcher dispatcher)
    {
        op.StateChanged += (s, args) =>
        {
            dispatcher.Invoke(() =>
            {
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
                        break;
                    case CoreWebView2DownloadState.Completed:
                        item.State = DownloadState.Completed;
                        break;
                }
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
