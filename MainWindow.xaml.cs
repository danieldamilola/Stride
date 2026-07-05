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
        InitializeComponent();

        _vm = vm;
        _settingsStore = services.GetRequiredService<ISettingsStore>();
        _oneTabStore = services.GetRequiredService<IOneTabStore>();
        _sessionStore = services.GetRequiredService<ISessionStore>();
        _historyStore = services.GetRequiredService<IHistoryStore>();
        _downloadStore = services.GetRequiredService<IDownloadStore>();

        var deps = services.GetRequiredService<EngineDependencies>();
        _engine = new TabEngine(WebViewHost, deps);

        var extManager = services.GetRequiredService<ExtensionManager>();
        _router = new WebMessageRouter(_engine, _vm, _oneTabStore, _historyStore, _downloadStore, _settingsStore, extManager);
        _router.SettingChanged += OnSettingChanged;

        DataContext = _vm;

        WireEngineEvents();

        // Apply saved accent color
        if (_vm.Settings.AccentColor != "#D4A574")
            ApplyAccentColor(_vm.Settings.AccentColor);

        Loaded += OnWindowLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyRoundedCorners();

        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
            var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                NativeMethods.GetMonitorInfo(monitor, ref mi);

                var work = mi.rcWork;
                var mon = mi.rcMonitor;

                mmi.ptMaxPosition = new NativeMethods.POINT { X = work.Left - mon.Left, Y = work.Top - mon.Top };
                mmi.ptMaxSize = new NativeMethods.POINT { X = work.Right - work.Left, Y = work.Bottom - work.Top };

                // When the taskbar is auto-hidden, rcWork == rcMonitor (full screen).
                // The maximized window then covers the taskbar trigger zone (the 1px edge).
                // Detect this and leave a 1px gap on the taskbar edge so the cursor can trigger it.
                if (NativeMethods.IsTaskbarAutoHide())
                {
                    var edge = NativeMethods.GetTaskbarEdge();
                    switch (edge)
                    {
                        case 0: // Left
                            mmi.ptMaxPosition.X += 1;
                            mmi.ptMaxSize.X -= 1;
                            break;
                        case 1: // Top
                            mmi.ptMaxPosition.Y += 1;
                            mmi.ptMaxSize.Y -= 1;
                            break;
                        case 2: // Right
                            mmi.ptMaxSize.X -= 1;
                            break;
                        case 3: // Bottom (most common)
                            mmi.ptMaxSize.Y -= 1;
                            break;
                    }
                }
            }
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var preference = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch { /* Pre-Windows 11 — graceful fallback to square corners */ }
    }

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

            // Win32 foreground activation — WPF's Activate() alone is unreliable
            // on Windows 10/11 when the app is in the background. Windows enforces
            // strict foreground rules, so a background process calling Activate()
            // often just flashes the taskbar icon. Using the native APIs first
            // ensures the window actually comes to the front.
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                if (NativeMethods.IsIconic(hwnd))
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

                NativeMethods.SetForegroundWindow(hwnd);
            }

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Activate();
        });
    }

    private KeyboardShortcutMap BuildShortcutMap()
    {
        return new KeyboardShortcutMap(
            _engine,
            focusAddressBar: () => { FocusAddressBar(); return Task.CompletedTask; },
            saveAllTabs: () => { SaveAllTabs_Click(this, new RoutedEventArgs()); return Task.CompletedTask; },
            cycleTab: async reverse => await CycleTabAsync(reverse),
            toggleFullscreen: () => { ToggleFullscreen(); return Task.CompletedTask; },
            isFullscreen: () => _isFullscreen,
            updateZoomIndicator: () => { UpdateZoomIndicator(); return Task.CompletedTask; },
            openHistory: OpenHistoryTab,
            openDownloads: OpenDownloadsTab,
            switchToTabIndex: SwitchToTabByIndex,
            copyUrl: CopyUrlToClipboard,
            sendAllToOneTab: () => _engine.SendAllToOneTab(),
            saveOneTabGroup: entries =>
            {
                var group = new OneTabGroup
                {
                    Name = $"Saved {DateTime.Now:MMM d, h:mm tt}",
                    Tabs = entries.Select(t =>
                        new OneTabEntry(t.url, t.title, null, DateTime.UtcNow)).ToList()
                };
                _oneTabStore.AddGroup(group);
            },
            syncTabsBinding: SyncTabsBinding,
            openOneTab: OpenOneTabPage,
            openSettings: () => { Settings_Click(this, new RoutedEventArgs()); return Task.CompletedTask; });
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
        _engine.TabStateChanged += tab =>
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

    private void SyncTabsBinding()
    {
        TabList.ItemsSource = _engine.Tabs;
    }

    // ───────────────────── Tab Events ─────────────────────

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

        try
        {
            await _engine.ActivateAsync(tab);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"TabList_SelectionChanged activation failed: {ex}");
        }
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

    private void TabItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Grid grid || grid.Tag is not BrowserTab tab) return;

        var menu = new ContextMenu();

        var pinItem = new MenuItem
        {
            Header = tab.IsPinned ? "Unpin Tab" : "Pin Tab",
        };
        pinItem.Click += (_, _) => tab.IsPinned = !tab.IsPinned;
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

    private void GoBack_Click(object sender, RoutedEventArgs e) => _engine.GoBack();
    private void GoForward_Click(object sender, RoutedEventArgs e) => _engine.GoForward();
    private void Refresh_Click(object sender, RoutedEventArgs e) => _engine.Reload();

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideCommandBar();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var input = _vm.AddressText?.Trim();
        if (string.IsNullOrEmpty(input)) return;

        var url = _vm.ResolveInput(input);
        if (_engine.ActiveTab is { } tab)
        {
            tab.Url = url;
            _vm.AddressText = url;
            _engine.Navigate(tab, url);
        }

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

    // ───────────────────── Helpers ─────────────────────

    private bool _isCommandBarOpen;

    private void FocusAddressBar()
    {
        ShowCommandBar();
    }

    private async void ShowCommandBar()
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

        // Capture WebView content as screenshot before hiding (for dimmed preview)
        try
        {
            var core = _engine.GetCoreWebView2();
            if (core is not null)
            {
                using var ms = new System.IO.MemoryStream();
                await core.CapturePreviewAsync(
                    Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, ms);
                ms.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                PageSnapshot.Source = bitmap;
                PageSnapshot.Visibility = Visibility.Visible;
            }
        }
        catch { /* Fallback: no snapshot, just dark background */ }

        // Hide WebView — it's a native HWND that renders above WPF overlays (airspace issue)
        WebViewHost.Visibility = Visibility.Hidden;
        CommandBarOverlay.Visibility = Visibility.Visible;

        // 150ms ease-out appear animation: fade in + slide down
        CommandBarOverlay.Opacity = 0;
        CommandBarPanel.RenderTransform = new TranslateTransform(0, -10);

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var slideIn = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

        CommandBarOverlay.BeginAnimation(OpacityProperty, fadeIn);
        CommandBarPanel.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);

        // Focus after layout pass to ensure TextBox is in visual tree
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            AddressBar.Focus();
            Keyboard.Focus(AddressBar);
            AddressBar.SelectAll();
        });
    }

    private void HideCommandBar()
    {
        if (!_isCommandBarOpen) return;
        _isCommandBarOpen = false;

        // 150ms ease-out dismiss animation: fade out + slide up
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        fadeOut.Completed += (_, _) =>
        {
            CommandBarOverlay.Visibility = Visibility.Collapsed;
            CommandBarOverlay.BeginAnimation(OpacityProperty, null);

            // Restore WebView visibility and clear snapshot
            PageSnapshot.Visibility = Visibility.Collapsed;
            PageSnapshot.Source = null;
            WebViewHost.Visibility = Visibility.Visible;

            // Return focus to WebView
            if (_engine.ActiveTab is not null)
                WebViewHost.Focus();
        };

        var slideOut = new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

        CommandBarOverlay.BeginAnimation(OpacityProperty, fadeOut);
        if (CommandBarPanel.RenderTransform is TranslateTransform tt)
            tt.BeginAnimation(TranslateTransform.YProperty, slideOut);
    }

    private void UrlLabel_Click(object sender, MouseButtonEventArgs e)
    {
        ShowCommandBar();
        e.Handled = true;
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

    private void UpdateUrlLabel(BrowserTab tab)
    {
        if (string.IsNullOrEmpty(tab.Url) || InternalUrls.IsInternal(tab.Url))
        {
            UrlLabel.Text = tab.Title ?? "New Tab";
            return;
        }

        try
        {
            var uri = new Uri(tab.Url);
            UrlLabel.Text = uri.Host;
        }
        catch
        {
            UrlLabel.Text = tab.Title ?? tab.Url;
        }
    }

    // ───────────────────── Adaptive Site Tinting ─────────────────────
    // Extracts dominant color from favicon and applies subtle 8% tint
    // on the toolbar background. 400ms smooth transition.

    private Color _currentTintColor = Colors.Transparent;

    private void UpdateToolbarTint(BrowserTab tab)
    {
        var tintColor = ExtractDominantColor(tab.Favicon as BitmapSource);
        if (tintColor == _currentTintColor) return;
        _currentTintColor = tintColor;

        var baseColor = (Color)FindResource("SidebarColor");
        Color targetColor;

        if (tintColor == Colors.Transparent)
        {
            targetColor = baseColor;
        }
        else
        {
            // Blend: 92% base + 8% site color
            targetColor = Color.FromRgb(
                (byte)(baseColor.R * 0.92 + tintColor.R * 0.08),
                (byte)(baseColor.G * 0.92 + tintColor.G * 0.08),
                (byte)(baseColor.B * 0.92 + tintColor.B * 0.08));
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
    }

    private static Color ExtractDominantColor(BitmapSource? bitmap)
    {
        if (bitmap is null) return Colors.Transparent;

        try
        {
            // Scale down to 1x1 pixel for average color
            var scaled = new TransformedBitmap(bitmap,
                new ScaleTransform(
                    1.0 / bitmap.PixelWidth,
                    1.0 / bitmap.PixelHeight));

            var pixel = new byte[4];
            scaled.CopyPixels(pixel, 4, 0);

            var b = pixel[0];
            var g = pixel[1];
            var r = pixel[2];

            // Skip very dark or very light colors (not useful as tint)
            var brightness = (r * 0.299 + g * 0.587 + b * 0.114);
            if (brightness < 20 || brightness > 240)
                return Colors.Transparent;

            // Skip near-grey colors (low saturation)
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            if (max - min < 20)
                return Colors.Transparent;

            return Color.FromRgb(r, g, b);
        }
        catch
        {
            return Colors.Transparent;
        }
    }

    private void UpdateSecurityIcon(string url)
    {
        if (string.IsNullOrEmpty(url) || InternalUrls.IsInternal(url))
        {
            SecurityIcon.Visibility = Visibility.Collapsed;
            return;
        }

        SecurityIcon.Visibility = Visibility.Visible;
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            SecurityIcon.Data = (StreamGeometry)FindResource("IconLock");
            SecurityIcon.Stroke = new SolidColorBrush(Color.FromRgb(0x6B, 0x8F, 0x71));
        }
        else
        {
            SecurityIcon.Data = (StreamGeometry)FindResource("IconGlobe");
            SecurityIcon.Stroke = (Brush)FindResource("TextSecondary");
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
            _engine.ApplyDarkModeToAll(_vm.Settings.ForceDarkMode);

        if (key == "accentColor")
            ApplyAccentColor(_vm.Settings.AccentColor);

        // Live-rebuild shortcut bindings when user rebinds a key
        if (key is "shortcut" or "shortcutReset")
            _shortcuts?.RebuildBindings(_vm.Settings.CustomShortcuts);

        if (_engine.ActiveTab?.Url == InternalUrls.Settings)
            _engine.NavigateToSettings(_engine.ActiveTab, _vm.Settings);

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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // No sidebar toggle needed — Stride has no sidebar
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

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        try
        {
            if (_shortcuts is not null)
            {
                var modifiers = Keyboard.Modifiers;
                var handled = await _shortcuts.TryExecuteAsync(modifiers, e.Key);
                if (handled)
                {
                    e.Handled = true;
                    return;
                }

                if (modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D9)
                {
                    var tabIndex = e.Key - Key.D1;
                    if (e.Key == Key.D9)
                        tabIndex = _engine.Tabs.Count - 1;
                    await SwitchToTabByIndex(tabIndex);
                    e.Handled = true;
                    return;
                }

                // Alt+1–9: also switch tabs (user preference)
                if (modifiers == ModifierKeys.Alt && e.Key >= Key.D1 && e.Key <= Key.D9)
                {
                    var tabIndex = e.Key - Key.D1;
                    if (e.Key == Key.D9)
                        tabIndex = _engine.Tabs.Count - 1;
                    await SwitchToTabByIndex(tabIndex);
                    e.Handled = true;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"OnKeyDown failed: {ex}");
        }

        base.OnKeyDown(e);
    }

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

            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var source = HwndSource.FromHwnd(hwnd);
                source?.RemoveHook(WndProc);
            }
            catch { }

            Application.Current.Shutdown();
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
