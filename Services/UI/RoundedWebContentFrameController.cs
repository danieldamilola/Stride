using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using UmberBrowser.Interop;
using UmberBrowser.Models;

namespace UmberBrowser.Services.UI;

/// <summary>
/// The native boundary the clip pass drives. The product uses
/// <see cref="RoundedFrameNativeOps"/> and nothing else. The lifetime tests substitute
/// this seam because handle identity, host ownership and native failure cannot be
/// produced against a real child window in a test host.
/// </summary>
public interface IRoundedFrameHostOps
{
    /// <summary>True when a panel child is a page host.</summary>
    bool IsPageHost(FrameworkElement child);

    /// <summary>The host window handle, or zero while the control has not built one.</summary>
    IntPtr HandleOf(FrameworkElement host);

    /// <summary>
    /// True only while the control is still a child of this controller's panel, still in
    /// this controller's window, and backed by a window this process owns. A transferred
    /// control fails this, so neither a set nor a clear can ever reach the receiving
    /// window's region.
    /// </summary>
    bool IsOwnedByHost(FrameworkElement host, IntPtr handle);

    /// <summary>The host's client size. False when it has no positive size yet.</summary>
    bool TryGetClientSize(IntPtr handle, out int width, out int height);

    /// <summary>The host window's DPI, defaulted when the system reports none.</summary>
    int DpiOf(IntPtr handle);

    /// <summary>Clips the handle to a rounded rectangle. True only when the region landed.</summary>
    bool TrySetRegion(IntPtr handle, int width, int height, int ellipse);

    /// <summary>Removes the region from the handle. True only when the call landed.</summary>
    bool TryClearRegion(IntPtr handle);
}

/// <summary>
/// The production native boundary. Every call here is a real Win32 call on the page
/// host's own child window. Owner and process checks happen before the region is written,
/// because the handle is only ever this process's own host and only while this window
/// still holds it.
/// </summary>
public sealed class RoundedFrameNativeOps : IRoundedFrameHostOps
{
    private readonly Window _window;
    private readonly Panel _host;

    public RoundedFrameNativeOps(Window window, Panel host)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public bool IsPageHost(FrameworkElement child) => child is WebView2;

    public IntPtr HandleOf(FrameworkElement host)
    {
        try
        {
            return ((WebView2)host).Handle;
        }
        catch (InvalidOperationException)
        {
            // The host window is not built yet. The next layout pass will find it.
            return IntPtr.Zero;
        }
    }

