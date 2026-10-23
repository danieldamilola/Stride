using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using UmberBrowser.Models;

namespace UmberBrowser.Services.Downloads;

/// <summary>
/// Fallback for the hidden transfer window when the server answers a download
/// URL with content a browser renders inline instead of an attachment. Only
/// true attachments raise DownloadStarting, so without this the request waits
/// twenty seconds and fails every time. The fallback streams the bytes with
/// HTTP while the profile cookies travel along, and pause, resume and cancel
/// stay under the same coordinator as native downloads.
/// </summary>
public sealed class HttpFallbackTransfer : IDownloadTransfer
{
    private static readonly HttpClient Client = new()
    {
        // ResponseHeadersRead keeps the header wait bounded by this timeout;
        // the body of a slow wallpaper download streams on without it.
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly DownloadItem _item;
    private readonly Dispatcher _dispatcher;
    private readonly IDownloadStore _store;
    private readonly DownloadCoordinator _actions;
    private readonly string? _cookieHeader;
    private readonly string? _userAgent;
    private readonly CancellationTokenSource _stopped = new();
    private readonly object _gate = new();
    private Task _run = Task.CompletedTask;
    private long _offset;
    private long _lastPostTickCount;
    private bool _acceptsRanges;
    private bool _running;

    private HttpFallbackTransfer(DownloadItem item, Dispatcher dispatcher, IDownloadStore store,
        DownloadCoordinator actions, string? cookieHeader, string? userAgent)
    {
        _item = item;
        _dispatcher = dispatcher;
        _store = store;
        _actions = actions;
        _cookieHeader = cookieHeader;
        _userAgent = userAgent;
    }

    // A validator is part of resuming: Range without If-Range can append the
    // wrong file's bytes when the source changed. Both fields must agree.
    private bool CanAppend => _item.ETag is not null || _item.LastModified is not null;
    public bool CanResume => _acceptsRanges && _offset > 0 && CanAppend;

    /// <summary>
    /// Reads the session state the fallback needs while the hidden controller
    /// still exists, attaches the transfer, and streams the first attempt.
    /// Initial failures throw so the coordinator keeps its standard path.
    /// </summary>
    public static async Task StartAsync(CoreWebView2 core, DownloadItem item, Dispatcher dispatcher,
        IDownloadStore store, DownloadCoordinator actions, Guid originTabId, CancellationToken token)
    {
        var cookies = await core.Profile.CookieManager.GetCookiesAsync(item.Url);
        // The adopted path learns its destination from DownloadStarting. The
        // fallback never sees that event, so it reserves the slot itself in
        // the same default folder WebView2 proposes.
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(directory);
        item.FilePath = DownloadDestination.Reserve(
            Path.Combine(directory, string.IsNullOrWhiteSpace(item.FileName) ? "download" : item.FileName),
            store, item.Id);
        var transfer = new HttpFallbackTransfer(item, dispatcher, store, actions,
            BuildCookieHeader(cookies.Select(cookie => (cookie.Name, cookie.Value)).ToList()),
            core.Settings.UserAgent);
        actions.Attach(item, transfer, originTabId);
        await transfer.RunAsync(initial: true, token).ConfigureAwait(false);
    }

    public void Pause()
    {
        lock (_gate) _stopped.Cancel();
        // The reader exits on the stopped token and the part file keeps the
        // received prefix for the next attempt.
        var canResume = CanResume;
        Post(() =>
        {
            _item.SpeedBytesPerSec = 0;
            _item.EstimatedTimeRemaining = null;
            _item.CanResumeNative = canResume;
            _item.State = DownloadState.Paused;
        });
        lock (_gate) _running = false;
    }

    public void Resume()
    {
        if (!CanResume) return;
        Post(() =>
        {
            _item.ErrorMessage = "";
            _item.State = DownloadState.InProgress;
        });
        var (offset, ifRange) = DownloadFallbackPolicy.PlanResume(
            _acceptsRanges, _offset, _item.ETag, _item.LastModified);
        _offset = offset;
        var run = Task.Run(() => RunAsync(initial: false, CancellationToken.None, ifRange: ifRange));
        lock (_gate) _run = run;
    }

    public void Cancel()
    {
        lock (_gate) { _running = false; _stopped.Cancel(); }
        var run = _run;
        _ = Task.Run(async () =>
        {
            try { await run.ConfigureAwait(false); } catch { /* only the part file matters now */ }
            DeletePartFile(ignoreState: false);
        });
    }

    private async Task RunAsync(bool initial, CancellationToken token, string? ifRange = null)
    {
        lock (_gate) _running = true;
        try
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token, _stopped.Token);
            await AttemptAsync(ifRange, attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Pause, cancellation or shutdown stopped the read. The caller that
            // triggered the stop posts the resulting state, so leave the item
            // alone here.
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Fallback download failed for {_item.Url}: {ex.Message}");
            if (initial)
            {
                // A failed first attempt leaves a part file that would make the
                // next attempt reserve a numbered copy instead. Clean it before
                // handing the failure back.
                DeletePartFile(ignoreState: true);
                throw;
            }
            Fail(DownloadFallbackPolicy.DescribeException(ex));
        }
        finally { lock (_gate) _running = false; }
    }

