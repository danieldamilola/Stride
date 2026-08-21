using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StrideBrowser.Models;

public enum AppThemeMode { System, Light, Dark }

/// <summary>User-configurable browser settings.</summary>
public sealed partial class BrowserSettings : ObservableObject
{

    [ObservableProperty]
    private string _searchEngine = "DuckDuckGo";

    [ObservableProperty]
    private AppThemeMode _appTheme = AppThemeMode.System;

    [ObservableProperty]
    private bool _isSidebarOnRight;

    [ObservableProperty]
    private bool _isCompactMode;

    [ObservableProperty]
    private bool _adBlockEnabled = true;

    [ObservableProperty]
    private string _accentColor = "#7fb89a";

    [ObservableProperty]
    private int _defaultZoom = 100;

    [ObservableProperty]
    private bool _restoreSessionOnStartup = true;

    [ObservableProperty]
    private bool _hardwareAccelerationEnabled = true;

    [ObservableProperty]
    private bool _forceDarkMode = true;

    [ObservableProperty]
    private bool _forceHttps = true;

    /// <summary>
    /// Enables Microsoft Defender SmartScreen (phishing/malware protection). When on,
    /// navigated URLs are checked with Microsoft - a privacy trade-off the user controls.
    /// Read at WebView2 environment creation, so changes require a restart to apply.
    /// </summary>
    [ObservableProperty]
    private bool _smartScreenEnabled = true;

    [ObservableProperty]
    private bool _clearDataOnExit;

    [ObservableProperty]
    private bool _blockDuplicateTabs = true;

    [ObservableProperty]
    private bool _tabHibernationEnabled = true;

    [ObservableProperty]
    private bool _tabSleepEnabled = true;

    [ObservableProperty]
    private double _tabSleepOpacity = 0.55;

    [ObservableProperty]
    private double _tabHibernationOpacity = 0.15;

    [ObservableProperty]
    private bool _tabSleepDimEnabled = true;

    [ObservableProperty]
    private bool _tabHibernationDimEnabled = true;

    [ObservableProperty]
    private bool _autoCheckForUpdates = true;

    [ObservableProperty]
    private bool _useFloatingCommandBar = true;

    [ObservableProperty]
    private bool _isSidebarPinned;

    [ObservableProperty]
    private bool _showSettingsIcon = true;

    [ObservableProperty]
    private bool _showDownloadsIcon = true;

    [ObservableProperty]
    private bool _showBackArrow = true;

    [ObservableProperty]
    private bool _showForwardArrow = true;

    [ObservableProperty]
    private bool _showRefreshButton = true;

    [ObservableProperty]
    private bool _showTabNames;

    [ObservableProperty]
    private bool _addressBarOnLeft;

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

    [ObservableProperty]
    private bool _hasCompletedOnboarding;

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

    /// <summary>Resets all settings to their default values.</summary>
    public void ResetToDefaults()
    {
        var d = new BrowserSettings();
        SearchEngine = d.SearchEngine;
        AppTheme = d.AppTheme;
        IsSidebarOnRight = d.IsSidebarOnRight;
        IsCompactMode = d.IsCompactMode;
        AdBlockEnabled = d.AdBlockEnabled;
        AccentColor = d.AccentColor;
        DefaultZoom = d.DefaultZoom;
        RestoreSessionOnStartup = d.RestoreSessionOnStartup;
        HardwareAccelerationEnabled = d.HardwareAccelerationEnabled;
        ForceDarkMode = d.ForceDarkMode;
        ForceHttps = d.ForceHttps;
        SmartScreenEnabled = d.SmartScreenEnabled;
        ClearDataOnExit = d.ClearDataOnExit;
        BlockDuplicateTabs = d.BlockDuplicateTabs;
        TabHibernationEnabled = d.TabHibernationEnabled;
        TabSleepEnabled = d.TabSleepEnabled;
        TabSleepOpacity = d.TabSleepOpacity;
        TabHibernationOpacity = d.TabHibernationOpacity;
        TabSleepDimEnabled = d.TabSleepDimEnabled;
        TabHibernationDimEnabled = d.TabHibernationDimEnabled;
        AutoCheckForUpdates = d.AutoCheckForUpdates;
        UseFloatingCommandBar = d.UseFloatingCommandBar;
        IsSidebarPinned = d.IsSidebarPinned;
        ShowSettingsIcon = d.ShowSettingsIcon;
        ShowDownloadsIcon = d.ShowDownloadsIcon;
        ShowBackArrow = d.ShowBackArrow;
        ShowForwardArrow = d.ShowForwardArrow;
        ShowRefreshButton = d.ShowRefreshButton;
        ShowTabNames = d.ShowTabNames;
        AddressBarOnLeft = d.AddressBarOnLeft;
        NewTabBackground = d.NewTabBackground;
        NewTabShortcuts = new List<ShortcutItem>(d.NewTabShortcuts);
        FocusLocked = d.FocusLocked;
        FocusDomains = d.FocusDomains;
        CustomShortcuts = new Dictionary<string, string>(d.CustomShortcuts);
        YtEnhancerEnabled = d.YtEnhancerEnabled;
        YtDefaultQuality = d.YtDefaultQuality;
        YtDisableAutoplay = d.YtDisableAutoplay;
        YtPauseOnTabSwitch = d.YtPauseOnTabSwitch;
        YtDefaultSpeed = d.YtDefaultSpeed;
        YtLoopVideo = d.YtLoopVideo;
        UnhookEnabled = d.UnhookEnabled;
        UnhookHideHomeFeed = d.UnhookHideHomeFeed;
        UnhookHideVideoSidebar = d.UnhookHideVideoSidebar;
        UnhookHideRecommended = d.UnhookHideRecommended;
        UnhookHideLiveChat = d.UnhookHideLiveChat;
        UnhookHidePlaylist = d.UnhookHidePlaylist;
        UnhookHideFundraiser = d.UnhookHideFundraiser;
        UnhookHideEndScreenFeed = d.UnhookHideEndScreenFeed;
        UnhookHideEndScreenCards = d.UnhookHideEndScreenCards;
        UnhookHideShorts = d.UnhookHideShorts;
        UnhookHideComments = d.UnhookHideComments;
        UnhookHideMixes = d.UnhookHideMixes;
        UnhookHideMerch = d.UnhookHideMerch;
        UnhookHideVideoInfo = d.UnhookHideVideoInfo;
        UnhookHideTopHeader = d.UnhookHideTopHeader;
        UnhookHideNotifications = d.UnhookHideNotifications;
        UnhookHideInaptSearch = d.UnhookHideInaptSearch;
        UnhookHideExplore = d.UnhookHideExplore;
        UnhookHideMoreYT = d.UnhookHideMoreYT;
        UnhookHideSubscriptions = d.UnhookHideSubscriptions;
        UnhookDisableAutoplay = d.UnhookDisableAutoplay;
        UnhookDisableAnnotations = d.UnhookDisableAnnotations;
    }

}