    public bool IsOwnedByHost(FrameworkElement host, IntPtr handle)
    {
        // The panel child is the only thing this instance may touch, and only while the
        // panel still holds it inside this window. A transfer moves the control to the
        // other window's panel, so the old owner refuses both the set and the clear.
        if (!_host.Children.Contains(host)) return false;
        if (!ReferenceEquals(Window.GetWindow(host), _window)) return false;
        NativeMethods.GetWindowThreadProcessId(handle, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    public bool TryGetClientSize(IntPtr handle, out int width, out int height)
    {
        if (NativeMethods.GetClientRect(handle, out var rect) && rect.Right > 0 && rect.Bottom > 0)
        {
            width = rect.Right;
            height = rect.Bottom;
            return true;
        }

        width = 0;
        height = 0;
        return false;
    }

    public int DpiOf(IntPtr handle)
    {
        var dpi = NativeMethods.GetDpiForWindow(handle);
        return dpi == 0 ? 96 : (int)dpi;
    }

    public bool TrySetRegion(IntPtr handle, int width, int height, int ellipse)
    {
        var region = NativeMethods.CreateRoundRectRgn(0, 0, width + 1, height + 1, ellipse, ellipse);
        if (region == IntPtr.Zero)
        {
            Trace.WriteLine("RoundedWebContentFrame: CreateRoundRectRgn returned null");
            return false;
        }

        if (NativeMethods.SetWindowRgn(handle, region, true) == 0)
        {
            // The system takes ownership only on success, so the caller still owns it.
            Trace.WriteLine($"RoundedWebContentFrame: SetWindowRgn failed ({Marshal.GetLastWin32Error()})");
            NativeMethods.DeleteObject(region);
            return false;
        }

        return true;
    }

    public bool TryClearRegion(IntPtr handle)
    {
        if (NativeMethods.SetWindowRgn(handle, IntPtr.Zero, true) != 0) return true;
        Trace.WriteLine($"RoundedWebContentFrame: clearing the region failed ({Marshal.GetLastWin32Error()})");
        return false;
    }
}

/// <summary>
/// Clips each web page host in one window to the compact rounded frame and drives the
/// outline that sits outside the clip.
///
/// The page is an HwndHost child window, so XAML rounded corners cannot reach it and
/// WPF cannot hit test or clip through it. The child window belongs to this process, so
/// it can be rounded directly: <c>SetWindowRgn</c> with a round rectangle region cuts the
/// four corners of the real page. Only the host's own handle is touched. The renderer
/// window and the GPU window belong to other processes and are never named here.
///
/// The region is binary and integer, so the radius comes from the window DPI rather than
/// the outline, which WPF antialiases. One instance per window owns the region for every
/// page host that window holds, and it writes a region only while it still owns the host:
/// a transferred tab belongs to the receiving window, which clips it, and this instance
/// must never set or clear the handle the receiving window just rounded.
/// </summary>
public sealed class RoundedWebContentFrameController : IDisposable
{
    private readonly Window _window;
    private readonly Panel _host;
    private readonly BrowserSettings _settings;
    private readonly Border _outline;
    private readonly IRoundedFrameHostOps _ops;
    private readonly Dictionary<FrameworkElement, HostClip> _clips = new();
    private DependencyPropertyDescriptor? _resizeMode;
    private bool _applying;
    private bool _disposed;

    private const string SettingName = nameof(BrowserSettings.RoundedWebContentFrameEnabled);

    /// <summary>
    /// What was last pushed to one host window. The handle is part of the record because a
    /// control can drop its window and build a new one at the same bounds: a replacement
    /// handle must not read as an unchanged pass, or the new window never gets a region.
    /// </summary>
    private struct HostClip : IEquatable<HostClip>
    {
        public IntPtr Handle;
        public int Width;
        public int Height;
        public int Ellipse;
        public bool Clipped;

        public bool Equals(HostClip other) =>
            Handle == other.Handle && Width == other.Width && Height == other.Height &&
            Ellipse == other.Ellipse && Clipped == other.Clipped;

        public override bool Equals(object? obj) => obj is HostClip other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Handle, Width, Height, Ellipse, Clipped);
    }

    public RoundedWebContentFrameController(Window window, Panel host, BrowserSettings settings, Border outline)
        : this(window, host, settings, outline, null)
    {
    }

    /// <summary>
    /// <paramref name="hostOps"/> is the native boundary. The product passes null and gets
    /// the real one, which reads the real child window handle. A test passes its own, so a
    /// replacement handle, a lost host and a refused native call can all be driven.
    /// </summary>
    public RoundedWebContentFrameController(Window window, Panel host, BrowserSettings settings, Border outline,
        IRoundedFrameHostOps? hostOps)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _outline = outline ?? throw new ArgumentNullException(nameof(outline));
        _ops = hostOps ?? new RoundedFrameNativeOps(_window, _host);

        _settings.PropertyChanged += OnSettingChanged;
        _window.StateChanged += OnWindowStateChanged;
        _window.Closed += OnWindowClosed;
        _resizeMode = DependencyPropertyDescriptor.FromProperty(Window.ResizeModeProperty, typeof(Window));
        _resizeMode?.AddValueChanged(_window, OnResizeModeChanged);

        // Loaded and Unloaded are Direct routed events, so they never reach this panel.
        // LayoutUpdated is the reliable signal that the host panel's children changed,
        // and it is not a timer: it fires as layout completes, which is what added,
        // removed, revealed, resized and rebuilt hosts all trigger.
        _host.LayoutUpdated += OnHostLayoutUpdated;

        Apply();
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == SettingName) Apply();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => Apply();

    private void OnResizeModeChanged(object? sender, EventArgs e) => Apply();

    private void OnHostLayoutUpdated(object? sender, EventArgs e) => Apply();

    private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

