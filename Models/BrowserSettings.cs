using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StrideBrowser.Models;

/// <summary>User-configurable browser settings.</summary>
public sealed partial class BrowserSettings : ObservableObject
{
    [ObservableProperty]
    private string _searchEngine = "DuckDuckGo";

    [ObservableProperty]
    private bool _isSidebarOnRight;

    [ObservableProperty]
    private bool _isCompactMode;

    [ObservableProperty]
    private bool _adBlockEnabled = true;

    [ObservableProperty]
    private string _accentColor = "#D4A574";

    [ObservableProperty]
    private int _defaultZoom = 100;

    [ObservableProperty]
    private bool _restoreSessionOnStartup = true;

    [ObservableProperty]
    private bool _forceDarkMode = true;

    [ObservableProperty]
    private bool _forceHttps = true;

    /// <summary>
    /// Enables Microsoft Defender SmartScreen (phishing/malware protection). When on,
    /// navigated URLs are checked with Microsoft — a privacy trade-off the user controls.
    /// Read at WebView2 environment creation, so changes require a restart to apply.
    /// </summary>
    [ObservableProperty]
    private bool _smartScreenEnabled = true;

    [ObservableProperty]
    private bool _clearDataOnExit;

    [ObservableProperty]
    private bool _blockDuplicateTabs = true;

    [ObservableProperty]
    private bool _isSidebarPinned;

    [ObservableProperty]
    private string _newTabBackground = "";

    [ObservableProperty]
    private List<ShortcutItem> _newTabShortcuts = new();

    [ObservableProperty]
    private bool _focusLocked;

    [ObservableProperty]
    private string _focusDomains = "";

    /// <summary>
    /// Custom keyboard shortcut overrides. Maps action name (e.g. "NewTab")
    /// to key combo string (e.g. "Ctrl+K"). Missing entries use built-in defaults.
    /// </summary>
    [ObservableProperty]
    private Dictionary<string, string> _customShortcuts = new();

    // ── YouTube Enhancer ──

    [ObservableProperty]
    private bool _ytEnhancerEnabled = true;

    [ObservableProperty]
    private string _ytDefaultQuality = "hd1080";

    [ObservableProperty]
    private bool _ytDisableAutoplay = true;

    [ObservableProperty]
    private bool _ytPauseOnTabSwitch;

    [ObservableProperty]
    private double _ytDefaultSpeed = 1.0;

    [ObservableProperty]
    private bool _ytLoopVideo;

    // ── YouTube Unhook ──

    [ObservableProperty]
    private bool _unhookEnabled = true;

    [ObservableProperty]
    private bool _unhookHideHomeFeed;

    [ObservableProperty]
    private bool _unhookHideVideoSidebar = true;

    [ObservableProperty]
    private bool _unhookHideRecommended = true;

    [ObservableProperty]
    private bool _unhookHideLiveChat = true;

    [ObservableProperty]
    private bool _unhookHidePlaylist = true;

    [ObservableProperty]
    private bool _unhookHideFundraiser = true;

    [ObservableProperty]
    private bool _unhookHideEndScreenFeed = true;

    [ObservableProperty]
    private bool _unhookHideEndScreenCards = true;

    [ObservableProperty]
    private bool _unhookHideShorts = true;

    [ObservableProperty]
    private bool _unhookHideComments = true;

    [ObservableProperty]
    private bool _unhookHideMixes = true;

    [ObservableProperty]
    private bool _unhookHideMerch = true;

    [ObservableProperty]
    private bool _unhookHideVideoInfo;

    [ObservableProperty]
    private bool _unhookHideTopHeader;

    [ObservableProperty]
    private bool _unhookHideNotifications = true;

    [ObservableProperty]
    private bool _unhookHideInaptSearch = true;

    [ObservableProperty]
    private bool _unhookHideExplore = true;

    [ObservableProperty]
    private bool _unhookHideMoreYT = true;

    [ObservableProperty]
    private bool _unhookHideSubscriptions;

    [ObservableProperty]
    private bool _unhookDisableAutoplay = true;

    [ObservableProperty]
    private bool _unhookDisableAnnotations = true;

}
