using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Shell;
using UmberBrowser.Models;

namespace UmberBrowser.Services.UI;

/// <summary>
/// Makes the WindowChrome resize border reachable while the WebView2 fills the window.
///
/// The resize border is an invisible hit region WPF owns on the top level window: the mouse
/// over it returns HTLEFT, HTBOTTOM and so on, and Windows then resizes. The WebView2
/// renders through an HwndHost child window that sits on top of that region. Once the page
/// fills the area below the toolbar, the edge and corner points land on Chromium's own
/// window, which answers HTCLIENT, so only the toolbar strip still resizes. WPF cannot hit
/// test through an HwndHost child, and the child belongs to the WebView2 process, so the
/// message cannot be redirected from here. The fix is to keep the page host clear of the
/// border, so the border stays WPF content. The inset is the window's own resize border
/// width in DIPs, and it is dropped whenever the border is not in use: maximized, or while
/// the fullscreen controller has set ResizeMode to NoResize.
/// </summary>
public sealed class WindowEdgeResizeInset
{
    private readonly Window _window;
    private readonly FrameworkElement _host;
    private readonly Thickness _baseMargin;
    private readonly Thickness _resizeBorder;
    private readonly BrowserSettings? _settings;
    private DependencyPropertyDescriptor? _resizeMode;
    private Thickness? _applied;

    private WindowEdgeResizeInset(Window window, FrameworkElement host, Thickness resizeBorder, BrowserSettings? settings)
    {
        _window = window;
        _host = host;
        _baseMargin = host.Margin;
        _resizeBorder = resizeBorder;
        _settings = settings;
    }

    /// <summary>
    /// Attaches to <paramref name="window"/> and insets its page host. Returns null when the
    /// window has no resize border or no host to inset, in which case nothing changes.
    /// <paramref name="settings"/> is the shared browser settings; when it is given this
    /// instance is the only writer of the host margin, so the compact frame gap and the
    /// resize border inset can never be added twice, and a live frame toggle moves the
    /// margin at the same moment the controller moves the clip.
    /// </summary>
    public static WindowEdgeResizeInset? Attach(Window window, BrowserSettings? settings = null)
    {
        try
        {
            var host = (window.FindName("WebViewHost") ?? window.FindName("ContentArea")) as FrameworkElement;
            if (host is null) return null;

            var border = WindowChrome.GetWindowChrome(window)?.ResizeBorderThickness ?? new Thickness(0);
            var inset = InsetFor(border);
            if (inset.Left <= 0 && inset.Right <= 0 && inset.Bottom <= 0) return null;

            var instance = new WindowEdgeResizeInset(window, host, border, settings);
            instance.Start();
            return instance;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"WindowEdgeResizeInset.Attach failed: {ex}");
            return null;
        }
    }

    /// <summary>
    /// The inset for a resize border. The top edge is left out: the toolbar already covers
    /// the top of the client area, so the top border and the top corners are reachable.
    /// </summary>
    public static Thickness InsetFor(Thickness resizeBorder) =>
        new(resizeBorder.Left, 0, resizeBorder.Right, resizeBorder.Bottom);

    /// <summary>
    /// True when the resize border is in use. A maximized window does not resize by its
    /// border, and fullscreen is NoResize, so neither should carry the inset.
    /// </summary>
    public static bool WantsInset(WindowState state, ResizeMode mode) =>
        mode != ResizeMode.NoResize && state == WindowState.Normal;

    /// <summary>
    /// The page host inset for the current state. With the rounded frame on, the host is
    /// held the frame's content margin off every edge: the side and bottom gap plus the
    /// outline stroke, and the stroke alone at the top. That also keeps most of the
    /// WindowChrome resize border reachable. With the frame off, the resize border inset
    /// is applied exactly as it was before the frame existed.
    /// </summary>
    public static Thickness CombinedInset(bool frameOn, WindowState state, ResizeMode mode, Thickness resizeBorder)
    {
        if (frameOn) return RoundedWebContentFrameGeometry.ContentMargin();
        return WantsInset(state, mode) ? InsetFor(resizeBorder) : new Thickness(0);
    }

    private void Start()
    {
        _window.StateChanged += OnStateChanged;
        _window.Closed += OnClosed;
        // ResizeMode is a dependency property with no change event, and the fullscreen
        // controller drives it, so it is watched directly.
        _resizeMode = DependencyPropertyDescriptor.FromProperty(Window.ResizeModeProperty, typeof(Window));
        _resizeMode?.AddValueChanged(_window, OnResizeModeChanged);
        // The frame preference can be toggled while the window is up, so the margin has
        // to follow it, not just the window state.
        if (_settings is not null) _settings.PropertyChanged += OnSettingChanged;
        Update();
    }

    private void OnStateChanged(object? sender, EventArgs e) => Update();

    private void OnResizeModeChanged(object? sender, EventArgs e) => Update();

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserSettings.RoundedWebContentFrameEnabled)) Update();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _window.StateChanged -= OnStateChanged;
        _window.Closed -= OnClosed;
        _resizeMode?.RemoveValueChanged(_window, OnResizeModeChanged);
        if (_settings is not null) _settings.PropertyChanged -= OnSettingChanged;
    }

    private void Update()
    {
        try
        {
            var frameOn = RoundedWebContentFrameGeometry.IsFrameOn(
                _settings?.RoundedWebContentFrameEnabled == true, _window.WindowState, _window.ResizeMode);
            var inset = CombinedInset(frameOn, _window.WindowState, _window.ResizeMode, _resizeBorder);
            if (_applied is { } last && last == inset) return;
            _host.Margin = new Thickness(_baseMargin.Left + inset.Left, _baseMargin.Top + inset.Top,
                _baseMargin.Right + inset.Right, _baseMargin.Bottom + inset.Bottom);
            _applied = inset;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"WindowEdgeResizeInset.Update failed: {ex}");
        }
    }
}
