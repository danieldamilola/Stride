using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using StrideBrowser.Interop;

namespace StrideBrowser.Helpers;

/// <summary>
/// Handles native window chrome operations: maximize bounds fix,
/// rounded corners, and WndProc hooking.
/// </summary>
public sealed class WindowChromeHelper
{
    private readonly Window _window;
    private HwndSource? _hwndSource;

    public WindowChromeHelper(Window window)
    {
        _window = window;
    }

    public void Initialize()
    {
        ApplyRoundedCorners();

        var hwnd = new WindowInteropHelper(_window).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
    }

    public void Unhook()
    {
        try
        {
            _hwndSource?.RemoveHook(WndProc);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to unhook WndProc: {ex}");
        }
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
            var hwnd = new WindowInteropHelper(_window).Handle;
            var preference = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch { /* Pre-Windows 11 - graceful fallback to square corners */ }
    }
}

