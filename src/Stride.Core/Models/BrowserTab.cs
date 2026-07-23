using CommunityToolkit.Mvvm.ComponentModel;

namespace StrideBrowser.Models;

public sealed partial class BrowserTab : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty]
    private string _url = "";

    [ObservableProperty]
    private string _title = "New Tab";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private byte[]? _favicon;

    [ObservableProperty]
    private bool _isHibernated;

    [ObservableProperty]
    private bool _isLoading;

    public DateTime LastActiveTime { get; set; } = DateTime.UtcNow;
    public double ZoomLevel { get; set; } = 1.0;

    public BrowserTab() { }

    public BrowserTab(string url)
    {
        _url = url;
    }
}
