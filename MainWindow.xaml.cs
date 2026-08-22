using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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

namespace StrideBrowser;

/// <summary>
/// Thin view layer — delegates all logic to BrowserViewModel and TabEngine.
/// Horizontal toolbar + horizontal tab strip layout.
/// </summary>
public partial class MainWindow : Window
{
    private readonly BrowserViewModel _vm;
    private readonly TabEngine _engine;
    private readonly IOneTabStore _oneTabStore;
    private readonly ISettingsStore _settingsStore;
    private readonly ISessionStore _sessionStore;
    private readonly IHistoryStore _historyStore;
    private readonly IDownloadStore _downloadStore;
    private readonly WebMessageRouter _router;
    private KeyboardShortcutMap? _shortcuts;

    /// <summary>Prevents re-entrant tab selection changes when we programmatically update the ListBox selection.</summary>
    private bool _isUpdatingSelection;
    private bool _isFullscreen;
    private WindowState _preFullscreenState;

    private const double LoadingIndicatorWidth = 100;
    private const double LoadingOvershootPx = 20;
    private const double FallbackContainerWidth = 900;
    private const double SweepDurationSeconds = 1.2;

    public MainWindow(IServiceProvider services, BrowserViewModel vm)
    {
        // We no longer need Deactivated/LocationChanged/SizeChanged hooks 
        // since the Command Bar is integrated back into the visual tree.

        InitializeComponent();

        _vm = vm;
        _settingsStore = services.GetRequiredService<ISettingsStore>();
        _oneTabStore = services.GetRequiredService<IOneTabStore>();
        _sessionStore = services.GetRequiredService<ISessionStore>();
        _historyStore = services.GetRequiredService<IHistoryStore>();
        _downloadStore = services.GetRequiredService<IDownloadStore>();

        _engine = services.GetRequiredService<TabEngine>();
        _engine.AttachHost(WebViewHost);

        _router = services.GetRequiredService<WebMessageRouter>();
        _router.SettingChanged += OnSettingChanged;
        var themeManager = services.GetRequiredService<ThemeManager>();
        themeManager.ThemeChanged += () =>
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
        };

        DataContext = _vm;

        // Attach event handlers
        WireEngineEvents();

