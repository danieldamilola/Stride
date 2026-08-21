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
