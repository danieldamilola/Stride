using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using StrideBrowser.Engine;
using StrideBrowser.Interop;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Input;
using StrideBrowser.Services.Reader;
using StrideBrowser.Services.UI;
using StrideBrowser.ViewModels;
using StrideBrowser.ViewModels.Reader;
using StrideBrowser.ViewModels.LinkPreview;

namespace StrideBrowser;

/// <summary>
/// Thin view layer coordinating UI controllers, shortcuts, and tab strip bindings.
/// </summary>
public partial class MainWindow : Window
{
    private readonly BrowserViewModel _vm;
    private readonly ReaderViewModel _readerVm;
    private readonly TabEngine _engine;
    private readonly IOneTabStore _oneTabStore;
    private readonly ISettingsStore _settingsStore;
    private readonly ISessionStore _sessionStore;
    private readonly IHistoryStore _historyStore;
    private readonly IDownloadStore _downloadStore;
    private readonly WebMessageRouter _router;
    private readonly ThemeManager _themeManager;
    private readonly IReaderService _readerService;

    private readonly CommandBarController _commandBar;
    private readonly TabStripController _tabStrip;
    private readonly WindowLifecycleController _lifecycle;
    private readonly TCLensLauncher _tcLensLauncher;
    private readonly ToolbarTintAdapter _toolbarTint;
    private readonly SecurityBadgeHelper _securityBadge;
    private readonly LoadingAnimationController _loadingAnim;
    private readonly LinkPreviewWindowController _linkPreviewController;
    private readonly LinkPreviewViewModel _linkPreviewVm;

    private KeyboardShortcutMap? _shortcuts;
    private WindowChromeManager? _chromeManager;
    private System.Windows.Threading.DispatcherTimer? _copyTimer;
    private WindowState _preFullscreenWindowState = WindowState.Normal;