        // Apply saved accent color
        if (_vm.Settings.AccentColor != "#D4A574")
            ApplyAccentColor(_vm.Settings.AccentColor);

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
                var scrollViewer = GetScrollViewer(TabList);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + (delta * 0.5));
                }
            }
        };
        _chromeManager.Initialize();
    }

    private const int WM_MOUSEHWHEEL = 0x020E;



    // ───────────────────── Initialization ─────────────────────

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _engine.InitializeAsync();

            _shortcuts = BuildShortcutMap();
            _shortcuts.RebuildBindings(_vm.Settings.CustomShortcuts);

            await RestoreSessionOrCreateTab();
            SyncTabsBinding();
            // Don't auto-open command bar on startup

            await HandleCommandLineUrls();

            SingleInstanceManager.InstanceMessageReceived += OnInstanceMessageReceived;

            if (!DefaultBrowserRegistrar.IsRegistered())
                DefaultBrowserRegistrar.Register();


        }
        catch (Exception ex)
        {
            Trace.WriteLine($"OnWindowLoaded failed: {ex}");
        }
    }

    private void OnInstanceMessageReceived(string[] args)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            foreach (var arg in args)
            {
                if (arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var tab = _engine.CreateTab(arg);
                    _engine.SwitchTo(tab);
                    await _engine.ActivateAsync(tab);
                    break;
                }
            }

            _chromeManager?.BringToFront();
        });
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
                ToggleFullscreen = () => { ToggleFullscreen(); return Task.CompletedTask; },
                IsFullscreen = () => _isFullscreen,
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
                LaunchTCLens = LaunchTCLensAsync,
                ToggleReader = () => Task.CompletedTask
            });
    }

    private async Task LaunchTCLensAsync()
    {
        var wv = _engine.GetCoreWebView2();
        if (wv != null)
        {
            try
            {
                var exts = await wv.Profile.GetBrowserExtensionsAsync();
                foreach (var ex in exts) {
                }
                var tcLens = exts.FirstOrDefault(e => e.Name.Contains("T&C Lens", StringComparison.OrdinalIgnoreCase) || e.Name.Contains("T-C", StringComparison.OrdinalIgnoreCase));
                if (tcLens != null)
                {
                    var url = $"extension://{tcLens.Id}/options/options.html";
                    
                    // Look for existing tab
                    var existing = _engine.Tabs.FirstOrDefault(t => t.Url != null && t.Url.StartsWith($"extension://{tcLens.Id}/options"));
                    if (existing != null)
                    {
                        _engine.SwitchTo(existing);
                    }
                    else
                    {
                        var tab = _engine.CreateTab(url);
                        _engine.SwitchTo(tab);
                        await _engine.ActivateAsync(tab);
                    }
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Failed to launch T&C Lens: {ex.Message}");
            }
        }
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
                    var tab = _engine.CreateTab(entry.Url);
                    tab.Title = entry.Title;
                    tab.IsPinned = entry.IsPinned;
                    _ = _engine.LoadFaviconAsync(tab);
                }
                _engine.SwitchTo(_engine.Tabs[0]);
                await _engine.ActivateAsync(_engine.Tabs[0]);
                restored = true;
            }
        }

        if (!restored)
        {
            var tab = _engine.CreateTab();
            _engine.SwitchTo(tab);
            await _engine.ActivateAsync(tab);
        }

        string flagFile = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "post_update.flag");
        bool isPostUpdate = false;
        
        if (System.IO.File.Exists(flagFile))
        {
            try { System.IO.File.Delete(flagFile); } catch {}
            isPostUpdate = true;
        }
        
        var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.2.1";
        if (_vm.Settings.LastSeenReleaseNotesVersion != currentVersion)
        {
            isPostUpdate = true;
            _vm.Settings.LastSeenReleaseNotesVersion = currentVersion;
            _settingsStore.Save(_vm.Settings);
        }

        if (isPostUpdate)
        {
            var tab = _engine.CreateTab(InternalUrls.ReleaseNotes);
            _engine.SwitchTo(tab);
            await _engine.ActivateAsync(tab);
        }
    }

    private async Task HandleCommandLineUrls()
    {
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args.Skip(1))
        {
            if (arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var tab = _engine.CreateTab(arg);
                _engine.SwitchTo(tab);
                await _engine.ActivateAsync(tab);
                break;
            }
        }
    }

    private void WireEngineEvents()
    {
        _engine.FullScreenChanged += isFullScreen =>
        {
            if (isFullScreen)
            {
                Toolbar.Visibility = Visibility.Collapsed;
                WindowState = WindowState.Normal;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else
            {
                Toolbar.Visibility = Visibility.Visible;
                ResizeMode = ResizeMode.CanResize;
            }
        };

        _engine.TabStateChanged += async tab =>
        {
            if (tab.IsActive)
            {
                _vm.SyncAddressBar(tab);
                UpdateSecurityIcon(tab.Url);
                UpdateUrlLabel(tab);
                UpdateToolbarTint(tab);
                _isUpdatingSelection = true;
                try
                {
                    if (TabList.SelectedItem != tab)
                        TabList.SelectedItem = tab;
                }
                finally { _isUpdatingSelection = false; }

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
                if (isLoading)
                {
                    LoadingBar.Visibility = Visibility.Visible;
                    StartLoadingAnimation();
                    StartSecuritySpinner();
                }
                else
                {
                    StopLoadingAnimation();
                    StopSecuritySpinner();
                    LoadingBar.Visibility = Visibility.Collapsed;
                    UpdateSecurityIcon(tab.Url);
                }
            }
        };

        _engine.WebMessageReceived += HandleWebMessage;
        _engine.TabCreated += tab =>
        {
        };
    }

    private void LinkPreviewDim_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        LinkPreviewDim.Visibility = Visibility.Collapsed;
    }

    private void SyncTabsBinding()
    {
        TabList.ItemsSource = _engine.Tabs;
    }

    // ───────────────────── Tab Events ─────────────────────

    private void TabList_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var scrollViewer = GetScrollViewer(TabList);
        if (scrollViewer != null)
        {
            // Use e.Delta to support smooth precision trackpad scrolling
            // e.Delta is typically 120 for a mouse wheel notch, and smaller values for trackpads.
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - (e.Delta * 0.5));
            e.Handled = true;
        }
    }

    private ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer sv) return sv;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
    private async void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection) return;

        var listBox = (ListBox)sender;
        if (listBox.SelectedItem is not BrowserTab tab || tab == _engine.ActiveTab) return;

        _isUpdatingSelection = true;
        try
        {
            _engine.SwitchTo(tab);
        }
        finally { _isUpdatingSelection = false; }

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
        if (sender is Button b && b.Tag is BrowserTab tab)
        {
            if (tab.IsPinned) return;
            _engine.CloseTab(tab);
        }
    }

    private async void Downloads_Click(object sender, RoutedEventArgs e)
    {
        await OpenDownloadsTab();
    }

    private void TabItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Grid grid || grid.Tag is not BrowserTab tab) return;

        var menu = new ContextMenu();

        var pinItem = new MenuItem
        {
            Header = tab.IsPinned ? "Unpin Tab" : "Pin Tab",
        };
        pinItem.Click += (_, _) => 
        {
            tab.IsPinned = !tab.IsPinned;
            if (tab.IsPinned)
            {
                var index = _engine.Tabs.IndexOf(tab);
                if (index > 0)
                {
                    _engine.Tabs.Move(index, 0);
                }
            }
        };
        menu.Items.Add(pinItem);

        var dupeItem = new MenuItem
        {
            Header = "Duplicate Tab",
        };
        dupeItem.Click += async (_, _) =>
        {
            try
            {
                var newTab = _engine.CreateTab(tab.Url);
                _engine.SwitchTo(newTab);
                await _engine.ActivateAsync(newTab);
            }
            catch (Exception ex) { Trace.WriteLine($"Duplicate tab error: {ex.Message}"); }
        };
        menu.Items.Add(dupeItem);

        if (!tab.IsPinned)
        {
            menu.Items.Add(new Separator());
            var closeItem = new MenuItem
            {
                Header = "Close Tab",
            };
            closeItem.Click += (_, _) => _engine.CloseTab(tab);
            menu.Items.Add(closeItem);
        }

        grid.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ───────────────────── Navigation ─────────────────────



    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.ShowSuggestions = false;
            HideCommandBar();
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

        // If a suggestion is selected, use it; otherwise use typed text
        string? input;
        if (_vm.ShowSuggestions && _vm.SelectedSuggestionIndex >= 0
            && _vm.SelectedSuggestionIndex < _vm.Suggestions.Count)
        {
            input = _vm.Suggestions[_vm.SelectedSuggestionIndex];
        }
        else
        {
            input = (sender as System.Windows.Controls.TextBox)?.Text?.Trim() ?? _vm.AddressText?.Trim();
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
        HideCommandBar();
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

    public static string PendingTCLensText = "";
    public static string PendingTCLensUrl = "";
    public static string PendingTCLensTitle = "";

    private async Task HandleNativeTCLensShortcutAsync()
    {
        try
        {
            var activeTab = _engine.ActiveTab;
            if (activeTab == null) return;
            var wv = _engine.GetCoreWebView2();
            if (wv == null) return;

            var rawJson = await wv.ExecuteScriptAsync("document.body.innerText");
            if (!string.IsNullOrEmpty(rawJson) && rawJson != "null")
            {
                PendingTCLensText = System.Text.Json.JsonSerializer.Deserialize<string>(rawJson) ?? "";
            }
            PendingTCLensUrl = activeTab.Url ?? "";
            PendingTCLensTitle = activeTab.Title ?? "";

            var newTab = _engine.CreateTab("http://local.assets/TCLens/options/options.html");
            _engine.SwitchTo(newTab);
            await _engine.ActivateAsync(newTab);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"HandleNativeTCLensShortcutAsync failed: {ex}");
        }
    }

    // ───────────────────── Interop Helpers ─────────────────────

    private bool _isCommandBarOpen;

    private void FocusAddressBar()
    {
        if (_vm.Settings.UseFloatingCommandBar)
        {
            ShowCommandBar();
        }
        else
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
            StandardAddressBar.SelectAll();
        }
    }

    private void ShowCommandBar()
    {
        if (_isCommandBarOpen) return;
        _isCommandBarOpen = true;

        // Pre-fill with current URL
        if (_engine.ActiveTab is { } activeTab && !string.IsNullOrEmpty(activeTab.Url)
            && !InternalUrls.IsInternal(activeTab.Url))
        {
            _vm.AddressText = activeTab.Url;
        }
        else
        {
            _vm.AddressText = "";
        }

        CommandBarGrid.Visibility = Visibility.Visible;

        // 80ms punchy ease-out animation
        CommandBarGrid.Opacity = 0;
        CommandBarPanel.RenderTransform = new TranslateTransform(0, -10);

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var slideIn = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(80))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        CommandBarGrid.BeginAnimation(OpacityProperty, fadeIn);
        CommandBarPanel.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);

        // Focus after layout pass to ensure TextBox is in visual tree and Popup is active
        Dispatcher.BeginInvoke(new Action(() =>
        {
            AddressBar.Focus();
            Keyboard.Focus(AddressBar);
            AddressBar.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void HideCommandBar()
    {
        if (!_isCommandBarOpen) return;
        _isCommandBarOpen = false;

        _vm.ShowSuggestions = false;
        _vm.Suggestions.Clear();

        // 80ms ease-out dismiss animation: fade out + slide up
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(80))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fadeOut.Completed += (_, _) =>
        {
            CommandBarGrid.Visibility = Visibility.Collapsed;
            CommandBarGrid.BeginAnimation(OpacityProperty, null);

            // Return focus to WebView
            if (_engine.ActiveTab is not null)
                WebViewHost.Focus();
        };

        var slideOut = new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(80))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        CommandBarGrid.BeginAnimation(OpacityProperty, fadeOut);
        if (CommandBarPanel.RenderTransform is TranslateTransform tt)
            tt.BeginAnimation(TranslateTransform.YProperty, slideOut);
    }

    private void UrlLabel_Click(object sender, MouseButtonEventArgs e)
    {
        FocusAddressBar();
        e.Handled = true;
    }

    private void StandardAddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.ShowSuggestions = false;
            UpdateUrlLabel(_engine.ActiveTab);
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

    private void StandardAddressBar_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        StandardAddressBar.Dispatcher.InvokeAsync(() => StandardAddressBar.SelectAll());
    }

    private void StandardAddressBar_LostFocus(object sender, RoutedEventArgs e)
    {
        // Do not auto-close to allow MouseUp to fire on the list
    }

    private void StandardAddressBar_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsKeyboardFocusWithin)
            _ = _vm.UpdateSuggestionsAsync(tb.Text);
    }

    private void StandardSuggestionsList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedSuggestionIndex >= 0 && _vm.SelectedSuggestionIndex < _vm.Suggestions.Count)
        {
            var url = _vm.ResolveInput(_vm.Suggestions[_vm.SelectedSuggestionIndex]);
            if (_engine.ActiveTab is { } tab)
            {
                tab.Url = url;
                _vm.AddressText = url;
                _engine.Navigate(tab, url);
            }
            _vm.ShowSuggestions = false;
            WebViewHost.Focus();
        }
    }

    private void CommandBarBackdrop_Click(object sender, MouseButtonEventArgs e)
    {
        HideCommandBar();
    }

    private void AddressBar_LostFocus(object sender, RoutedEventArgs e)
    {
        // Don't auto-close — only close on Escape, Enter, or backdrop click
        // This prevents the race condition where it closes immediately
    }

    private void AddressBar_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsKeyboardFocusWithin)
            _ = _vm.UpdateSuggestionsAsync(tb.Text);
    }

    private void SuggestionsList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedSuggestionIndex >= 0 && _vm.SelectedSuggestionIndex < _vm.Suggestions.Count)
        {
            var input = _vm.Suggestions[_vm.SelectedSuggestionIndex];
            var url = _vm.ResolveInput(input);
            if (_engine.ActiveTab is { } tab)
            {
                tab.Url = url;
                _vm.AddressText = url;
                _engine.Navigate(tab, url);
            }
            _vm.ShowSuggestions = false;
            HideCommandBar();
        }
    }

    private void UpdateUrlLabel(BrowserTab tab)
    {
        if (!StandardAddressBar.IsKeyboardFocusWithin)
        {
            StandardAddressBar.Text = tab.Url;
        }

        if (string.IsNullOrEmpty(tab.Url) || InternalUrls.IsInternal(tab.Url))
        {
            UrlLabel.Text = tab.Title ?? "New Tab";
            return;
        }

        try
        {
            var uri = new Uri(tab.Url);
            var host = uri.Host;
            if (host.StartsWith("www.")) host = host[4..];
            UrlLabel.Text = host;
        }
        catch
        {
            UrlLabel.Text = tab.Title ?? tab.Url;
        }
    }

    // ───────────────────── Adaptive Site Tinting ─────────────────────
    // Uses the page's actual theme color (extracted by ContentScriptInjector JS)
    // and applies it directly to the entire toolbar chrome for a seamless look.
    // 400ms smooth QuadraticEase transition.

    private string _currentThemeColorHex = "";

    private void UpdateToolbarTint(BrowserTab tab)
    {
        var hex = tab.ThemeColor ?? "";
        if (hex == _currentThemeColorHex) return;
        _currentThemeColorHex = hex;

        var baseColor = (Color)FindResource("SidebarColor");
        Color targetColor;

        if (!string.IsNullOrEmpty(hex))
        {
            try
            {
                targetColor = (Color)ColorConverter.ConvertFromString(hex);
                // Reject pure/near white. This prevents the toolbar from turning blindingly white 
                // when Dark Reader is active but the site's meta theme-color tag still says #FFFFFF.
                if (targetColor.R > 245 && targetColor.G > 245 && targetColor.B > 245)
                {
                    targetColor = Color.FromRgb(0x11, 0x11, 0x11);
                }
                // Reject strong green colors (e.g. jiji.ng) because they clash with the dark theme
                else if (targetColor.G > 120 && targetColor.G > targetColor.R + 40 && targetColor.G > targetColor.B + 40)
                {
                    targetColor = Color.FromRgb(0x11, 0x11, 0x11);
                }
            }
            catch
            {
                targetColor = baseColor;
            }
        }
        else
        {
            targetColor = baseColor;
        }

        var anim = new ColorAnimation
        {
            To = targetColor,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        var brush = Toolbar.Background as SolidColorBrush;
        if (brush is null || brush.IsFrozen)
        {
            brush = new SolidColorBrush(baseColor);
            Toolbar.Background = brush;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);

        // Dynamic Contrast: If the toolbar adapts to a light color, switch icons/text to dark
        var luminance = (0.299 * targetColor.R + 0.587 * targetColor.G + 0.114 * targetColor.B) / 255.0;
        if (luminance > 0.5)
        {
            Toolbar.Resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(30, 30, 34));
            Toolbar.Resources["TextSecondary"] = new SolidColorBrush(Color.FromRgb(70, 70, 74));
            Toolbar.Resources["TextMuted"] = new SolidColorBrush(Color.FromRgb(100, 100, 104));
        }
        else
        {
            // Dark background -> remove local overrides so they fall back to global App.xaml theme
            Toolbar.Resources.Remove("TextPrimary");
            Toolbar.Resources.Remove("TextSecondary");
            Toolbar.Resources.Remove("TextMuted");
        }
    }


    private void UpdateSecurityIcon(string url)
    {
        if (string.IsNullOrEmpty(url) || InternalUrls.IsInternal(url))
        {
            SecurityIcon.Visibility = Visibility.Collapsed;
            StandardSecurityIcon.Visibility = Visibility.Collapsed;
            return;
        }

        if (_vm.Settings.UseFloatingCommandBar)
        {
            SecurityIcon.Visibility = Visibility.Visible;
            StandardSecurityIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecurityIcon.Visibility = Visibility.Collapsed;
            StandardSecurityIcon.Visibility = Visibility.Visible;
        }

        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            SecurityIcon.Data = (StreamGeometry)FindResource("IconLock");
            SecurityIcon.Stroke = new SolidColorBrush(Color.FromRgb(0x6B, 0x8F, 0x71));
            StandardSecurityIcon.Data = (StreamGeometry)FindResource("IconLock");
            StandardSecurityIcon.Stroke = new SolidColorBrush(Color.FromRgb(0x6B, 0x8F, 0x71));
        }
        else
        {
            SecurityIcon.Data = (StreamGeometry)FindResource("IconGlobe");
            SecurityIcon.Stroke = (Brush)FindResource("TextSecondary");
            StandardSecurityIcon.Data = (StreamGeometry)FindResource("IconGlobe");
            StandardSecurityIcon.Stroke = (Brush)FindResource("TextSecondary");
        }
    }

    private void StartSecuritySpinner()
    {
        if (SecurityIcon.Visibility != Visibility.Visible) return;

        StopSecuritySpinner();
        SecurityIcon.Data = (StreamGeometry)FindResource("IconRefresh");
        SecurityIcon.Stroke = (Brush)FindResource("Accent");
        var rot = new RotateTransform();
        SecurityIcon.RenderTransformOrigin = new Point(0.5, 0.5);
        SecurityIcon.RenderTransform = rot;
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        rot.BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    private void StopSecuritySpinner()
    {
        if (SecurityIcon.RenderTransform is RotateTransform rot)
            rot.BeginAnimation(RotateTransform.AngleProperty, null);
        SecurityIcon.RenderTransform = null;
    }

    // ───────────────────── OneTab ─────────────────────

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

    private void OnSettingChanged(string key, string _)
    {
        if (key == "darkMode")
        {
            // Native dark mode (WebContentsForceDark) takes effect at engine init — a restart is required.
            // Show a banner on the settings page to inform the user.
            var msg = _vm.Settings.ForceDarkMode ? "Dark mode enabled" : "Dark mode disabled";
            var js = $@"(function() {{
                var existing = document.getElementById('stride-restart-banner');
                if (existing) existing.remove();
                var banner = document.createElement('div');
                banner.id = 'stride-restart-banner';
                banner.style.cssText = 'position:fixed;bottom:24px;left:50%;transform:translateX(-50%);background:#1a1a2e;color:#e0e0e0;border:1px solid rgba(255,255,255,0.12);border-radius:12px;padding:14px 22px;font-size:14px;font-family:inherit;display:flex;align-items:center;gap:14px;z-index:99999;box-shadow:0 8px 32px rgba(0,0,0,0.5);';
                banner.innerHTML = '<span style=""font-size:18px"">🌙</span><span><strong>{msg}</strong> — Restart Stride to apply</span><button onclick=""this.parentElement.remove()"" style=""background:rgba(255,255,255,0.1);border:none;color:#e0e0e0;border-radius:8px;padding:6px 12px;cursor:pointer;font-size:13px;margin-left:8px;"">Dismiss</button>';
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
            var tm = ((App)Application.Current).Services.GetRequiredService<ThemeManager>();
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
        }

        if (key == "accentColor")
            ApplyAccentColor(_vm.Settings.AccentColor);

        // Live-rebuild shortcut bindings when user rebinds a key
        if (key is "shortcut" or "shortcutReset")
            _shortcuts?.RebuildBindings(_vm.Settings.CustomShortcuts);

        // Removed: Reloading the settings page here causes it to jump back to the General tab
        // whenever the user changes any setting. The frontend UI updates itself.

        // Live-refresh new tab pages when shortcuts are added/removed
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

            // Update accent color & brush
            Application.Current.Resources["AccentColor"] = color;
            Application.Current.Resources["Accent"] = brush;

            // Update accent wash (10% opacity version)
            var wash = new SolidColorBrush(Color.FromArgb(0x1A, color.R, color.G, color.B));
            wash.Freeze();
            Application.Current.Resources["AccentWash"] = wash;

            // Update loading bar gradient to match accent
            Application.Current.Resources["LoadingBarGlowColor"] = color;
            Application.Current.Resources["LoadingBarFadeColor"] = Color.FromArgb(0x00, color.R, color.G, color.B);
        }
        catch { /* ignore invalid color strings */ }
    }

    // ──── Keyboard shortcuts ────

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (_shortcuts is not null)
            {
                var modifiers = Keyboard.Modifiers;
                var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
                
                if (modifiers == ModifierKeys.Alt && actualKey == Key.T)
                {
                    await HandleNativeTCLensShortcutAsync();
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
            await _engine.ActivateAsync(tab);
            _engine.NavigateToSettings(tab, _vm.Settings);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Settings_Click failed: {ex}");
        }
    }

    private Task CheckForUpdatesInBackgroundAsync()
    {
        return Task.CompletedTask;
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

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            _preFullscreenState = WindowState;
            Toolbar.Visibility = Visibility.Collapsed;
            WindowState = WindowState.Maximized;
        }
        else
        {
            Toolbar.Visibility = Visibility.Visible;
            WindowState = _preFullscreenState;
        }
    }

    // ───────────────────── Tab Drag & Drop ─────────────────────

    private Point _dragStartPoint;
    private bool _isDragging;

    private void TabList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    private void TabList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;
        
        var pos = e.GetPosition(null);
        var diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var listBox = sender as ListBox;
            if (listBox?.SelectedItem is BrowserTab tab)
            {
                _isDragging = true;
                DragDrop.DoDragDrop(listBox, tab, DragDropEffects.Move);
                _isDragging = false;
            }
        }
    }

    private void TabList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(BrowserTab)) is not BrowserTab droppedTab) return;
        
        var listBox = sender as ListBox;
        if (listBox is null) return;
        
        var targetElement = e.OriginalSource as FrameworkElement;
        while (targetElement != null && targetElement != listBox)
        {
            if (targetElement.DataContext is BrowserTab targetTab && targetTab != droppedTab)
            {
                var oldIndex = _engine.Tabs.IndexOf(droppedTab);
                var newIndex = _engine.Tabs.IndexOf(targetTab);
                if (oldIndex >= 0 && newIndex >= 0)
                {
                    _isUpdatingSelection = true;
                    try { _engine.Tabs.Move(oldIndex, newIndex); }
                    finally { _isUpdatingSelection = false; }
                }
                break;
            }
            targetElement = VisualTreeHelper.GetParent(targetElement) as FrameworkElement;
        }
    }

    // ───────────────────── Zoom Indicator ─────────────────────

    private void UpdateZoomIndicator()
    {
        // Stride minimal UI — no visible zoom indicator
        // Zoom still works via Ctrl+/- shortcuts
    }

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
            await _engine.ActivateAsync(tab);
            var entries = _historyStore.Load();
            _engine.NavigateToHistory(tab, entries);
        }
        catch (Exception ex) { Trace.WriteLine($"History open failed: {ex}"); }
    }

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

    private bool _isShuttingDown;

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isShuttingDown)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        _isShuttingDown = true;

        try
        {
            if (_vm.Settings.RestoreSessionOnStartup)
            {
                var tabs = _engine.Tabs
                    .Where(t => !InternalUrls.IsInternal(t.Url))
                    .Select(t => (t.Url, t.Title, t.IsPinned));
                _sessionStore.Save(tabs);
            }

            if (_vm.Settings.ClearDataOnExit)
            {
                try
                {
                    var profile = _engine.GetCoreWebView2()?.Profile;
                    if (profile is not null)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await profile.ClearBrowsingDataAsync().WaitAsync(cts.Token);
                    }
                }
                catch { /* Timeout or disposal — best-effort cleanup */ }
            }

            _settingsStore.Save(_vm.Settings);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"OnClosing save failed: {ex}");
        }
        finally
        {
            try { _engine.Shutdown(); }
            catch (Exception ex) { Trace.WriteLine($"Engine shutdown failed: {ex}"); }

            Application.Current.Dispatcher.InvokeAsync(() => Close(), System.Windows.Threading.DispatcherPriority.Normal);
        }
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

