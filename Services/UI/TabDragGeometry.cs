using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using UmberBrowser.Interop;

namespace UmberBrowser.Services.UI;

public static class TabDragGeometry
{
    public const double DetachDistance = 32;

    public static bool ShouldDetach(Rect strip, Point cursor, bool cancelled, bool dropped)
    {
        if (cancelled || dropped || strip.IsEmpty) return false;
        strip.Inflate(DetachDistance, DetachDistance);
        return !strip.Contains(cursor);
    }

    public static int InsertionIndex(IReadOnlyList<double> centers, double x)
    {
        for (var i = 0; i < centers.Count; i++) if (x < centers[i]) return i;
        return centers.Count;
    }

    internal static Point CursorPosition()
    {
        GetCursorPos(out var point);
        return new Point(point.X, point.Y);
    }

    internal static void PositionWindow(Window window, Point point)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var dpi = VisualTreeHelper.GetDpi(window);
        var x = (int)(point.X - 80 * dpi.DpiScaleX);
        var y = (int)(point.Y - 20 * dpi.DpiScaleY);
        var monitor = MonitorFromPoint(new NativeMethods.POINT { X = (int)point.X, Y = (int)point.Y }, 2);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            x = Math.Clamp(x, info.rcWork.Left, Math.Max(info.rcWork.Left, info.rcWork.Right - (int)(window.Width * dpi.DpiScaleX)));
            y = Math.Clamp(y, info.rcWork.Top, Math.Max(info.rcWork.Top, info.rcWork.Bottom - (int)(window.Height * dpi.DpiScaleY)));
        }
        NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativeMethods.POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativeMethods.POINT point, uint flags);
}