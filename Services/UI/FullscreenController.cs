using System.Windows;

namespace StrideBrowser.Services.UI;

/// <summary>Immutable snapshot of the browser fullscreen state.</summary>
public readonly record struct FullscreenState(bool IsActive, WindowState SavedWindowState)
{
    public static FullscreenState Initial => new(false, WindowState.Normal);
}

/// <summary>
/// Pure fullscreen transitions with no WPF window dependency, so the state
/// machine is unit-testable. Enter records the window state to restore on exit;
/// repeated transitions are no-ops.
/// </summary>
public static class FullscreenTransitions
{
    public static FullscreenState Enter(FullscreenState current, WindowState windowState) =>
        current.IsActive ? current : current with { IsActive = true, SavedWindowState = windowState };

    public static FullscreenState Exit(FullscreenState current) =>
        current.IsActive ? current with { IsActive = false } : current;

    public static FullscreenState Toggle(FullscreenState current, WindowState windowState) =>
        current.IsActive ? Exit(current) : Enter(current, windowState);
}

/// <summary>
/// Single source of truth for browser window fullscreen. Shared by the F11
/// keyboard toggle and WebView2 video fullscreen events so both paths stay in
/// sync. Applies the visual effects (toolbar visibility, resize mode) and
/// sizes the window natively to the exact monitor rectangle so fullscreen
/// covers the taskbar with no chrome edges.
/// </summary>
public sealed class FullscreenController
{
    private readonly Window _window;
    private readonly FrameworkElement _toolbar;
    private FullscreenState _state = FullscreenState.Initial;
    private Rect _preFullscreenBounds;
    private bool _nativeFullscreenActive;

    /// <summary>Set by the window once WindowChromeManager exists (OnSourceInitialized).</summary>
    public WindowChromeManager? ChromeManager { get; set; }

    public bool IsFullscreen => _state.IsActive;

    public FullscreenController(Window window, FrameworkElement toolbar)
    {
        _window = window;
        _toolbar = toolbar;
    }

    public void Toggle() =>
        _state = Apply(FullscreenTransitions.Toggle(_state, _window.WindowState));

    /// <summary>Idempotent; driven by WebView2 ContainsFullScreenElementChanged.</summary>
    public void SetFullscreen(bool active)
    {
        if (active == _state.IsActive) return;
        _state = active
            ? Apply(FullscreenTransitions.Enter(_state, _window.WindowState))
            : Apply(FullscreenTransitions.Exit(_state));
    }

    private FullscreenState Apply(FullscreenState next)
    {
        if (next.IsActive)
        {
            // RestoreBounds works in both Normal and Maximized states and is
            // what we restore to on exit.
            _preFullscreenBounds = _window.RestoreBounds;

            if (ChromeManager is { } chrome && chrome.EnterMonitorFullscreen(_preFullscreenBounds))
            {
                // Window sized natively to the exact monitor rectangle; covers the taskbar.
                _nativeFullscreenActive = true;
            }
            else
            {
                _nativeFullscreenActive = false;
                _window.WindowState = WindowState.Maximized; // fallback if native sizing fails
            }

            _toolbar.Visibility = Visibility.Collapsed;
            _window.ResizeMode = ResizeMode.NoResize;
        }
        else
        {
            if (_nativeFullscreenActive && ChromeManager is { } chrome)
            {
                chrome.ExitMonitorFullscreen();
                if (next.SavedWindowState == WindowState.Maximized)
                    _window.WindowState = WindowState.Maximized;
            }
            else
            {
                _window.WindowState = next.SavedWindowState;
            }
            _nativeFullscreenActive = false;

            _toolbar.Visibility = Visibility.Visible;
            _window.ResizeMode = ResizeMode.CanResize;
        }
        return next;
    }
}