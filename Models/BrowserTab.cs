using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StrideBrowser.Models;

/// <summary>
/// Represents a single browser tab. Implements INotifyPropertyChanged
/// via ObservableObject for data binding in the tab strip.
/// </summary>
public sealed partial class BrowserTab : ObservableObject
{
    /// <summary>Unique tab identifier.</summary>
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
    private ImageSource? _favicon;

    /// <summary>
    /// Whether this tab's WebView2 is currently initialized and in memory.
    /// False when the tab has been hibernated to save resources.
    /// </summary>
    [ObservableProperty]
    private bool _isHibernated;

    /// <summary>True while navigation is in progress.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Last time this tab was the active tab (for LRU hibernation).</summary>
    public DateTime LastActiveTime { get; set; } = DateTime.UtcNow;

    public BrowserTab() { }

    public BrowserTab(string url)
    {
        _url = url;
    }
}
