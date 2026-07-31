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
    private DownloadState _state;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string FileName { get; init; } = "";
    public string Url { get; init; } = "";
    public string FilePath { get; set; } = "";
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

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