    public MainWindow(
        BrowserViewModel vm,
        ReaderViewModel readerVm,
        LinkPreviewViewModel linkPreviewVm,
        LinkPreviewWindowController linkPreviewController,
        ISettingsStore settingsStore,
        IOneTabStore oneTabStore,
        ISessionStore sessionStore,
        IHistoryStore historyStore,
        IDownloadStore downloadStore,
        TabEngine engine,
        WebMessageRouter router,
        TCLensTransferService tcLensTransfer,
        ThemeManager themeManager,
        UpdateService updateService,
        IReaderService readerService)
    {
        InitializeComponent();

        _vm = vm;
        _readerVm = readerVm;
        _linkPreviewVm = linkPreviewVm;
        _linkPreviewController = linkPreviewController;
        _settingsStore = settingsStore;
        _oneTabStore = oneTabStore;
        _sessionStore = sessionStore;
        _historyStore = historyStore;
        _downloadStore = downloadStore;
        _engine = engine;
        _router = router;
        _themeManager = themeManager;
        _readerService = readerService;

        _engine.AttachHost(WebViewHost);
        _linkPreviewController.Attach(this);
        _linkPreviewVm.PropertyChanged += OnLinkPreviewViewModelPropertyChanged;

        // Reader mode wiring. Single shared VM that mirrors active tab, service owns per tab truth.
        _readerVm.PropertyChanged += OnReaderViewModelPropertyChanged;
        _engine.ActiveTabChanged += tab =>
        {
            _readerVm.OnActiveTabChanged(tab?.Id);
            Dispatcher.InvokeAsync(UpdateReaderOverlay);
        };
        _engine.TabClosed += tabId => Dispatcher.InvokeAsync(UpdateReaderOverlay);
        _readerService.SessionChanged += (s, tabId) => Dispatcher.InvokeAsync(UpdateReaderOverlay);

        _router.SettingChanged += OnSettingChanged;
        _router.AddressChanged += OnRouterAddressChanged;

        updateService.UpdateAvailable += (s, e) =>
        {
            Dispatcher.Invoke(() => UpdateBadge.Visibility = Visibility.Visible);
        };

        updateService.AppExitRequested += () => Dispatcher.Invoke(() => Application.Current.Shutdown());

        _commandBar = new CommandBarController(
            _vm, _engine, CommandBarGrid, CommandBarPanel, AddressBar, StandardAddressBar, UrlLabel, WebViewHost, Dispatcher);

        _tabStrip = new TabStripController(
            TabList, _engine, Dispatcher, () => _commandBar.FocusAddressBar());

        _tcLensLauncher = new TCLensLauncher(_engine, tcLensTransfer);

        _lifecycle = new WindowLifecycleController(
            this,
            Toolbar,
            _engine,
            _vm,
            _settingsStore,
            _sessionStore,
            OnPostInit,
            () => _chromeManager?.BringToFront());

        new TabStripDragDropBehavior(TabList, (oldIdx, newIdx) =>
        {
            _tabStrip.IsUpdatingSelection = true;
            try { _engine.Tabs.Move(oldIdx, newIdx); }
            finally { _tabStrip.IsUpdatingSelection = false; }
        });

        _toolbarTint = new ToolbarTintAdapter(Toolbar);
        _securityBadge = new SecurityBadgeHelper(SecurityIcon, StandardSecurityIcon, _vm.Settings);
        _loadingAnim = new LoadingAnimationController(LoadingIndicator, LoadingBar);

        _themeManager.ThemeChanged += OnThemeManagerChanged;

        DataContext = _vm;

        WireEngineEvents();

        if (_vm.Settings.AccentColor != "#7fb89a")
            ApplyAccentColor(_vm.Settings.AccentColor);

        Loaded += _lifecycle.OnWindowLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _chromeManager = new WindowChromeManager(this)
        {
            OnMouseHWheel = delta =>
            {
                var scrollViewer = TabStripController.GetScrollViewer(TabList);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + (delta * 0.5));
                }
            }
        };
        _chromeManager.Initialize();
    }

    private void OnPostInit()
    {
        _shortcuts = BuildShortcutMap();
        _shortcuts.RebuildBindings(_vm.Settings.CustomShortcuts);
        _tabStrip.SyncTabsBinding();
    }

    private void OnThemeManagerChanged()
    {
        var themeStr = _themeManager.GetThemeString();
        var js = $"document.documentElement.setAttribute('data-theme', '{themeStr}');";
        foreach (var tab in _engine.Tabs)
        {
            if (tab.Url?.StartsWith("internal://") == true || string.IsNullOrEmpty(tab.Url))
            {
                _engine.ExecuteScript(tab.Id, js);
            }
        }
        _engine.ApplyAppThemeToWebViews();
    }

    private KeyboardShortcutMap BuildShortcutMap()
    {
        return new KeyboardShortcutMap(
            _engine,
            new ShortcutActions
            {
                FocusAddressBar = () => { _commandBar.FocusAddressBar(); return Task.CompletedTask; },
                SaveAllTabs = () => { SaveAllTabs_Click(this, new RoutedEventArgs()); return Task.CompletedTask; },
                CycleTab = async reverse => await _tabStrip.CycleTabAsync(reverse),
                ToggleFullscreen = () => { _lifecycle.ToggleFullscreen(); return Task.CompletedTask; },
                IsFullscreen = () => _lifecycle.IsFullscreen,
                OpenHistory = OpenHistoryTab,
                OpenDownloads = OpenDownloadsTab,
                SwitchToTabIndex = _tabStrip.SwitchToTabByIndex,
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
                SyncTabsBinding = _tabStrip.SyncTabsBinding,
                OpenOneTab = OpenOneTabPage,
                OpenSettings = () => { Settings_Click(this, new RoutedEventArgs()); return Task.CompletedTask; },
                LaunchTCLens = _tcLensLauncher.LaunchTCLensAsync,
                ToggleReader = async () => { await _readerVm.ToggleAsync(); }
            });
    }

    private void WireEngineEvents()
    {
        _engine.FullScreenChanged += isFullScreen =>
        {
            if (isFullScreen)
            {
                _preFullscreenWindowState = WindowState;
                Toolbar.Visibility = Visibility.Collapsed;
                WindowState = WindowState.Normal;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else
            {
                Toolbar.Visibility = Visibility.Visible;
                ResizeMode = ResizeMode.CanResize;
                WindowState = _preFullscreenWindowState;
            }
        };

        _engine.TabStateChanged += async tab =>
        {
            if (tab.IsActive)
            {
                _vm.SyncAddressBar(tab);
                _securityBadge.UpdateSecurityIcon(tab.Url);
                _commandBar.UpdateUrlLabel(tab);
                _toolbarTint.UpdateTint(tab);
                _tabStrip.HandleTabSelection(tab);

                try
                {
                    await _engine.ActivateAsync(tab);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"TabStateChanged activation failed: {ex}");
                }
            }
        };

        _engine.LoadingStateChanged += (tab, isLoading) =>
        {
            if (tab.IsActive)
            {
                _vm.IsLoading = isLoading;
                if (isLoading)
                {
                    LoadingBar.Visibility = Visibility.Visible;
                    _loadingAnim.Start();
                    _securityBadge.StartSecuritySpinner();
                }
                else
                {
                    _loadingAnim.Stop();
                    _securityBadge.StopSecuritySpinner();
                    LoadingBar.Visibility = Visibility.Collapsed;
                    _securityBadge.UpdateSecurityIcon(tab.Url);
                }
            }
        };

        _engine.WebMessageReceived += HandleWebMessage;
    }

    // ───────────────────── Tab Strip Event Handlers ─────────────────────

    private void TabList_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        _tabStrip.OnPreviewMouseWheel(sender, e);

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _tabStrip.OnSelectionChanged(sender, e);

    private async void NewTab_Click(object sender, RoutedEventArgs e) =>
        await _tabStrip.CreateNewTabAsync();

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is BrowserTab tab)
            _tabStrip.CloseTab(tab);
    }

    private void TabItem_RightClick(object sender, MouseButtonEventArgs e) =>
        _tabStrip.OnTabItemRightClick(sender, e);

    // ───────────────────── Navigation & Address Bar ─────────────────────

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.ShowSuggestions = false;
            _commandBar.HideCommandBar();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && _vm.ShowSuggestions)
        {
            if (_vm.SelectedSuggestionIndex < _vm.Suggestions.Count - 1)
                _vm.SelectedSuggestionIndex++;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && _vm.ShowSuggestions)
        {
            if (_vm.SelectedSuggestionIndex > 0)
                _vm.SelectedSuggestionIndex--;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter) return;
        e.Handled = true;

        string? input;
        if (_vm.ShowSuggestions && _vm.SelectedSuggestionIndex >= 0
            && _vm.SelectedSuggestionIndex < _vm.Suggestions.Count)
        {
            input = _vm.Suggestions[_vm.SelectedSuggestionIndex];
        }
        else
        {
            input = (sender as TextBox)?.Text?.Trim() ?? _vm.AddressText?.Trim();
        }

        if (string.IsNullOrEmpty(input)) return;

        var url = _vm.ResolveInput(input);
        if (_engine.ActiveTab is { } tab)
        {
            tab.Url = url;
            _vm.AddressText = url;
            _engine.Navigate(tab, url);
        }

        _vm.ShowSuggestions = false;
        _commandBar.HideCommandBar();
    }

    private void AddressBar_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.SelectAll();
    }

    private void AddressBar_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!AddressBar.IsKeyboardFocusWithin)
        {
            Keyboard.Focus(AddressBar);
            AddressBar.SelectAll();
            e.Handled = true;
        }
    }

    private void AddressBar_LostFocus(object sender, RoutedEventArgs e) { }

    private void AddressBar_TextChanged(object sender, TextChangedEventArgs e) =>
        _commandBar.HandleAddressTextChanged(sender as TextBox);

    private void UrlLabel_Click(object sender, MouseButtonEventArgs e)
    {
        _commandBar.FocusAddressBar();
        e.Handled = true;
    }

    private void StandardAddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.ShowSuggestions = false;
            if (_engine.ActiveTab != null)
                _commandBar.UpdateUrlLabel(_engine.ActiveTab);
            WebViewHost.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && _vm.ShowSuggestions)
        {
            if (_vm.SelectedSuggestionIndex < _vm.Suggestions.Count - 1)
                _vm.SelectedSuggestionIndex++;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && _vm.ShowSuggestions)
        {
            if (_vm.SelectedSuggestionIndex > 0)
                _vm.SelectedSuggestionIndex--;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter) return;
        e.Handled = true;

        string? input;
        if (_vm.ShowSuggestions && _vm.SelectedSuggestionIndex >= 0 && _vm.SelectedSuggestionIndex < _vm.Suggestions.Count)
        {
            input = _vm.Suggestions[_vm.SelectedSuggestionIndex];
        }
        else
        {
            input = StandardAddressBar.Text?.Trim();
        }

        if (string.IsNullOrEmpty(input)) return;

        var url = _vm.ResolveInput(input);
        if (_engine.ActiveTab is { } tab)
        {
            tab.Url = url;
            _vm.AddressText = url;
            _engine.Navigate(tab, url);
        }

        _vm.ShowSuggestions = false;
        WebViewHost.Focus();
    }

    private void StandardAddressBar_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        StandardAddressBar.Dispatcher.InvokeAsync(() => StandardAddressBar.SelectAll());

    private void StandardAddressBar_LostFocus(object sender, RoutedEventArgs e) { }

    private void StandardAddressBar_TextChanged(object sender, TextChangedEventArgs e) =>
        _commandBar.HandleAddressTextChanged(sender as TextBox);

    private void StandardSuggestionsList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedSuggestionIndex >= 0 && _vm.SelectedSuggestionIndex < _vm.Suggestions.Count)
        {
            _commandBar.NavigateToSuggestion(_vm.Suggestions[_vm.SelectedSuggestionIndex]);
            WebViewHost.Focus();
        }
    }

    private void SuggestionsList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedSuggestionIndex >= 0 && _vm.SelectedSuggestionIndex < _vm.Suggestions.Count)
        {
            _commandBar.NavigateToSuggestion(_vm.Suggestions[_vm.SelectedSuggestionIndex]);
            _commandBar.HideCommandBar();
        }
    }

    private void CommandBarBackdrop_Click(object sender, MouseButtonEventArgs e) =>
        _commandBar.HideCommandBar();

    // ───────────────────── Internal Pages ─────────────────────

    private async void OneTab_Click(object sender, RoutedEventArgs e) => await OpenOneTabPage();

    private async Task OpenOneTabPage()
    {
        try
        {
            var tab = _engine.CreateTab(InternalUrls.OneTab);
            tab.Title = "OneTab";
            _engine.SwitchTo(tab);
            await _engine.ActivateAsync(tab);
            _engine.NavigateToOneTab(tab, _oneTabStore.Load());
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
            _tabStrip.IsUpdatingSelection = true;
            try
            {
                foreach (var old in oldTabs)
                    _engine.CloseTab(old);
            }
            finally { _tabStrip.IsUpdatingSelection = false; }

            _commandBar.FocusAddressBar();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SaveAllTabs_Click failed: {ex}");
        }
    }

    private async void Downloads_Click(object sender, RoutedEventArgs e) => await OpenDownloadsTab();

    private async Task OpenDownloadsTab()
    {
        try
        {
            var tab = _engine.CreateTab(InternalUrls.Downloads);
            tab.Title = "Downloads";
            _engine.SwitchTo(tab);
            await _engine.ActivateAsync(tab);
            _engine.NavigateToDownloads(tab);
        }
        catch (Exception ex) { Trace.WriteLine($"Downloads open failed: {ex}"); }
    }

    private async Task OpenHistoryTab()
    {
        try
        {
            var tab = _engine.CreateTab(InternalUrls.History);
            tab.Title = "History";
            _engine.SwitchTo(tab);
            await _engine.ActivateAsync(tab);
            var entries = _historyStore.Load();
            _engine.NavigateToHistory(tab, entries);
        }
        catch (Exception ex) { Trace.WriteLine($"History open failed: {ex}"); }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tab = _engine.CreateTab(InternalUrls.Settings);
            tab.Title = "Settings";
            _engine.SwitchTo(tab);
            await _engine.ActivateAsync(tab);
            _engine.NavigateToSettings(tab, _vm.Settings);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Settings_Click failed: {ex}");
        }
    }

    // ───────────────────── Web Message Dispatch ─────────────────────

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

    private void OnRouterAddressChanged(string url) => _vm.AddressText = url;

    private void OnSettingChanged(string key, string _)
    {
        if (key == "darkMode")
        {
            var msg = _vm.Settings.ForceDarkMode ? "Dark mode enabled" : "Dark mode disabled";
            var js = $@"(function() {{
                var existing = document.getElementById('stride-restart-banner');
                if (existing) existing.remove();
                var banner = document.createElement('div');
                banner.id = 'stride-restart-banner';
                banner.style.cssText = 'position:fixed;bottom:24px;left:50%;transform:translateX(-50%);background:#1a1a2e;color:#e0e0e0;border:1px solid rgba(255,255,255,0.12);border-radius:12px;padding:14px 22px;font-size:14px;font-family:inherit;display:flex;align-items:center;gap:14px;z-index:99999;box-shadow:0 8px 32px rgba(0,0,0,0.5);';
                banner.innerHTML = '<span style=""font-size:18px"">🌙</span><span><strong>{msg}</strong> - Restart Stride to apply</span><button onclick=""this.parentElement.remove()"" style=""background:rgba(255,255,255,0.1);border:none;color:#e0e0e0;border-radius:8px;padding:6px 12px;cursor:pointer;font-size:13px;margin-left:8px;"">Dismiss</button>';
                document.body.appendChild(banner);
                setTimeout(() => {{ if(banner.parentNode) banner.remove(); }}, 6000);
            }})();";
            foreach (var tab in _engine.Tabs)
            {
                if (tab.Url?.StartsWith("internal://settings") == true)
                    _engine.ExecuteScript(tab.Id, js);
            }
        }

        if (key == "appTheme")
        {
            OnThemeManagerChanged();
        }

        if (key == "accentColor")
            ApplyAccentColor(_vm.Settings.AccentColor);

        if (key is "shortcut" or "shortcutReset")
            _shortcuts?.RebuildBindings(_vm.Settings.CustomShortcuts);

        if (key == "shortcuts")
        {
            foreach (var tab in _engine.Tabs)
            {
                if (tab.Url == InternalUrls.NewTab)
                    _engine.Navigate(tab, InternalUrls.NewTab);
            }
        }
    }

    private void ApplyAccentColor(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            Application.Current.Resources["AccentColor"] = color;
            Application.Current.Resources["Accent"] = brush;

            var wash = new SolidColorBrush(Color.FromArgb(0x1A, color.R, color.G, color.B));
            wash.Freeze();
            Application.Current.Resources["AccentWash"] = wash;

            Application.Current.Resources["LoadingBarGlowColor"] = color;
            Application.Current.Resources["LoadingBarFadeColor"] = Color.FromArgb(0x00, color.R, color.G, color.B);
        }
        catch { /* ignore invalid color strings */ }
    }

    // ───────────────────── Keyboard Shortcuts & Input ─────────────────────

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (_linkPreviewVm.IsVisible && (e.Key == Key.Escape || e.Key == Key.System && e.SystemKey == Key.Escape))
            {
                _linkPreviewVm.Dismiss();
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
                    await _tabStrip.SwitchToTabByIndex(tabIndex);
                    e.Handled = true;
                    return;
                }

                if (modifiers == ModifierKeys.Alt && actualKey >= Key.D1 && actualKey <= Key.D9)
                {
                    var tabIndex = actualKey - Key.D1;
                    if (actualKey == Key.D9)
                        tabIndex = _engine.Tabs.Count - 1;
                    await _tabStrip.SwitchToTabByIndex(tabIndex);
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

    // ───────────────────── Title Bar & Window Chrome ─────────────────────

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

    private void ZoomIndicator_Click(object sender, MouseButtonEventArgs e) =>
        _engine.ResetZoom();

    // ───────────────────── Reader Fallback Overlay ─────────────────────
    // Code-behind references for the fallback overlay and the standard WebView2.
    // ReaderFallbackOverlay, ReaderFallbackTitle, ReaderFallbackByline, ReaderFallbackText,
    // and ReaderFallbackError are generated from MainWindow.xaml x:Name.
    // ActiveStandardWebView is the current tab's WebView2 when UseFloatingCommandBar is disabled (HwndHost).

    private FrameworkElement? ActiveStandardWebView
    {
        get
        {
            if (_engine.ActiveTab == null) return null;
            return _engine.GetWebViewElement(_engine.ActiveTab.Id);
        }
    }

    private void ShowReaderFallback(string title, string byline, string text, string? error = null)
    {
        ReaderFallbackTitle.Text = title;
        ReaderFallbackByline.Text = byline;
        ReaderFallbackText.Text = text;
        if (!string.IsNullOrEmpty(error))
        {
            ReaderFallbackError.Text = error;
            ReaderFallbackError.Visibility = Visibility.Visible;
        }
        else
        {
            ReaderFallbackError.Visibility = Visibility.Collapsed;
        }

        if (!_vm.Settings.UseFloatingCommandBar)
        {
            // Standard WebView2 is an HwndHost which ignores Visibility.Hidden and keeps rendering over the overlay.
            // Collapse the active WebView before showing the fallback so the overlay is actually visible.
            var activeWebView = ActiveStandardWebView;
            if (activeWebView != null)
                activeWebView.Visibility = Visibility.Collapsed;
            ReaderFallbackOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            // Floating command bar uses WebView2CompositionControl which supports Hidden without airspace issues.
            // Preserve existing behavior: just show the overlay on top.
            ReaderFallbackOverlay.Visibility = Visibility.Visible;
        }
    }

    private void HideReaderFallback()
    {
        ReaderFallbackOverlay.Visibility = Visibility.Collapsed;
        if (!_vm.Settings.UseFloatingCommandBar && _engine.ActiveTab != null)
        {
            var activeWebView = ActiveStandardWebView;
            if (activeWebView != null)
                activeWebView.Visibility = Visibility.Visible;
        }
    }

    private void OnLinkPreviewViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LinkPreviewViewModel.IsVisible))
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_linkPreviewVm.IsVisible)
                {
                    if (!_vm.Settings.UseFloatingCommandBar)
                    {
                        var activeWebView = ActiveStandardWebView;
                        if (activeWebView != null)
                            activeWebView.Visibility = Visibility.Collapsed;
                    }

                    LinkPreviewDim.Visibility = Visibility.Visible;
                    var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
                    {
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };
                    LinkPreviewDim.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                }
                else
                {
                    var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100))
                    {
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };
                    fadeOut.Completed += (_, _) =>
                    {
                        if (!_linkPreviewVm.IsVisible)
                        {
                            LinkPreviewDim.Visibility = Visibility.Collapsed;
                            LinkPreviewDim.BeginAnimation(UIElement.OpacityProperty, null);
                            if (!_vm.Settings.UseFloatingCommandBar && _engine.ActiveTab != null)
                            {
                                var activeWebView = ActiveStandardWebView;
                                if (activeWebView != null)
                                    activeWebView.Visibility = Visibility.Visible;
                            }
                        }
                    };
                    LinkPreviewDim.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                }
            });
        }
    }

    private void LinkPreviewDim_Click(object sender, MouseButtonEventArgs e)
    {
        _linkPreviewVm.Dismiss();
        e.Handled = true;
    }

    // ───────────────────── Reader Mode ─────────────────────

    private void OnReaderViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReaderViewModel.IsInReader) or nameof(ReaderViewModel.Current) or nameof(ReaderViewModel.Error) or nameof(ReaderViewModel.IsReaderAvailable))
        {
            Dispatcher.InvokeAsync(UpdateReaderOverlay);
        }
    }

    private async void ReaderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _readerVm.ToggleAsync();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Reader toggle failed: {ex}");
        }
    }

    private async void UpdateReaderOverlay()
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
                Trace.WriteLine($"Reader error surfaced: {error}");
                Title = $"Reader error: {error}";
                return;
            }

            var tabId = _engine.ActiveTab?.Id;
            if (tabId is null) return;
            var core = _engine.GetCoreForTab(tabId.Value);

            if (isInReader && current is not null)
            {
                if (core is null)
                {
                    Trace.WriteLine("Reader enter: CoreWebView2 is null for active tab");
                    return;
                }

                try
                {
                    _engine.IsProgrammaticReaderNavigation = true;
                    try { core.Settings.IsScriptEnabled = false; } catch (Exception ex) { Trace.WriteLine($"Reader disable script failed: {ex.Message}"); }
                    core.NavigateToString(current.Html);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Reader NavigateToString failed: {ex}");
                }
                finally
                {
                    Dispatcher.InvokeAsync(() => _engine.IsProgrammaticReaderNavigation = false, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            else if (!isInReader)
            {
                // Exit path. If we were in reader, the session still holds OriginalUrl. Navigate back and re-enable script.
                var session = _readerService.GetSession(tabId.Value);
                if (session is not null && !string.IsNullOrEmpty(session.OriginalUrl) && core is not null)
                {
                    var currentSource = core.Source ?? string.Empty;
                    var isReaderDocument = currentSource.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase) || currentSource == string.Empty;
                    if (isReaderDocument || currentSource != session.OriginalUrl)
                    {
                        // Only navigate back if we are still showing the reader document or have diverged
                        // Check if we previously were in reader by seeing if core's last navigation was reader HTML
                        // For now, if not in reader and session has OriginalUrl, ensure script is enabled and navigate if needed.
                        // The service's Exit already cleared IsInReader, so we re-enable script here.
                        try { core.Settings.IsScriptEnabled = true; } catch (Exception ex) { Trace.WriteLine($"Reader re-enable script failed: {ex.Message}"); }

                        // Only navigate if the tab's Url is still the reader's OriginalUrl and core Source is about:blank
                        // To avoid double navigation on tab switch, check if tab.Url is still OriginalUrl and core.Source is about:blank
                        if (currentSource.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase) || currentSource == "about:srcdoc")
                        {
                            try
                            {
                                _engine.IsProgrammaticReaderNavigation = true;
                                var tab = _engine.Tabs.FirstOrDefault(t => t.Id == tabId.Value);
                                if (tab is not null)
                                {
                                    tab.Url = session.OriginalUrl!;
                                    _engine.Navigate(tab, session.OriginalUrl!);
                                }
                            }
                            finally
                            {
                                Dispatcher.InvokeAsync(() => _engine.IsProgrammaticReaderNavigation = false, System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }
                        else
                        {
                            // Already navigated via implicit exit, just ensure script is on
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
            Trace.WriteLine($"UpdateReaderOverlay failed: {ex}");
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) =>
        _lifecycle.OnClosing(e);
}