    /// <summary>
    /// Re-evaluates the frame for this window: the outline and the region on every host.
    /// The whole pass is guarded against re-entry, because the native region call runs
    /// the host window's own procedure and that can drive another layout pass.
    /// </summary>
    private void Apply()
    {
        if (_disposed || _applying) return;
        _applying = true;
        try
        {
            var frameOn = RoundedWebContentFrameGeometry.IsFrameOn(
                _settings.RoundedWebContentFrameEnabled, _window.WindowState, _window.ResizeMode);

            _outline.Visibility = frameOn ? Visibility.Visible : Visibility.Collapsed;
            if (frameOn)
            {
                _outline.Margin = RoundedWebContentFrameGeometry.OutlineMargin();
                _outline.BorderThickness = RoundedWebContentFrameGeometry.OutlineThickness();
                _outline.CornerRadius = RoundedWebContentFrameGeometry.OutlineCornerRadius();
            }

            SyncHosts();
            foreach (var host in _clips.Keys)
                ApplyClip(host, frameOn);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"RoundedWebContentFrameController.Apply failed: {ex}");
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>
    /// Adds every page host the panel now holds and drops the ones it let go of. A host
    /// is only ever a control this window's panel owns, so a transfer in or out moves
    /// ownership of the clip with the control. Dropping a host clears nothing: it may
    /// already belong to another window, which is the only writer of its region now.
    /// </summary>
    private void SyncHosts()
    {
        HashSet<FrameworkElement>? present = null;
        foreach (var child in _host.Children)
        {
            if (child is not FrameworkElement element) continue;
            if (!_ops.IsPageHost(element)) continue;
            (present ??= new HashSet<FrameworkElement>()).Add(element);
            if (!_clips.ContainsKey(element)) Register(element);
        }

        if (_clips.Count == 0) return;

        List<FrameworkElement>? gone = null;
        foreach (var host in _clips.Keys)
        {
            if (present is null || !present.Contains(host)) (gone ??= new List<FrameworkElement>()).Add(host);
        }

        if (gone is null) return;
        foreach (var host in gone) Unregister(host);
    }

    private void Register(FrameworkElement host)
    {
        _clips[host] = default;
        host.SizeChanged += OnHostSizeChanged;
        host.IsVisibleChanged += OnHostIsVisibleChanged;
    }

    private void Unregister(FrameworkElement host)
    {
        host.SizeChanged -= OnHostSizeChanged;
        host.IsVisibleChanged -= OnHostIsVisibleChanged;
        _clips.Remove(host);
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e) => Apply();

    private void OnHostIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => Apply();

    private void ApplyClip(FrameworkElement host, bool frameOn)
    {
        var handle = _ops.HandleOf(host);

        // Ownership first, and for both directions. A control another window holds must
        // not be set and must not be cleared: that window is the only writer of its region
        // now, and clearing here would strip the rounding it just applied. A zero handle
        // means the control has no window yet, so nothing is remembered for it and the
        // pass runs again once a window exists.
        if (handle == IntPtr.Zero || !_ops.IsOwnedByHost(host, handle))
        {
            _clips[host] = default;
            return;
        }

        var clipped = false;
        var width = 0;
        var height = 0;
        var ellipse = 0;

        // A hidden host keeps its last arranged size, so only clip a host that has one.
        // Showing it again raises a layout pass, which lands here and applies the region.
        if (frameOn && _ops.TryGetClientSize(handle, out var clientWidth, out var clientHeight))
        {
            width = clientWidth;
            height = clientHeight;
            var radius = RoundedWebContentFrameGeometry.ContentRadiusPx(_ops.DpiOf(handle));
            ellipse = RoundedWebContentFrameGeometry.RegionEllipsePx(radius, width, height);
            clipped = true;
        }

        var desired = new HostClip
        {
            Handle = handle,
            Width = width,
            Height = height,
            Ellipse = ellipse,
            Clipped = clipped,
        };

        if (_clips[host].Equals(desired)) return;

        // The record moves only when the native call actually landed. A null region or a
        // refused set keeps the previous record, so the next pass with the same geometry
        // retries instead of remembering a region that is not on the window.
        if (ApplyRegion(handle, desired)) _clips[host] = desired;
    }

    private bool ApplyRegion(IntPtr handle, HostClip clip) =>
        clip.Clipped
            ? _ops.TrySetRegion(handle, clip.Width, clip.Height, clip.Ellipse)
            : _ops.TryClearRegion(handle);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _settings.PropertyChanged -= OnSettingChanged;
        _window.StateChanged -= OnWindowStateChanged;
        _window.Closed -= OnWindowClosed;
        _resizeMode?.RemoveValueChanged(_window, OnResizeModeChanged);
        _host.LayoutUpdated -= OnHostLayoutUpdated;

        // Drop the clip from every host this window still owns. A host that has already
        // moved to another window is left alone, because that window owns its region and
        // this window closing must not open the rounded corners it just applied.
        _applying = true;
        try
        {
            foreach (var host in _clips.Keys)
            {
                host.SizeChanged -= OnHostSizeChanged;
                host.IsVisibleChanged -= OnHostIsVisibleChanged;
                try
                {
                    var handle = _ops.HandleOf(host);
                    if (handle != IntPtr.Zero && _ops.IsOwnedByHost(host, handle))
                        _ops.TryClearRegion(handle);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"RoundedWebContentFrame: clear region failed: {ex.Message}");
                }
            }
        }
        finally
        {
            _clips.Clear();
            _applying = false;
        }
    }
}
