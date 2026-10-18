using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;

namespace StrideBrowser.Engine;

/// <summary>
/// Keeps a torn-off tab's WebView2 control bound to the window it is now shown in.
/// WebView2Base captures the window a control was built in and tears the control down from
/// that window's Closed event, so a tab that has moved to another window would die as soon
/// as the window it came from closes. The hook is private, so it is moved by hand. If the
/// SDK shape is not the one we know, nothing is changed.
/// </summary>
internal static class WebView2WindowBinding
{
    private const string BaseTypeName = "Microsoft.Web.WebView2.Wpf.WebView2Base";
    private const string ClosedHandler = "ParentWindow_Closed";
    private const string ParentWindowField = "_parentWindow";

    /// <summary>The native handle of a window, or zero when it has none.</summary>
    public static IntPtr HandleOf(Window? window)
    {
        if (window is null) return IntPtr.Zero;
        try { return new WindowInteropHelper(window).Handle; }
        catch (Exception ex) { Trace.WriteLine($"Window handle lookup failed: {ex.Message}"); return IntPtr.Zero; }
    }

    /// <summary>
    /// Moves the control's Closed subscription to the window that now hosts it, and
    /// reports whether that actually happened. A false answer means the control is
    /// still tied to <paramref name="from"/>, so its tab has to be rebuilt when that
    /// window closes instead of being carried across.
    /// The composition control cannot be moved this way: its D3D surface and controller
    /// are built against the host window's handle and stay there. TabEngine rebuilds those
    /// tabs when their host window closes instead.
    /// </summary>
    public static bool Rebind(FrameworkElement control, Window? from, Window? to)
    {
        if (FindInnerBase(control) is not { } owner) return false;
        var type = owner.GetType();
        var handler = type.GetMethod(ClosedHandler, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
        var field = type.GetField(ParentWindowField, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
        if (handler is null || field is null) return false;
        try
        {
            if (Delegate.CreateDelegate(typeof(EventHandler), owner, handler, throwOnBindFailure: false) is not EventHandler callback) return false;
            if (from is not null) from.Closed -= callback;
            if (to is not null) to.Closed += callback;
            field.SetValue(owner, to);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"WebView2 parent window rebind failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The object holding the window hook. Current controls keep a private WebView2Base
    /// field so the HwndHost and composition flavours share one implementation.
    /// </summary>
    private static object? FindInnerBase(FrameworkElement control)
    {
        for (var type = control.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            if (type.FullName == BaseTypeName) return control;
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType.FullName == BaseTypeName && field.GetValue(control) is { } inner) return inner;
            }
        }
        return null;
    }
}