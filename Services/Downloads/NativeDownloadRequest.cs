using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using UmberBrowser.Engine.Handlers;
using UmberBrowser.Models;

namespace UmberBrowser.Services.Downloads;

public sealed class NativeDownloadRequest : IDisposable
{
    private readonly IDownloadStore _store;
    private readonly BrowserSession _session;
    private readonly DownloadCoordinator _actions;
    private readonly HashSet<CoreWebView2Controller> _controllers = new();
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;
    public NativeDownloadRequest(IDownloadStore store, BrowserSession session, DownloadCoordinator actions)
        { _store = store; _session = session; _actions = actions; }

    public async Task StartAsync(CoreWebView2Environment environment, DownloadItem item, Guid originTabId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!DownloadItem.IsRestartableUrl(item.Url)) throw new InvalidOperationException("The download link has expired.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var token = linked.Token;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _session.NativeDownloads.Remove(item.Id);
        // HWND_MESSAGE gives the native transfer an invisible host that shares the browser
        // profile and stays alive when the user's browsing tab closes.
        var controller = await environment.CreateCoreWebView2ControllerAsync(new IntPtr(-3));
        _controllers.Add(controller);
        void Close()
        {
            if (_controllers.Remove(controller))
                try { controller.Close(); } catch (Exception ex) { Trace.WriteLine(ex); }
        }
        try
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            controller.IsVisible = false;
            var core = controller.CoreWebView2;
            core.Settings.IsScriptEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var adopted = false;
            ulong requestedNavigation = 0;
            core.BasicAuthenticationRequested += (_, e) =>
            { e.Cancel = true; started.TrySetException(new InvalidOperationException("Sign in on the website and try its download link again.")); };
            core.NavigationStarting += (_, e) =>
            {
                if (e.Uri != "about:blank") requestedNavigation = e.NavigationId;
                if (e.Uri != "about:blank" && !DownloadItem.IsRestartableUrl(e.Uri))
                { e.Cancel = true; started.TrySetResult(false); }
            };
            core.NavigationCompleted += async (_, e) =>
            {
                if (adopted || requestedNavigation == 0 || e.NavigationId != requestedNavigation) return;
                await CompleteNavigationAsync(started, () => adopted, token);
            };
            core.DownloadStarting += (_, e) =>
            {
                try
                {
                    e.Handled = true;
                    if (token.IsCancellationRequested || _store.Get(item.Id) != item || _disposed)
                    { e.Cancel = true; started.TrySetCanceled(token); return; }
                    adopted = true;
                    e.ResultFilePath = DownloadDestination.Reserve(e.ResultFilePath, _store, item.Id);
                    item.FilePath = e.ResultFilePath;
                    item.FileName = TabDownloadHandler.ResolveDownloadFileName(e.ResultFilePath, e.DownloadOperation.Uri);
                    item.ReceivedBytes = 0; item.SpeedBytesPerSec = 0; item.EstimatedTimeRemaining = null;
                    item.TotalBytes = e.DownloadOperation.TotalBytesToReceive is <= long.MaxValue ? (long)e.DownloadOperation.TotalBytesToReceive.Value : 0;
                    TabDownloadHandler.Track(e.DownloadOperation, item, dispatcher, _store, _session.NativeDownloads, _actions, originTabId, dependsOnOriginTab: false);
                    var operation = e.DownloadOperation;
                    operation.StateChanged += (_, _) =>
                    {
                        try
                        {
                            if (operation.State == CoreWebView2DownloadState.Completed ||
                                operation.State == CoreWebView2DownloadState.Interrupted && operation.InterruptReason != CoreWebView2DownloadInterruptReason.UserPaused)
                                dispatcher.BeginInvoke(Close);
                        }
                        catch (Exception ex) { Trace.WriteLine(ex); dispatcher.BeginInvoke(Close); }
                    };
                    started.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex);
                    try { e.Cancel = true; } catch (Exception cancel) { Trace.WriteLine(cancel); }
                    started.TrySetException(ex);
                }
            };
            core.Navigate(item.Url);
            var navigated = await started.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            if (!navigated)
            {
                // The URL answered with content the hidden window rendered
                // instead of an attachment, so no DownloadStarting ever raised.
                // Fall back to a direct HTTP transfer, still sharing the
                // profile cookies so signed-in content authorises the same way.
                await HttpFallbackTransfer.StartAsync(core, item, dispatcher,
                    _store, _actions, originTabId, token);
                // The fallback captured what it needs up front and needs no
                // controller afterwards. The adopted path closes through the
                // download state change instead.
                Close();
            }
        }
        catch { Close(); throw; }
    }
    internal static async Task CompleteNavigationAsync(TaskCompletionSource<bool> started, Func<bool> isDownloadAdopted, CancellationToken token)
    {
        try
        {
            // Attachment navigations can finish just before DownloadStarting arrives.
            await Task.Delay(250, token);
            if (!isDownloadAdopted()) started.TrySetResult(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            started.TrySetCanceled(token);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _shutdown.Cancel();
        foreach (var controller in _controllers.ToArray())
            try { controller.Close(); } catch (Exception ex) { Trace.WriteLine(ex); }
        _controllers.Clear(); _shutdown.Dispose();
    }
}
