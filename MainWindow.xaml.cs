using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using StrideBrowser.Engine;
using StrideBrowser.Interop;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Input;
using StrideBrowser.ViewModels;
using StrideBrowser.ViewModels.Reader;
using StrideBrowser.Services.Reader;
using StrideBrowser.Services.UI;

namespace StrideBrowser;

/// <summary>
/// Thin view layer — delegates all logic to BrowserViewModel and TabEngine.
/// Horizontal toolbar + horizontal tab strip layout.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly BrowserWindowManager _windows;
    private readonly BrowserWindowContext _windowContext;
    private readonly BrowserViewModel _vm;
    private readonly TabEngine _engine;
    private readonly TabStripController _tabStripController;
    private readonly ToolbarDropController _toolbarDropController;
    private readonly ToolbarNavigationController _toolbarNavigationController;
    private readonly SiteInfoController _siteInfoController;
    private readonly DownloadsPopupController _downloadsController;
    private readonly ExtensionsPopupController _extensionsController;
    private readonly IOneTabStore _oneTabStore;
    private readonly ISessionStore _sessionStore;
    private readonly IHistoryStore _historyStore;
    private readonly WebMessageRouter _router;
    private readonly ReaderViewModel _readerVm;
    private readonly IReaderService _readerService;
    private readonly ViewModels.LinkPreview.LinkPreviewViewModel _linkPreviewVm;
    private readonly Services.UI.TCLensLauncher _tcLensLauncher;
    private KeyboardShortcutMap? _shortcuts;

    /// <summary>Prevents re-entrant tab selection changes when we programmatically update the ListBox selection.</summary>
    private bool _isUpdatingSelection;
    private readonly Services.UI.FullscreenController _fullscreenController;
    private readonly ToolbarTintAdapter _tintAdapter;
    private readonly RoundedWebContentFrameController _frameController;
    private readonly TabActivationGuard _activationGuard = new();

    private const double LoadingIndicatorWidth = 100;
    private const double LoadingOvershootPx = 20;
    private const double FallbackContainerWidth = 900;
    private const double SweepDurationSeconds = 1.2;

    public MainWindow(IServiceProvider services, BrowserViewModel vm)
    {
        InitializeComponent();

        _fullscreenController = new Services.UI.FullscreenController(this, Toolbar);
        _tintAdapter = new ToolbarTintAdapter(Toolbar, StandardAddressBarBorder, StandardSuggestionsPopupBorder);

        _vm = vm;
        _services = services;
        // The frame controller owns the rounded clip on every page host in this window.
        // It reads the frame preference from the shared settings instance and publishes
        // the same geometry the inset owner uses, so the clip and the gap cannot drift.
        _frameController = new RoundedWebContentFrameController(this, WebViewHost, vm.Settings, ContentFrameOutline);
        Closed += (_, _) => _frameController.Dispose();
        _windows = services.GetRequiredService<BrowserWindowManager>();
        _windowContext = services.GetRequiredService<BrowserWindowContext>();
        _oneTabStore = services.GetRequiredService<IOneTabStore>();
        _sessionStore = services.GetRequiredService<ISessionStore>();
        _historyStore = services.GetRequiredService<IHistoryStore>();
        _tcLensLauncher = services.GetRequiredService<Services.UI.TCLensLauncher>();

        _engine = services.GetRequiredService<TabEngine>();
        _engine.AttachHost(WebViewHost);
        _tabStripController = new TabStripController(TabList, _engine, Dispatcher, FocusAddressBar);
        _toolbarDropController = new ToolbarDropController(TabList, StandardAddressBarBorder, NewTabButton,
            (input, target) => _tabStripController.OpenDroppedInputAsync(
                Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.IsFile ? uri.AbsoluteUri : _vm.ResolveInput(input), target),
            () => _engine.ActiveTab, _tabStripController.ActivateTabAsync);
        Closed += (_, _) => _tabStripController.Dispose();
        Closed += (_, _) => _toolbarDropController.Dispose();
        _siteInfoController = new SiteInfoController(SiteInfoButton, StandardSecurityIcon, SiteInfoPopup,
            SiteInfoHost, SiteInfoConnection, SiteInfoDescription, _vm.CloseSuggestions);
        Closed += (_, _) => _siteInfoController.Dispose();
        _downloadsController = new DownloadsPopupController(this, DownloadsButton, Toolbar, DownloadsPopup,
            DownloadsPanel, _engine, services.GetRequiredService<IDownloadStore>(), _vm.CloseSuggestions, DownloadsStatusDot);
        Closed += (_, _) => _downloadsController.Dispose();
        _extensionsController = new ExtensionsPopupController(this, ExtensionsButton, ExtensionsPopup, ExtensionsPanel,
            PinnedExtensions, _engine, services.GetRequiredService<Services.Extensions.ExtensionLibrary>(),
            services.GetRequiredService<ExtensionActions>(), _vm.CloseSuggestions);
        Closed += (_, _) => _extensionsController.Dispose();
        _toolbarNavigationController = new ToolbarNavigationController(RefreshButton, _engine);
        Closed += (_, _) => _toolbarNavigationController.Dispose();
        _windows.Register(this, _engine, TabList, services);
        // Only now that the window is registered can a drag hand its tab to a peer window
        // or tear one off, both of which look this window up by its strip and engine.
        _tabStripController.Drag.Transfer = new Services.UI.TabTransferHost(_engine, _windows);

        var linkPreviewController = services.GetRequiredService<Services.UI.LinkPreviewWindowController>();
        linkPreviewController.Attach(this);
        _readerVm = services.GetRequiredService<ReaderViewModel>();
        _readerService = services.GetRequiredService<IReaderService>();
        _linkPreviewVm = services.GetRequiredService<ViewModels.LinkPreview.LinkPreviewViewModel>();
        
        _linkPreviewVm.PropertyChanged += OnLinkPreviewViewModelPropertyChanged;
        _readerVm.PropertyChanged += OnReaderViewModelPropertyChanged;
        
        _engine.ActiveTabChanged += tab =>
        {
            Dispatcher.InvokeAsync(UpdateReaderOverlay);
        };
        _engine.TabClosed += tabId =>
        {
            _activationGuard.Forget(tabId);
            Dispatcher.InvokeAsync(UpdateReaderOverlay);
        };
        _readerService.SessionChanged += (s, tabId) => Dispatcher.InvokeAsync(UpdateReaderOverlay);


        _router = services.GetRequiredService<WebMessageRouter>();
        _router.SettingChanged += OnSettingChanged;
        var extensionActions = services.GetRequiredService<ExtensionActions>();
        extensionActions.SettingChanged += OnSettingChanged;
        Closed += (_, _) => extensionActions.SettingChanged -= OnSettingChanged;
        var themeManager = services.GetRequiredService<ThemeManager>();
        Action onThemeChanged = () =>
        {
            var themeStr = themeManager.GetThemeString();
            var js = $"document.documentElement.setAttribute('data-theme', '{themeStr}');";
            foreach (var tab in _engine.Tabs)
            {
                if (tab.Url?.StartsWith("internal://") == true || string.IsNullOrEmpty(tab.Url))
                {
                    _engine.ExecuteScript(tab.Id, js);
                }
            }
            _engine.ApplyAppThemeToWebViews();
            ApplyAppIcon();
            // App theme swapped the base colors the tint builds on, so the
            // adapter must re-resolve instead of trusting cached values.
            _tintAdapter.Reset();
            if (_engine.ActiveTab is not null)
                UpdateToolbarTint(_engine.ActiveTab);
        };
        // The theme manager is shared by every window, so this window's handler has to
        // come off when the window does.
        themeManager.ThemeChanged += onThemeChanged;
        Closed += (_, _) => themeManager.ThemeChanged -= onThemeChanged;

        DataContext = _vm;

        // Attach event handlers
        WireEngineEvents();

        Loaded += OnWindowLoaded;
    }
    private WindowChromeManager? _chromeManager;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        
        _chromeManager = new WindowChromeManager(this)
        {
            OnMouseHWheel = delta =>
            {
                if (TabList.IsMouseOver) _tabStripController.ScrollWheel(-delta);
            },
            // The inset owner resolves the combined gap from the same shared settings
            // instance the frame controller reads, so a live toggle moves both.
            Settings = _vm.Settings
        };
        _chromeManager.Initialize();

        _fullscreenController.ChromeManager = _chromeManager;
    }

    // ───────────────────── Initialization ─────────────────────

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _engine.InitializeAsync();
            // The window can be closed while the runtime is still starting, in which case
            // the manager has already cancelled Ready and nothing here should run.
            if (_windowContext.IsClosing) return;

            _shortcuts = BuildShortcutMap();
            _shortcuts.RebuildBindings(_vm.Settings.CustomShortcuts);

            // Only the startup window restores. A window torn off later must come up
            // holding the tab it was given, not the saved session and not a fresh tab:
            // the dock that follows this window's Ready puts that tab at slot zero.
            if (_windowContext.RestoreStartupSession) await RestoreSessionOrCreateTab();
            SyncTabsBinding();
            ApplyAppIcon();
            Dispatcher.BeginInvoke(new Action(ApplyTabShrink),
                System.Windows.Threading.DispatcherPriority.Loaded);

            if (_windowContext.RestoreStartupSession)
            {
                var startupCoordinator = _services.GetRequiredService<Services.Startup.StartupCoordinator>();
                await startupCoordinator.HandleCommandLineAndReleaseNotesAsync(Environment.GetCommandLineArgs().Skip(1).ToArray());
                if (!DefaultBrowserRegistrar.IsRegistered())
                    DefaultBrowserRegistrar.Register();
            }
            // Anything docking a tab into this window waits for this, so it is set only
            // once the window can actually show a tab.
            _windowContext.Ready.TrySetResult();


        }
        catch (Exception ex)
        {
            // A window that never became ready would hang anything waiting to dock into it.
            _windowContext.Ready.TrySetException(ex);
            Trace.WriteLine($"OnWindowLoaded failed: {ex}");
        }
    }

    private KeyboardShortcutMap BuildShortcutMap()
    {
        return new KeyboardShortcutMap(
            _engine,
            new ShortcutActions
            {
                FocusAddressBar = () => { FocusAddressBar(); return Task.CompletedTask; },
                SaveAllTabs = () => { SaveAllTabs_Click(this, new RoutedEventArgs()); return Task.CompletedTask; },
                CycleTab = async reverse => await CycleTabAsync(reverse),
                ToggleFullscreen = () => { _fullscreenController.Toggle(); return Task.CompletedTask; },
                IsFullscreen = () => _fullscreenController.IsFullscreen,
                OpenHistory = () => { OpenHistoryTab(); return Task.CompletedTask; },
                OpenDownloads = () => { OpenDownloadsTab(); return Task.CompletedTask; },
                SwitchToTabIndex = SwitchToTabByIndex,
                CopyUrl = CopyUrlToClipboard,
                SendAllToOneTab = () => _engine.SendAllToOneTab(),
                SaveOneTabGroup = entries =>
                {
                    var group = new OneTabGroup
                    {
                        Name = $"Saved {DateTime.Now:MMM d, h:mm tt}",
                        Tabs = entries.Select(t =>
                            new OneTabEntry(t.url, t.title, null, DateTime.UtcNow)).ToList()
                    };
                    _oneTabStore.AddGroup(group);
                },
                SyncTabsBinding = SyncTabsBinding,
                OpenOneTab = () => { OpenOneTabPage(); return Task.CompletedTask; },
                OpenSettings = () => { Settings_Click(this, new RoutedEventArgs()); return Task.CompletedTask; },
                LaunchTCLens = () => _tcLensLauncher.LaunchTCLensAsync(),
                ToggleReader = async () =>
                {
                    if (_engine.ActiveTab is { } tab && _engine.ToggleReaderAsync is { } toggle)
                        await toggle(tab.Id);
                }
            });
    }


    private async Task RestoreSessionOrCreateTab()
    {
        var restored = false;
        if (_vm.Settings.RestoreSessionOnStartup)
        {
            var session = _sessionStore.Load();
            if (session.Count > 0)
            {
                foreach (var entry in session)
                {
                    // Restore every saved row verbatim. Duplicates stay duplicated:
                    // the default duplicate blocker would collapse repeat URLs,
                    // shorten the list and misalign the saved active index.
                    var tab = _engine.CreateTab(entry.Url, blockDuplicates: false);
                    tab.Title = entry.Title;
                    tab.IsPinned = entry.IsPinned;
                    _ = _engine.LoadFaviconAsync(tab);
                }
                var activeIndex = SessionStore.ResolveActiveIndex(session);
                if (activeIndex >= _engine.Tabs.Count) activeIndex = 0;
                _engine.SwitchTo(_engine.Tabs[activeIndex]);
                await _engine.ActivateAsync(_engine.Tabs[activeIndex]);
                restored = true;
            }
        }

        if (!restored)
        {
            var tab = _engine.CreateTab();
            _engine.SwitchTo(tab);
            await _engine.ActivateAsync(tab);
        }
    }



    private void WireEngineEvents()
    {
        _engine.LaunchTCLensAsync = () => _tcLensLauncher.LaunchTCLensAsync();

        _engine.FullScreenChanged += isFullScreen => _fullscreenController.SetFullscreen(isFullScreen);

        _engine.TabStateChanged += async tab =>
        {
            if (tab.IsActive)
            {
                _vm.SyncNavigationState();
                // Display text the user never typed belongs to the old tab.
                // Refresh it on the new tab, but never clobber real typing.
                if (NavigationService.ShouldRefreshAddressDisplay(
                        TabList.SelectedItem != tab,
                        StandardAddressBar.IsKeyboardFocusWithin,
                        _vm.IsAddressEditedSinceDisplay))
                    _vm.SyncAddressBar(tab);
                _siteInfoController.Update(tab);
                UpdateUrlLabel(tab);
                _tintAdapter.UpdateTint(tab, _vm.Settings.AdaptiveToolbarTint);
                _isUpdatingSelection = true;
                try
                {
                    if (TabList.SelectedItem != tab)
                        TabList.SelectedItem = tab;
                }
                finally { _isUpdatingSelection = false; }

                // Metadata-only updates on the active tab carry no switch, so
                // they must not reactivate the WebView. That path cancels any
                // in-flight activation and resuspends background tabs. The
                // guard decides by active tab identity: a newly active tab,
                // including A/B/A returns and wake selections, activates.
                // UI refresh above still runs for every event.
                if (!_activationGuard.ShouldActivate(tab)) return;
                try
                {
                    await _engine.ActivateAsync(tab);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"TabStateChanged activation failed: {ex}");
                }
            }
        };

        _engine.LoadingStateChanged += (tab, isLoading) =>
        {
            if (tab.IsActive)
            {
                _vm.IsLoading = isLoading;
                _siteInfoController.Update(tab);
                _vm.SyncNavigationState();
                if (isLoading)
                {
                    LoadingBar.Visibility = Visibility.Visible;
                    StartLoadingAnimation();
                }
                else
                {
                    StopLoadingAnimation();
                    LoadingBar.Visibility = Visibility.Collapsed;
                }
            }
        };

        _engine.WebMessageReceived += HandleWebMessage;
        _engine.TabCreated += tab =>
        {
            EnsureTabVisible(tab);
        };
    }

    
    private void OnLinkPreviewViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.LinkPreview.LinkPreviewViewModel.IsVisible))
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_linkPreviewVm.IsVisible)
                {
                    LinkPreviewDim.Visibility = Visibility.Visible;
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    LinkPreviewDim.BeginAnimation(OpacityProperty, fadeIn);
                }
                else
                {
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    fadeOut.Completed += (_, _) =>
                    {
                        if (!_linkPreviewVm.IsVisible)
                        {
                            LinkPreviewDim.Visibility = Visibility.Collapsed;
                            LinkPreviewDim.BeginAnimation(OpacityProperty, null);
                        }
                    };
                    LinkPreviewDim.BeginAnimation(OpacityProperty, fadeOut);
                }
            });
        }
    }

    private void OnReaderViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReaderViewModel.Current) ||
            e.PropertyName == nameof(ReaderViewModel.Error) ||
            e.PropertyName == nameof(ReaderViewModel.IsInReader))
        {
            Dispatcher.InvokeAsync(UpdateReaderOverlay);
        }
    }

    private void UpdateReaderOverlay()
    {
        try
        {
            var isInReader = _readerVm.IsInReader;
            var current = _readerVm.Current;
            var error = _readerVm.Error;

            ReaderButton.Opacity = isInReader ? 1.0 : 0.7;
            ReaderButton.ToolTip = isInReader ? "Exit reader view (Ctrl+Shift+R)" : "Reader view (Ctrl+Shift+R)";

            if (!string.IsNullOrEmpty(error))
            {
                Trace.WriteLine("Reader error surfaced: " + error);
                Title = "Reader error: " + error;
                return;
            }

            var tabId = _engine.ActiveTab?.Id;
            if (tabId is null) return;
            var core = _engine.GetCoreForTab(tabId.Value);

            if (isInReader && current is not null)
            {
                if (core is null) return;
                try
                {
                    _engine.IsProgrammaticReaderNavigation = true;
                    try { core.Settings.IsScriptEnabled = false; } catch (Exception ex) { Trace.WriteLine("Reader disable script failed: " + ex.Message); }
                    core.NavigateToString(current.Html);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("Reader NavigateToString failed: " + ex);
                }
                finally
                {
                    Dispatcher.InvokeAsync(() => _engine.IsProgrammaticReaderNavigation = false, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            else if (!isInReader)
            {
                var session = _readerService.GetSession(tabId.Value);
                if (session is not null && !string.IsNullOrEmpty(session.OriginalUrl) && core is not null)
                {
                    var currentSource = core.Source ?? string.Empty;
                    var isReaderDocument = currentSource.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase) || currentSource == string.Empty;
                    if (isReaderDocument || currentSource != session.OriginalUrl)
                    {
                        try { core.Settings.IsScriptEnabled = true; } catch (Exception ex) { Trace.WriteLine("Reader re-enable script failed: " + ex.Message); }

                        if (currentSource.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase) || currentSource == "about:srcdoc")
                        {
                            try
                            {
                                _engine.IsProgrammaticReaderNavigation = true;
                                var tab = _engine.Tabs.FirstOrDefault(t => t.Id == tabId.Value);
                                if (tab is not null)
                                {
                                    tab.Url = session.OriginalUrl;
                                    _engine.Navigate(tab, session.OriginalUrl);
                                }
                            }
                            finally
                            {
                                Dispatcher.InvokeAsync(() => _engine.IsProgrammaticReaderNavigation = false, System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }
                        else
                        {
                            try { core.Settings.IsScriptEnabled = true; } catch { }
                        }
                    }
                }
                else if (core is not null)
                {
                    try { core.Settings.IsScriptEnabled = true; } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine("UpdateReaderOverlay failed: " + ex);
        }
    }
    private void LinkPreviewDim_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        LinkPreviewDim.Visibility = Visibility.Collapsed;
    }

    private void SyncTabsBinding()
    {
        TabList.ItemsSource = _engine.Tabs;
        Dispatcher.BeginInvoke(new Action(ApplyTabShrink),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private bool _appIconLight;

    /// <summary>
    /// Applies the theme aware app icon: dark skull everywhere by default,
    /// light skull only on the light theme. Covers the window taskbar icon
    /// and the tab fallback favicon.
    /// </summary>
    private void ApplyAppIcon()
    {
        try
        {
            var themeManager = _services.GetRequiredService<ThemeManager>();
            _appIconLight = !themeManager.IsCurrentlyDark();
            Icon = new BitmapImage(new Uri(_appIconLight
                ? "pack://application:,,,/icons/stride-light.ico"
                : "pack://application:,,,/icons/stride.ico"));
            _vm.FallbackIconUri = _appIconLight
                ? "pack://application:,,,/icons/stride-browser-light-32x32.png"
                : "pack://application:,,,/icons/stride-browser-dark-32x32.png";
        }
        catch (Exception ex) { Trace.WriteLine($"ApplyAppIcon failed: {ex.Message}"); }
    }

    /// <summary>
    /// Recalculates tab widths and keeps the active tab visible after a setting change.
    /// </summary>
    private void ApplyTabShrink() => _tabStripController.ApplyLayout();

    private void TabList_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        _tabStripController.OnPreviewMouseWheel(sender, e);

    private void EnsureTabVisible(BrowserTab tab) => _tabStripController.EnsureVisible(tab);

    private async void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection || _tabStripController.IsUpdatingSelection) return;

        var listBox = (ListBox)sender;
        if (listBox.SelectedItem is not BrowserTab tab || tab == _engine.ActiveTab) return;

        _isUpdatingSelection = true;
        try
        {
            _engine.SwitchTo(tab);
        }
        finally { _isUpdatingSelection = false; }

        // Landing on a new tab focuses the bar with everything selected,
        // so typing starts instantly. Queued after activation below.
        if (tab.Url == InternalUrls.NewTab)
            Dispatcher.BeginInvoke(new Action(FocusAddressBar), System.Windows.Threading.DispatcherPriority.Loaded);

        Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await _engine.ActivateAsync(tab);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"TabList_SelectionChanged activation failed: {ex}");
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void NewTab_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tab = _engine.CreateTab();
            _engine.SwitchTo(tab);
            await _engine.ActivateAsync(tab);
            FocusAddressBar();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"NewTab_Click failed: {ex}");
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BrowserTab tab }) _tabStripController.CloseTab(tab);
        e.Handled = true;
    }

    private async void Downloads_Click(object sender, RoutedEventArgs e)
    {
        await OpenDownloadsTab();
    }

    /// <summary>
    /// Opens the toolbar overflow menu under its button. The menu carries the action commands
    /// the row drops when it runs out of width.
    /// </summary>
    private void Extensions_Click(object sender, RoutedEventArgs e) => _ = OpenExtensionsAsync();
    private async Task OpenExtensionsAsync()
    { try { await _engine.OpenExtensionsAsync(); } catch (Exception ex) { Trace.WriteLine(ex); } }

    private void ToolbarOverflow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }


    private void TabAudio_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BrowserTab tab }) _engine.SetTabMuted(tab, !tab.IsMuted);
        e.Handled = true;
    }

    // ───────────────────── Navigation ─────────────────────



    private void StandardAddressBar_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!StandardAddressBar.IsKeyboardFocusWithin)
        {
            Keyboard.Focus(StandardAddressBar);
            StandardAddressBar.SelectAll();
            e.Handled = true;
        }
    }


    // ───────────────────── Address Bar ─────────────────────

    private void FocusAddressBar()
    {
        StandardAddressBar.Focus();
        if (_engine.ActiveTab is { } activeTab && !string.IsNullOrEmpty(activeTab.Url) && !InternalUrls.IsInternal(activeTab.Url))
        {
            StandardAddressBar.Text = activeTab.Url;
        }
        else
        {
            StandardAddressBar.Text = "";
        }
        _vm.NoteAddressDisplayed(StandardAddressBar.Text);
        StandardAddressBar.SelectAll();
    }

    private void StandardAddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.RestoreTypedQuery();
            _vm.CloseSuggestions();
            UpdateUrlLabel(_engine.ActiveTab);
            WebViewHost.Focus();
            e.Handled = true;
            return;
        }

        // The arrow keys can move the highlight into the list, which puts a
        // suggestion in the box that the user never typed. A delete must hand
        // the typed query back rather than start eating the suggestion.
        if (e.Key is Key.Back or Key.Delete && _vm.IsSuggestionTextDisplayed)
        {
            _vm.RestoreTypedQuery();
            _vm.ShowSuggestions = true;
            StandardAddressBar.Select(StandardAddressBar.Text.Length, 0);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && _vm.ShowSuggestions)
        {
            _vm.MoveSuggestionHighlight(1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && _vm.ShowSuggestions)
        {
            _vm.MoveSuggestionHighlight(-1);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var input = _vm.TakeHighlightedInput() ?? _vm.ResolveBarInput(StandardAddressBar.Text);

        if (string.IsNullOrEmpty(input)) return;

        var url = _vm.ResolveInput(input);
        if (_engine.ActiveTab is { } tab)
        {
            tab.Url = url;
            _vm.ShowNavigatedUrl(url);
            _engine.Navigate(tab, url);
        }
        
        _vm.CloseSuggestions();
        WebViewHost.Focus();
    }

    private void StandardAddressBar_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _vm.RevealFullUrl();
        StandardAddressBar.Dispatcher.InvokeAsync(() => StandardAddressBar.SelectAll());
    }

    private void StandardAddressBar_LostFocus(object sender, RoutedEventArgs e)
    {
        // Do not auto-close to allow MouseUp to fire on the list
    }

    private void StandardAddressBar_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (sender is not TextBox tb || !tb.IsKeyboardFocusWithin) return;
        if (_vm.SuppressSuggestionRefresh) { _vm.SuppressSuggestionRefresh = false; return; }
        _vm.MarkAddressEdited();
        _ = _vm.UpdateSuggestionsAsync(tb.Text);
    }

    private void StandardSuggestionsList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var input = _vm.TakeHighlightedInput();
        if (input is not null)
        {
            var url = _vm.ResolveInput(input);
            if (_engine.ActiveTab is { } tab)
            {
                tab.Url = url;
                _vm.ShowNavigatedUrl(url);
                _engine.Navigate(tab, url);
            }
            _vm.CloseSuggestions();
            WebViewHost.Focus();
        }
    }

    private void UpdateUrlLabel(BrowserTab? tab)
    {
        if (tab == null) return;

        // Never overwrite an active edit. Metadata updates like favicon or
        // title changes arrive through the same event as navigations.
        if (!StandardAddressBar.IsKeyboardFocusWithin)
        {
            StandardAddressBar.Text = NavigationService.ToDisplayText(tab.Url, tab.Title);
        }
    }

    // ───────────────────── Adaptive Site Tinting ─────────────────────
    // The per-window adapter paints the toolbar, address field, suggestion
    // popup and tab cards from the page theme color. See ToolbarTintAdapter.

    private void UpdateToolbarTint(BrowserTab tab) =>
        _tintAdapter.UpdateTint(tab, _vm.Settings.AdaptiveToolbarTint);


    // ───────────────────── OneTab ─────────────────────

    private async void OneTab_Click(object sender, RoutedEventArgs e) => await OpenOneTabPage();

    private async Task OpenOneTabPage()
    {
        try
        {
            var tab = _engine.CreateTab(InternalUrls.OneTab);
            tab.Title = "Saved tabs";
            _engine.SwitchTo(tab);
            // Creation serves the page through routing; no second navigation here.
            await _engine.ActivateAsync(tab);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"OneTab_Click failed: {ex}");
        }
    }

    private async void SaveAllTabs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_engine.Tabs.Count == 0) return;

            var now = DateTime.UtcNow;
            var group = new OneTabGroup
            {
                Name = now.ToLocalTime().ToString("MMM d, h:mm tt"),
                SavedAt = now,
                Tabs = _engine.Tabs
                    .Where(t => !string.IsNullOrWhiteSpace(t.Url)
                        && !InternalUrls.IsInternal(t.Url)
                        && !t.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                        && !t.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                    .Select(t => new OneTabEntry(t.Url, t.Title, null, now))
                    .ToList()
            };

            if (group.Tabs.Count == 0) return;

            _oneTabStore.AddGroup(group);

            var freshTab = _engine.CreateTab();
            _engine.SwitchTo(freshTab);
            await _engine.ActivateAsync(freshTab);

            var oldTabs = _engine.Tabs.Where(t => t.Id != freshTab.Id).ToList();
            _isUpdatingSelection = true;
            try
            {
                foreach (var old in oldTabs)
                    _engine.CloseTab(old);
            }
            finally { _isUpdatingSelection = false; }

            FocusAddressBar();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SaveAllTabs_Click failed: {ex}");
        }
    }

    // ───────────────────── Web Messages (delegated to router) ─────

    private async void HandleWebMessage(string message)
    {
        try
        {
            await _router.RouteAsync(message);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"HandleWebMessage failed: {ex}");
        }
    }

    private void OnSettingChanged(string key, string value)
    {
        if (key == "shortcutRecording" && _shortcuts is not null) _shortcuts.IsRecording = value == "true";
        if (key is "appTheme" or "reset")
        {
            var tm = _services.GetRequiredService<ThemeManager>();
            var themeStr = tm.GetThemeString();
            var js = $"document.documentElement.setAttribute('data-theme', '{themeStr}');";
            foreach (var tab in _engine.Tabs)
            {
                if (tab.Url?.StartsWith("internal://") == true || string.IsNullOrEmpty(tab.Url))
                {
                    _engine.ExecuteScript(tab.Id, js);
                }
            }
            _engine.ApplyAppThemeToWebViews();
            _tintAdapter.Reset();
            if (_engine.ActiveTab is not null)
                UpdateToolbarTint(_engine.ActiveTab);
        }

        if (key == "adaptiveTint" && _engine.ActiveTab is not null)
        {
            _tintAdapter.Reset();
            UpdateToolbarTint(_engine.ActiveTab);
        }

        // Live-rebuild shortcut bindings when user rebinds a key
        if (key is "shortcut" or "shortcutReset" or "reset")
            _shortcuts?.RebuildBindings(_vm.Settings.CustomShortcuts);

        // Removed: Reloading the settings page here causes it to jump back to the General tab
        // whenever the user changes any setting. The frontend UI updates itself.

        // Live-refresh new tab pages when shortcuts are added/removed
        if (key == "shortcuts")
        {
            var shortcutsJson = System.Text.Json.JsonSerializer.Serialize(_vm.Settings.NewTabShortcuts);
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(shortcutsJson));
            var js = $"if (typeof updateShortcutsFromB64 === 'function') updateShortcutsFromB64('{b64}');";
            
            foreach (var tab in _engine.Tabs)
            {
                if (tab.Url == InternalUrls.NewTab)
                    _engine.ExecuteScript(tab.Id, js);
            }
        }
    }

    // ──── Keyboard shortcuts ────

    /// <summary>True while the dropdown is open and the address box holds logical focus.</summary>
    private bool IsAddressBoxLogicallyFocused()
    {
        var focused = FocusManager.GetFocusedElement(this);
        return ReferenceEquals(focused, StandardAddressBar);
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_shortcuts?.IsRecording == true) return;
        try
        {
            if (e.Key == Key.Escape && _tabStripController.CancelDrag())
            {
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && _vm.IsLoading && !_vm.ShowSuggestions)
            {
                _engine.StopLoading();
            }
            // Suggestion navigation lives here, not only on the address boxes:
            // arrow keys have been observed dying between the window tunnel and
            // the box when keyboard focus is lost, so the window drives the
            // highlight whenever the dropdown is open and an address box holds
            // logical focus. Handling here also stops a double move below.
            if ((e.Key == Key.Down || e.Key == Key.Up) && _vm.ShowSuggestions && IsAddressBoxLogicallyFocused())
            {
                if (!StandardAddressBar.IsKeyboardFocusWithin)
                    StandardAddressBar.Focus();
                _vm.MoveSuggestionHighlight(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                return;
            }

            if (_shortcuts is not null)
            {
                var modifiers = Keyboard.Modifiers;
                var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
                
                if (modifiers == ModifierKeys.Alt && actualKey == Key.T)
                {
                    await _tcLensLauncher.HandleNativeTCLensShortcutAsync();
                    e.Handled = true;
                    return;
                }

                var handled = await _shortcuts.TryExecuteAsync(modifiers, actualKey);
                if (handled)
                {
                    e.Handled = true;
                    return;
                }
                if (modifiers == ModifierKeys.Control && actualKey >= Key.D1 && actualKey <= Key.D9)
                {
                    var tabIndex = actualKey - Key.D1;
                    if (actualKey == Key.D9)
                        tabIndex = _engine.Tabs.Count - 1;
                    await SwitchToTabByIndex(tabIndex);
                    e.Handled = true;
                    return;
                }

                // Alt+1–9: also switch tabs (user preference)
                if (modifiers == ModifierKeys.Alt && actualKey >= Key.D1 && actualKey <= Key.D9)
                {
                    var tabIndex = actualKey - Key.D1;
                    if (actualKey == Key.D9)
                        tabIndex = _engine.Tabs.Count - 1;
                    await SwitchToTabByIndex(tabIndex);
                    e.Handled = true;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"PreviewKeyDown failed: {ex}");
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tab = _engine.CreateTab(InternalUrls.Settings);
            tab.Title = "Settings";
            _engine.SwitchTo(tab);
            // Creation serves the page through routing; no second navigation here.
            await _engine.ActivateAsync(tab);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Settings_Click failed: {ex}");
        }
    }

    

    // ───────────────────── Title Bar ─────────────────────

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            ToggleMaximize();
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); }
            catch (InvalidOperationException) { /* Mouse released during drag */ }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // ───────────────────── Tab Drag & Drop ─────────────────────

    private void ZoomIndicator_Click(object sender, MouseButtonEventArgs e)
    {
        _engine.ResetZoom();
    }

    // OnKeyDown logic moved to Window_PreviewKeyDown to handle WebView2 focus

    private async Task SwitchToTabByIndex(int tabIndex)
    {
        if (tabIndex >= 0 && tabIndex < _engine.Tabs.Count)
        {
            _engine.SwitchTo(_engine.Tabs[tabIndex]);
            await _engine.ActivateAsync(_engine.Tabs[tabIndex]);
        }
    }

    private async Task OpenHistoryTab()
    {
        try
        {
            var tab = _engine.CreateTab(InternalUrls.History);
            tab.Title = "History";
            _engine.SwitchTo(tab);
            // Creation serves the page through routing; no second navigation here.
            await _engine.ActivateAsync(tab);
        }
        catch (Exception ex) { Trace.WriteLine($"History open failed: {ex}"); }
    }

    private Task OpenDownloadsTab() => _engine.OpenDownloadsAsync();

    private System.Windows.Threading.DispatcherTimer? _copyTimer;

    private void CopyUrlToClipboard(string url)
    {
        Clipboard.SetText(url);
        Title = "URL copied!";
        _copyTimer?.Stop();
        _copyTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _copyTimer.Tick += OnCopyTimerTick;
        _copyTimer.Start();
    }

    private void OnCopyTimerTick(object? sender, EventArgs e)
    {
        if (_copyTimer is not null)
        {
            _copyTimer.Stop();
            _copyTimer.Tick -= OnCopyTimerTick;
            _copyTimer = null;
        }
        Title = "Stride";
    }

    private async Task CycleTabAsync(bool reverse)
    {
        if (_engine.Tabs.Count <= 1 || _engine.ActiveTab is null) return;

        var index = _engine.Tabs.IndexOf(_engine.ActiveTab);
        var next = reverse
            ? (index - 1 + _engine.Tabs.Count) % _engine.Tabs.Count
            : (index + 1) % _engine.Tabs.Count;

        _engine.SwitchTo(_engine.Tabs[next]);
        await _engine.ActivateAsync(_engine.ActiveTab!);
    }

    // ───────────────────── Cleanup ─────────────────────

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // One shutdown owner for the whole app. The manager marks this window closing,
        // cancels anything waiting on it, saves the session across the windows that
        // survive, hands tabs whose control is bound here to their new windows, and
        // then closes the window for real.
        _windows.Close(this, e);
        base.OnClosing(e);
    }

    private Storyboard? _loadingStoryboard;

    private void StartLoadingAnimation()
    {
        StopLoadingAnimation();

        var transform = new TranslateTransform();
        LoadingIndicator.RenderTransform = transform;

        var containerWidth = LoadingBar.ActualWidth > 0 ? LoadingBar.ActualWidth : FallbackContainerWidth;
        var anim = new DoubleAnimation
        {
            From = -LoadingIndicatorWidth,
            To = containerWidth + LoadingOvershootPx,
            Duration = TimeSpan.FromSeconds(SweepDurationSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        _loadingStoryboard = new Storyboard();
        Storyboard.SetTarget(anim, LoadingIndicator);
        Storyboard.SetTargetProperty(anim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
        _loadingStoryboard.Children.Add(anim);
        _loadingStoryboard.Begin();
    }

    private void StopLoadingAnimation()
    {
        _loadingStoryboard?.Stop();
        _loadingStoryboard = null;
    }
}



