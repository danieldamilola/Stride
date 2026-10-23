using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace UmberBrowser.Models;

public sealed class DownloadItem : INotifyPropertyChanged
{
    private string _fileName = "";
    private string _filePath = "";
    private long _receivedBytes;
    private long _totalBytes;
    private long _speedBytesPerSec;
    private TimeSpan? _estimatedTimeRemaining;
    private DownloadState _state;
    private string _errorMessage = "";
    private bool _canResumeNative;
    private bool _isStarting;
    private bool _isMissing;
    private bool _retryAllowed = true;

    private BitmapSource? _fileIcon;
    [JsonIgnore] public string FileIconDataUri { get; internal set; } = "";
    [JsonIgnore] public BitmapSource? FileIcon
    {
        get => _fileIcon;
        internal set { if (ReferenceEquals(_fileIcon, value)) return; _fileIcon = value; PropertyChanged?.Invoke(this, new(nameof(FileIcon))); }
    }

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string FileName { get => _fileName; set { _fileName = value; Changed(); } }
    public string Url { get; init; } = "";
    public string FilePath { get => _filePath; set { _filePath = value; Changed(); } }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public string ErrorMessage { get => _errorMessage; set { if (_errorMessage != value) { _errorMessage = value; Changed(); } } }
    public bool RetryAllowed { get => _retryAllowed; set { _retryAllowed = value; Changed(); } }
    [JsonIgnore]
    public bool CanResumeNative { get => _canResumeNative; set { if (_canResumeNative != value) { _canResumeNative = value; Changed(); } } }
    [JsonIgnore]
    public bool IsStarting { get => _isStarting; set { if (_isStarting != value) { _isStarting = value; Changed(); } } }
    [JsonIgnore]
    public bool IsMissing { get => _isMissing; set { if (_isMissing != value) { _isMissing = value; Changed(); } } }

    public long ReceivedBytes { get => _receivedBytes; set { if (_receivedBytes != value) { _receivedBytes = value; Changed(); } } }
    public long TotalBytes { get => _totalBytes; set { if (_totalBytes != value) { _totalBytes = value; Changed(); } } }
    public long SpeedBytesPerSec { get => _speedBytesPerSec; set { if (_speedBytesPerSec != value) { _speedBytesPerSec = value; Changed(); } } }
    public TimeSpan? EstimatedTimeRemaining { get => _estimatedTimeRemaining; set { if (_estimatedTimeRemaining != value) { _estimatedTimeRemaining = value; Changed(); } } }
    public DownloadState State { get => _state; set { if (_state != value) { _state = value; Changed(); } } }

    public int ProgressPercent => TotalBytes > 0 ? (int)Math.Clamp((double)ReceivedBytes * 100 / TotalBytes, 0, 100) : -1;
    public bool IsIndeterminate => IsStarting || State == DownloadState.InProgress && TotalBytes <= 0;
    public bool CanPause => State == DownloadState.InProgress && !IsStarting;
    public bool CanResume => State == DownloadState.Paused && CanResumeNative && !IsStarting;
    public bool CanRestart => !IsStarting && RetryAllowed && IsRestartableUrl(Url) &&
        (State is DownloadState.Failed or DownloadState.Cancelled || State == DownloadState.Paused && !CanResumeNative || IsMissing);
    public bool CanCancel => IsStarting || State is DownloadState.InProgress or DownloadState.Paused;
    public bool CanOpen => State == DownloadState.Completed && !IsMissing;
    public string RetryLabel => State == DownloadState.Paused ? "Restart" : IsMissing ? "Download again" : "Retry";
    public string StatusText => IsStarting ? "Starting download..." : IsMissing ? "File not found" : State switch
    {
        DownloadState.InProgress => "Downloading",
        DownloadState.Paused => CanResumeNative ? "Paused" : IsRestartableUrl(Url) ? "Interrupted. Restart to download again." : "Interrupted. Download it again from the website.",
        DownloadState.Completed => "Completed",
        DownloadState.Cancelled => "Cancelled",
        _ => "Failed"
    };
    public string SourceHost => Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.IdnHost : "";
    public string SizeText => State == DownloadState.Completed ? FormatBytes(TotalBytes > 0 ? TotalBytes : ReceivedBytes) :
        TotalBytes > 0 ? FormatBytes(ReceivedBytes) + " / " + FormatBytes(TotalBytes) : ReceivedBytes > 0 ? FormatBytes(ReceivedBytes) : "Size unknown";
    public string SpeedText => SpeedBytesPerSec > 0 && State == DownloadState.InProgress && !IsStarting ? FormatBytes(SpeedBytesPerSec) + "/s" : "";
    public string EtaText
    {
        get
        {
            if (State != DownloadState.InProgress || IsStarting || EstimatedTimeRemaining is not { } eta) return "";
            if (eta.TotalHours >= 1) return $"{(int)eta.TotalHours}h {eta.Minutes}m left";
            if (eta.TotalMinutes >= 1) return $"{(int)eta.TotalMinutes}m {eta.Seconds}s left";
            return $"{Math.Max(1, eta.Seconds)}s left";
        }
    }
    public string DetailText => string.Join(" · ", new[] { SizeText, SpeedText, EtaText, State == DownloadState.Completed ? SourceHost : "" }.Where(s => s.Length > 0));
    [JsonIgnore]
    public string PopupDetailText => IsStarting ? "Starting download..." : IsMissing ? "File not found" : State switch
    {
        DownloadState.InProgress => string.Join(" \u00b7 ", new[] { SizeText, SpeedText, EtaText }.Where(value => value.Length > 0)),
        DownloadState.Completed => SizeText,
        DownloadState.Paused => (CanResumeNative ? "Paused" : "Interrupted") + " \u00b7 " + SizeText,
        DownloadState.Cancelled => "Cancelled \u00b7 " + SizeText,
        _ => "Failed \u00b7 " + SizeText
    };
    public static bool IsRestartableUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = Math.Min((int)Math.Floor(Math.Log(bytes) / Math.Log(1024)), units.Length - 1);
        return (bytes / Math.Pow(1024, i)).ToString(i > 0 ? "F1" : "F0") + " " + units[i];
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new(name));
        // A transfer update also changes action availability and the shared display text.
        foreach (var property in new[] { nameof(ProgressPercent), nameof(IsIndeterminate), nameof(CanPause), nameof(CanResume), nameof(CanRestart), nameof(CanCancel), nameof(CanOpen), nameof(RetryLabel), nameof(StatusText), nameof(SizeText), nameof(SpeedText), nameof(EtaText), nameof(DetailText), nameof(PopupDetailText) })
            PropertyChanged?.Invoke(this, new(property));
    }
}

public enum DownloadState { InProgress, Paused, Completed, Cancelled, Failed }
