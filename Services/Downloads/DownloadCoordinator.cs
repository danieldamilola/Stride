using System.Diagnostics;
using System.IO;
using StrideBrowser.Models;

namespace StrideBrowser.Services.Downloads;

public interface IDownloadTransfer
{
    bool CanResume { get; }
    void Pause();
    void Resume();
    void Cancel();
}

public sealed class DownloadCoordinator
{
    private readonly IDownloadStore _store;
    private readonly Dictionary<string, IDownloadTransfer> _transfers = new();
    private readonly Dictionary<string, CancellationTokenSource> _pending = new();
    public event Action<DownloadItem, Guid>? DownloadStarted;
    public DownloadFileInfoService FileDetails { get; }
    public DownloadCoordinator(IDownloadStore store, DownloadFileInfoService? fileDetails = null)
    { _store = store; FileDetails = fileDetails ?? new DownloadFileInfoService(); }

    public void Remove(string id)
    {
        if (_store.Get(id) is { CanCancel: false }) _store.Remove(id);
    }
    public void CopyLink(string id)
    {
        if (_store.Get(id) is not { } item || !DownloadItem.IsRestartableUrl(item.Url)) return;
        try { System.Windows.Clipboard.SetText(item.Url); }
        catch (Exception ex) { Trace.WriteLine(ex); }
    }

    public void Attach(DownloadItem item, IDownloadTransfer transfer, Guid originTabId)
    {
        _transfers[item.Id] = transfer;
        item.IsStarting = false; item.IsMissing = false; item.ErrorMessage = ""; item.RetryAllowed = true;
        item.CanResumeNative = false; item.State = DownloadState.InProgress;
        if (_store.Get(item.Id) is null) _store.Add(item);
        DownloadStarted?.Invoke(item, originTabId);
    }
    public void Detach(string id) => _transfers.Remove(id);
    public void Pause(string id)
    {
        if (_store.Get(id) is not { State: DownloadState.InProgress } item || item.IsStarting) return;
        if (_transfers.TryGetValue(id, out var transfer)) TryCommand(item, transfer.Pause);
    }
    public void Resume(string id)
    {
        if (_store.Get(id) is not { State: DownloadState.Paused } item || item.IsStarting) return;
        if (_transfers.TryGetValue(id, out var transfer)) TryCommand(item, () =>
        {
            if (transfer.CanResume) transfer.Resume();
            else { item.CanResumeNative = false; item.ErrorMessage = "The paused transfer is no longer available. Restart the download."; }
        });
        else item.CanResumeNative = false;
    }
    public void Cancel(string id)
    {
        if (_pending.TryGetValue(id, out var pending)) pending.Cancel();
        if (_store.Get(id) is not { } item || !item.CanCancel) return;
        if (_transfers.Remove(id, out var transfer)) TryCommand(item, transfer.Cancel);
        item.IsStarting = false; item.CanResumeNative = false;
        item.SpeedBytesPerSec = 0; item.EstimatedTimeRemaining = null;
        item.State = DownloadState.Cancelled;
    }
    public async Task RetryAsync(string id, Func<DownloadItem, CancellationToken, Task> start)
    {
        var item = _store.Get(id);
        if (item is null || !item.CanRestart || _pending.ContainsKey(id)) return;
        using var cancellation = new CancellationTokenSource();
        _pending.Add(id, cancellation);
        item.IsStarting = true; item.ErrorMessage = "";
        try
        {
            if (_transfers.Remove(id, out var previous))
                try { previous.Cancel(); } catch (Exception ex) { Trace.WriteLine(ex); }
            await start(item, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.WriteLine($"Download restart failed: {ex}");
            if (_store.Get(id) == item && !cancellation.IsCancellationRequested)
            {
                item.ErrorMessage = "The download could not start. Try the download link on the website again.";
                item.State = DownloadState.Failed;
            }
        }
        finally { _pending.Remove(id); item.IsStarting = false; }
    }
    public void ClearAll()
    {
        foreach (var item in _store.Items.ToArray()) Cancel(item.Id);
        _store.ClearAll();
    }
    public void Open(string id) => OpenFile(id, false);
    public void ShowInFolder(string id) => OpenFile(id, true);
    private void OpenFile(string id, bool folder)
    {
        if (_store.Get(id) is not { State: DownloadState.Completed } item) return;
        if (!File.Exists(item.FilePath))
        {
            item.IsMissing = true;
            item.ErrorMessage = "This file was moved or deleted. Download it again from the website.";
            return;
        }
        try
        {
            if (folder)
            {
                var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                info.ArgumentList.Add("/select,"); info.ArgumentList.Add(item.FilePath);
                Process.Start(info);
            }
            else Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex) { Trace.WriteLine(ex); item.ErrorMessage = "Windows could not open this file. Try Show in folder."; }
    }
    private static void TryCommand(DownloadItem item, Action command)
    {
        try { command(); }
        catch (Exception ex) { Trace.WriteLine(ex); item.ErrorMessage = "This download is no longer available. Restart it from the website."; item.State = DownloadState.Failed; item.CanResumeNative = false; }
    }
}
