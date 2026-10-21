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
    private int _defaultZoom = 90;

    [ObservableProperty]
    private bool _restoreSessionOnStartup = true;

    [ObservableProperty]
    private bool _hardwareAccelerationEnabled = true;

    [ObservableProperty]
    private bool _forceDarkMode;

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

    /// <summary>
    /// When true, the address bar sends query text to DuckDuckGo for autocomplete.
    /// Off by default would be safest, but we preserve current behavior;
    /// the toggle lets privacy-sensitive users disable it.
    /// </summary>
    [ObservableProperty]
    private bool _searchSuggestionsEnabled = true;

    /// <summary>
    /// When true, a page with no engine favicon and no cached icon asks the DuckDuckGo
    /// icon service for one, which sends that page's host to a third party. Off by
    /// default. Settings files written before version
    /// <see cref="FaviconConsentSchemaVersion"/> are switched off once by
    /// <see cref="Services.SettingsMigration"/>, because a saved true from that era came
    /// from the property's old default-on value rather than from a choice the user made.
    /// History, autocomplete and shortcut suggestion rows never use this path at all.
    /// </summary>
    [ObservableProperty]
    private bool _faviconProxyEnabled;

    /// <summary>
    /// How hard WebView2 tracking prevention works. Balanced unless the user asks for
    /// Strict. The level is written to every profile Stride creates, and to the live ones
    /// when this changes, so the setting and the engine cannot drift apart.
    /// </summary>
    private TrackingPreventionMode _trackingPreventionMode = TrackingPreventionModes.Default;

    [JsonConverter(typeof(TrackingPreventionModeJsonConverter))]
    public TrackingPreventionMode TrackingPreventionMode
    {
        get => _trackingPreventionMode;
        set => SetProperty(ref _trackingPreventionMode, value);
    }

    /// <summary>
    /// The schema version of the settings file this build writes. Bumped when a change
    /// has to be applied to a file an older build left behind. See
    /// <see cref="Services.SettingsMigration"/>.
    /// </summary>
    [ObservableProperty]
    private int _settingsSchemaVersion = CurrentSchemaVersion;

    /// <summary>The newest settings schema this build understands.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The version at which the third-party favicon fallback became an explicit opt-in.
    /// A file below this was written when the property defaulted to on, so a saved true
    /// in it is not evidence that anyone chose it.
    /// </summary>
    public const int FaviconConsentSchemaVersion = 1;

    [ObservableProperty]
    private bool _blockDuplicateTabs = true;

    [ObservableProperty]
    private bool _tabHibernationEnabled = true;

    [ObservableProperty]
    private bool _tabSleepEnabled = true;

    [ObservableProperty]
    private bool _allowPinnedTabsToSleep;

    [ObservableProperty]
    private bool _allowPinnedTabsToHibernate;

    [ObservableProperty]
    private double _tabSleepOpacity = 0.55;

    [ObservableProperty]
    private double _tabHibernationOpacity = 0.5;

    [ObservableProperty]
    private bool _tabSleepDimEnabled = true;

    [ObservableProperty]
    private bool _tabHibernationDimEnabled = true;

    [ObservableProperty]
    private bool _autoCheckForUpdates = true;

    [ObservableProperty]
    private bool _isSidebarPinned;

    [ObservableProperty]
    private bool _showSettingsIcon = true;

    [ObservableProperty]
    private bool _showDownloadsIcon = true;

    [ObservableProperty]
    private bool _showReaderIcon = true;

    [ObservableProperty]
    private bool _showBackArrow = true;

    [ObservableProperty]
    private bool _showForwardArrow = true;

    [ObservableProperty]
    private bool _showRefreshButton = true;

    [ObservableProperty]
    private bool _showTabNames;

    /// <summary>When on, the toolbar follows the active site theme color.</summary>
    [ObservableProperty]
    private bool _adaptiveToolbarTint = true;

    /// <summary>
    /// When on, the web content sits inside a compact rounded frame: a short gap from
    /// the window edge, a thin outline, and the page itself clipped at the corners.
    /// Off removes the frame and restores the earlier resize inset, so a resizable
    /// normal window holds the content a few DIPs in from the sides and bottom rather
    /// than flush.
    /// </summary>
    [ObservableProperty]
    private bool _roundedWebContentFrameEnabled = true;

    [ObservableProperty]
    private bool _addressBarOnLeft = true;

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
    private bool _hasCompletedOnboarding = false;

    [ObservableProperty]
    private string _lastSeenReleaseNotesVersion = "";

    // ── Reader mode ──

    [ObservableProperty]
    private double _readerFontScale = 1.0;

    [ObservableProperty]
    private string _readerTheme = "system";

    [ObservableProperty]
    private double _readerContentWidth = 720;

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

    // ── Link Preview ──

    [ObservableProperty]
    private bool _linkPreviewEnabled = true;

    [ObservableProperty]
    private string _linkPreviewHotkey = "Alt";

    [ObservableProperty]
    private bool _linkPreviewAllowPress = true;

    /// <summary>Resets all settings to their default values.</summary>
    public void ResetToDefaults()
    {
        var d = new BrowserSettings();
        SearchEngine = d.SearchEngine;
        AppTheme = d.AppTheme;
        IsSidebarOnRight = d.IsSidebarOnRight;
        IsCompactMode = d.IsCompactMode;
        AdBlockEnabled = d.AdBlockEnabled;
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
        AllowPinnedTabsToSleep = d.AllowPinnedTabsToSleep;
        AllowPinnedTabsToHibernate = d.AllowPinnedTabsToHibernate;
        TabSleepOpacity = d.TabSleepOpacity;
        TabHibernationOpacity = d.TabHibernationOpacity;
        TabSleepDimEnabled = d.TabSleepDimEnabled;
        TabHibernationDimEnabled = d.TabHibernationDimEnabled;
        AutoCheckForUpdates = d.AutoCheckForUpdates;
        IsSidebarPinned = d.IsSidebarPinned;
        ShowSettingsIcon = d.ShowSettingsIcon;
        ShowDownloadsIcon = d.ShowDownloadsIcon;
        ShowReaderIcon = d.ShowReaderIcon;
        ShowBackArrow = d.ShowBackArrow;
        ShowForwardArrow = d.ShowForwardArrow;
        ShowRefreshButton = d.ShowRefreshButton;
        ShowTabNames = d.ShowTabNames;
        AdaptiveToolbarTint = d.AdaptiveToolbarTint;
        RoundedWebContentFrameEnabled = d.RoundedWebContentFrameEnabled;
        AddressBarOnLeft = d.AddressBarOnLeft;
        NewTabBackground = d.NewTabBackground;
        NewTabShortcuts = new List<ShortcutItem>(d.NewTabShortcuts);
        if (!FocusLocked)
        {
            FocusLocked = d.FocusLocked;
            FocusDomains = d.FocusDomains;
        }
        CustomShortcuts = new Dictionary<string, string>(d.CustomShortcuts);
        HasCompletedOnboarding = d.HasCompletedOnboarding;
        LastSeenReleaseNotesVersion = d.LastSeenReleaseNotesVersion;
        ReaderFontScale = d.ReaderFontScale;
        ReaderTheme = d.ReaderTheme;
        ReaderContentWidth = d.ReaderContentWidth;
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
        LinkPreviewEnabled = d.LinkPreviewEnabled;
        LinkPreviewHotkey = d.LinkPreviewHotkey;
        LinkPreviewAllowPress = d.LinkPreviewAllowPress;
        SearchSuggestionsEnabled = d.SearchSuggestionsEnabled;
        FaviconProxyEnabled = d.FaviconProxyEnabled;
        TrackingPreventionMode = d.TrackingPreventionMode;
        SettingsSchemaVersion = d.SettingsSchemaVersion;
    }

}

