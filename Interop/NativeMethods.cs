using System.Runtime.InteropServices;

namespace StrideBrowser.Interop;

/// <summary>
/// P/Invoke declarations and native struct definitions used by the window chrome.
/// </summary>
internal static class NativeMethods
{
    internal const int WM_GETMINMAXINFO = 0x0024;
    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWCP_ROUND = 2;
    internal const int MONITOR_DEFAULTTONEAREST = 2;

    internal const int GWL_EXSTYLE = -20;
    internal const int GWL_HWNDPARENT = -8;
    internal const int WS_EX_TOPMOST = 0x00000008;

    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>
    /// Sets the AppUserModelID for the current process. This tells Windows
    /// Task Manager to group all child processes (WebView2, GPU, etc.)
    /// under the main Stride entry instead of "Utility".
    /// Must be called before creating any windows.
    /// </summary>
    [DllImport("shell32.dll", SetLastError = true)]
    internal static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string AppID);

    // ── Auto-hide taskbar detection ──
    internal const int ABM_GETSTATE = 0x00000004;
    internal const int ABS_AUTOHIDE = 0x0001;
    internal const int ABM_GETTASKBARPOS = 0x00000005;

    [StructLayout(LayoutKind.Sequential)]
    internal struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    internal static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

    /// <summary>Returns true if the Windows taskbar is set to auto-hide.</summary>
    internal static bool IsTaskbarAutoHide()
    {
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        var state = (int)SHAppBarMessage(ABM_GETSTATE, ref abd);
        return (state & ABS_AUTOHIDE) != 0;
    }

    /// <summary>
    /// Returns the edge the taskbar is on: 0=Left, 1=Top, 2=Right, 3=Bottom.
    /// Returns 3 (bottom) as default if detection fails.
    /// </summary>
    internal static uint GetTaskbarEdge()
    {
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        SHAppBarMessage(ABM_GETTASKBARPOS, ref abd);
        return abd.uEdge;
    }

    // ── Foreground window activation ──
    // Window.Activate() alone is unreliable on Windows 10/11 when the app
    // is in the background. These Win32 calls are the standard pattern used
    // by Chrome, Firefox, and other single-instance apps.

    internal const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);
}
