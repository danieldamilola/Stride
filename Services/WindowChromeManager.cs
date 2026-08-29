using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using StrideBrowser.Interop;

namespace StrideBrowser.Services;

/// <summary>
/// Encapsulates native Windows API (P/Invoke) calls that manage window chrome,
/// activation, and taskbar autohide behaviors.
/// </summary>
public sealed class WindowChromeManager
{
    private readonly Window _window;
    private IntPtr _hwnd;
    private HwndSource? _source;

    // Optional callback for horizontal scroll wheel
    public Action<int>? OnMouseHWheel;

    private Rect _preFullscreenDipBounds;

    public WindowChromeManager(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public void Initialize()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        ApplyRoundedCorners();
    }

    /// <summary>
    /// Sizes the window to the exact monitor rectangle natively so fullscreen
    /// covers the taskbar, and strips DWM rounded corners and border so no
    /// light edge shows around fullscreen content.
    /// </summary>
    public bool EnterMonitorFullscreen(Rect dipRestoreBounds)
    {
        if (_hwnd == IntPtr.Zero) return false;

        var monitor = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return false;
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref mi)) return false;

        _preFullscreenDipBounds = dipRestoreBounds;

        _window.WindowState = WindowState.Normal;
        var mon = mi.rcMonitor;
        if (!NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero,
            mon.Left, mon.Top, mon.Right - mon.Left, mon.Bottom - mon.Top,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED))
        {
            return false;
        }

        ApplyFullscreenChrome(fullscreen: true);
        return true;
    }

    /// <summary>Restores the window bounds and DWM chrome captured on enter.</summary>
    public void ExitMonitorFullscreen()
    {
        ApplyFullscreenChrome(fullscreen: false);

        if (_hwnd == IntPtr.Zero) return;
        var source = PresentationSource.FromVisual(_window);
        var device = source?.CompositionTarget.TransformToDevice;
        double sx = device?.M11 ?? 1.0;
        double sy = device?.M22 ?? 1.0;

        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero,
            (int)(_preFullscreenDipBounds.X * sx),
            (int)(_preFullscreenDipBounds.Y * sy),
            (int)(_preFullscreenDipBounds.Width * sx),
            (int)(_preFullscreenDipBounds.Height * sy),
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
    }

    private void ApplyFullscreenChrome(bool fullscreen)
    {
        try
        {
            var corners = fullscreen ? NativeMethods.DWMWCP_DONOTROUND : NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));

            var border = fullscreen ? NativeMethods.DWMWA_COLOR_NONE : NativeMethods.DWMWA_COLOR_DEFAULT;
            NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch { /* Pre-Windows 11 - graceful fallback */ }
    }

    public void BringToFront()
    {
        if (_hwnd != IntPtr.Zero)
        {
            if (NativeMethods.IsIconic(_hwnd))
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_RESTORE);

            NativeMethods.SetForegroundWindow(_hwnd);
        }

        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        _window.Activate();
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            var preference = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch { /* Pre-Windows 11 - graceful fallback to square corners */ }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEHWHEEL = 0x020E;

        if (msg == WM_MOUSEHWHEEL)
        {
            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            OnMouseHWheel?.Invoke(delta);
            handled = true;
        }
        else if (msg == NativeMethods.WM_GETMINMAXINFO)
        {
            HandleMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void HandleMinMaxInfo(IntPtr hwnd, IntPtr lParam)
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
    }
}