    private async Task AttemptAsync(string? ifRange, CancellationToken token)
    {
        using var request = BuildRequest(ifRange);
        using var response = await Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

        var status = (int)response.StatusCode;
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!DownloadFallbackPolicy.ShouldAttempt(mediaType) ||
            status is 204 or 304 || response.Content.Headers.ContentLength == 0)
        {
            throw new InvalidOperationException("The link did not provide a file.");
        }
        if (status is not (200 or 201 or 206))
        {
            var message = DownloadFallbackPolicy.DescribeStatus(status);
            throw new InvalidOperationException(message ?? "The server could not provide the file.");
        }

        // A range response only lines up with the part file when it answers the
        // If-Range that was sent. Anything else restarts the body from zero.
        var append = status == 206 && ifRange is not null;
        _acceptsRanges = status == 206 ||
            response.Headers.AcceptRanges.Contains("bytes", StringComparer.OrdinalIgnoreCase);
        if (!append && _offset > 0) DeletePartFile(ignoreState: true);

        var validator = response.Headers.ETag?.Tag ?? response.Content.Headers.LastModified?.ToString("R");
        var contentLength = response.Content.Headers.ContentLength is <= long.MaxValue and > 0
            ? (long)response.Content.Headers.ContentLength : 0;
        var total = append && contentLength > 0 ? _offset + contentLength : contentLength;
        var startOffset = append ? _offset : 0;
        _offset = startOffset;

        Post(() =>
        {
            _item.ReceivedBytes = startOffset;
            if (total > 0) _item.TotalBytes = total;
            if (validator is not null) _item.ETag = validator;
        });

        var clock = Stopwatch.StartNew();
        var tracker = new DownloadProgressTracker();
        tracker.Reset(startOffset, clock.Elapsed);
        await using var source = await response.Content
            .ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var target = new FileStream(_item.FilePath, append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 16];
        int read;
        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            _offset += read;
            PostProgress(tracker, clock, total);
        }
        await target.FlushAsync(token).ConfigureAwait(false);

        // Every property write has to happen on the dispatcher thread so the
        // INotifyPropertyChanged updates reach the bindings cleanly. Track does
        // the same for native transfers.
        Post(() =>
        {
            _item.ReceivedBytes = Math.Max(_offset, startOffset);
            _item.TotalBytes = total > 0 ? total : _item.ReceivedBytes;
            _item.SpeedBytesPerSec = 0;
            _item.EstimatedTimeRemaining = null;
            _item.CanResumeNative = false;
            _item.CompletedAt = DateTime.UtcNow;
            _item.State = DownloadState.Completed;
        });
        _actions.Detach(_item.Id);
    }

    private HttpRequestMessage BuildRequest(string? ifRange)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _item.Url);
        if (!string.IsNullOrEmpty(_cookieHeader))
            request.Headers.TryAddWithoutValidation("Cookie", _cookieHeader);
        if (!string.IsNullOrWhiteSpace(_userAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        if (_offset > 0)
        {
            if (CanAppend && ifRange is not null)
                request.Headers.TryAddWithoutValidation("If-Range", ifRange);
            request.Headers.TryAddWithoutValidation("Range", $"bytes={_offset}-");
        }
        return request;
    }

    private void PostProgress(DownloadProgressTracker tracker, Stopwatch clock, long total)
    {
        var offset = _offset;
        var elapsed = clock.Elapsed;
        var (speed, remaining) = tracker.Sample(offset, total, elapsed);
        // Throttle the dispatcher traffic; completion always posts.
        var last = _lastPostTickCount;
        if (Environment.TickCount64 - last < 100) return;
        _lastPostTickCount = Environment.TickCount64;
        Post(() =>
        {
            _item.ReceivedBytes = offset;
            if (total > 0) _item.TotalBytes = total;
            _item.SpeedBytesPerSec = speed;
            _item.EstimatedTimeRemaining = remaining;
        });
    }

    private void Post(Action update) => Post(_ => update());

    private void Post(Action<DownloadItem> update)
    {

        try
        {
            _dispatcher.Invoke(() =>
            {
                if (_store.Get(_item.Id) != _item) return;
                update(_item);
            });
        }
        catch (TaskCanceledException)
        {
            // The dispatcher is winding down, typically during shutdown.
        }
    }

    private void Fail(string message) => Post(() =>
    {
        _item.ErrorMessage = message;
        _item.SpeedBytesPerSec = 0;
        _item.EstimatedTimeRemaining = null;
        _item.CanResumeNative = false;
        _item.RetryAllowed = true;
        _item.State = DownloadState.Failed;
    });

    /// <summary>
    /// Removes the part file. Paused downloads without a resumable range and
    /// cancelled or retried downloads both need the disk slot back so the next
    /// attempt lands on the plain file name again.
    /// </summary>
    private void DeletePartFile(bool ignoreState)
    {
        try
        {
            var path = _item.FilePath;
            if (path.Length == 0 || _item.State == DownloadState.Completed && !ignoreState) return;
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) { Trace.WriteLine(ex); }
    }

    private static string? BuildCookieHeader(IReadOnlyList<(string Name, string Value)> cookies)
    {
        if (cookies.Count == 0) return null;
        return string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));
    }
}
