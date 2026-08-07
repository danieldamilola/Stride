using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StrideBrowser.Models;

/// <summary>
/// Represents a single download tracked by the browser.
/// Properties raise change notifications for live UI binding.
/// </summary>
public sealed class DownloadItem : INotifyPropertyChanged
{
    private long _receivedBytes;
    private long _totalBytes;
    private long _speedBytesPerSec;
    private TimeSpan? _estimatedTimeRemaining;
    private DownloadState _state;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string FileName { get; init; } = "";
    public string Url { get; init; } = "";
    public string FilePath { get; set; } = "";
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public string? ETag { get; set; }
    public string? LastModified { get; set; }

    public long ReceivedBytes
    {
        get => _receivedBytes;
        set { if (_receivedBytes != value) { _receivedBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); } }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set { if (_totalBytes != value) { _totalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); } }
    }

    public long SpeedBytesPerSec
    {
        get => _speedBytesPerSec;
        set { if (_speedBytesPerSec != value) { _speedBytesPerSec = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedText)); } }
    }

    public TimeSpan? EstimatedTimeRemaining
    {
        get => _estimatedTimeRemaining;
        set { if (_estimatedTimeRemaining != value) { _estimatedTimeRemaining = value; OnPropertyChanged(); OnPropertyChanged(nameof(EtaText)); } }
    }

    public string SpeedText
    {
        get
        {
            if (SpeedBytesPerSec <= 0 || State != DownloadState.InProgress) return "";
            return FormatBytes(SpeedBytesPerSec) + "/s";
        }
    }

    public string EtaText
    {
        get
        {
            if (!EstimatedTimeRemaining.HasValue || State != DownloadState.InProgress) return "";
            var eta = EstimatedTimeRemaining.Value;
            if (eta.TotalDays >= 1) return $"{(int)eta.TotalDays}d {eta.Hours}h left";
            if (eta.TotalHours >= 1) return $"{eta.Hours}h {eta.Minutes}m left";
            if (eta.TotalMinutes >= 1) return $"{eta.Minutes}m {eta.Seconds}s left";
            return $"{eta.Seconds}s left";
        }
    }

    private string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB" };
        int i = Math.Min((int)Math.Floor(Math.Log(bytes) / Math.Log(1024)), units.Length - 1);
        return (bytes / Math.Pow(1024, i)).ToString(i > 0 ? "F1" : "F0") + " " + units[i];
    }

    public DownloadState State
    {
        get => _state;
        set { if (_state != value) { _state = value; OnPropertyChanged(); } }
    }

    /// <summary>Progress as 0–100, or -1 if total size is unknown.</summary>
    public int ProgressPercent =>
        TotalBytes > 0 ? (int)(ReceivedBytes * 100 / TotalBytes) : -1;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum DownloadState
{
    InProgress,
    Paused,
    Completed,
    Cancelled,
    Failed
}
