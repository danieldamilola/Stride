using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UmberBrowser.Models;
using UmberBrowser.Services.UI;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Covers the clip lifetime, not the geometry. A page host sits behind a native child
/// window that a control can drop and build again, and a host can move to another window.
/// The controller has to clip the handle that exists now, has to leave a handle another
/// window owns completely alone, and has to retry a native call that did not land instead
/// of remembering it as done. Every check drives the real controller through the native
/// seam and through real layout, so a change that only fixes the seam and not the pass
/// fails here.
/// </summary>
public class RoundedWebContentFrameControllerTests
{
    /// <summary>
    /// WPF windows and panels are apartment affine, so the body runs on its own STA
    /// thread and the first failure is rethrown on the test thread with its stack intact.
    /// </summary>
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    /// <summary>How a region write behaves, so a null region and a refused set are both driven.</summary>
    private enum SetOutcome
    {
        Landed,
        RegionWasNull,
        SetWasRefused,
    }

    private sealed class FakeOps : IRoundedFrameHostOps
    {
        public readonly Dictionary<FrameworkElement, IntPtr> Handles = new();
        public readonly List<IntPtr> SetHandles = new();
        public readonly List<IntPtr> ClearHandles = new();

        public bool IsOwned = true;
        public bool HasClientSize = true;
        public SetOutcome Outcome = SetOutcome.Landed;
        public bool ClearLands = true;
        public int NullRegionCount;
        public int RefusedSetCount;
        public int ClearsThatLanded;

        public bool IsPageHost(FrameworkElement child) => true;

        public IntPtr HandleOf(FrameworkElement host) =>
            Handles.TryGetValue(host, out var handle) ? handle : IntPtr.Zero;

        public bool IsOwnedByHost(FrameworkElement host, IntPtr handle) => IsOwned;

        public bool TryGetClientSize(IntPtr handle, out int width, out int height)
        {
            width = HasClientSize ? 800 : 0;
            height = HasClientSize ? 600 : 0;
            return HasClientSize;
        }

        public int DpiOf(IntPtr handle) => 96;

        public bool TrySetRegion(IntPtr handle, int width, int height, int ellipse)
        {
            SetHandles.Add(handle);
            if (Outcome == SetOutcome.RegionWasNull) { NullRegionCount++; return false; }
            if (Outcome == SetOutcome.SetWasRefused) { RefusedSetCount++; return false; }
            return true;
        }

        public bool TryClearRegion(IntPtr handle)
        {
            ClearHandles.Add(handle);
            if (!ClearLands) return false;
            ClearsThatLanded++;
            return true;
        }
    }

    private sealed class Harness : IDisposable
    {
        public readonly Window Window = new();
        public readonly Grid Host = new();
        public readonly Border HostElement = new();
        public readonly Border Outline = new();
        public readonly BrowserSettings Settings = new();
        public readonly FakeOps Ops;
        public readonly RoundedWebContentFrameController Controller;

        public Harness(FakeOps ops)
        {
            Ops = ops;
            Settings.RoundedWebContentFrameEnabled = true;
            Host.Children.Add(HostElement);
            ops.Handles[HostElement] = new IntPtr(0x1111);

            // A real window is what gives the panel a layout root, and the controller
            // reacts to LayoutUpdated. It is invisible, non activating and not in the
            // taskbar, so it only exists to drive the layout the product relies on.
            Window.Content = Host;
            Window.Width = 320;
            Window.Height = 240;
            Window.WindowStyle = WindowStyle.None;
            Window.ShowInTaskbar = false;
            Window.ShowActivated = false;
            Window.Opacity = 0;
            Window.Show();
            Pump();

            Controller = new RoundedWebContentFrameController(Window, Host, Settings, Outline, ops);
        }

        /// <summary>
        /// One real layout pass, which is what the product sees when a host is added,
        /// revealed, resized or rebuilt. The element is re-invalidated first so the pass
        /// is never skipped as already up to date.
        /// </summary>
        public void Layout()
        {
            HostElement.Width = HostElement.Width == 400 ? 401 : 400;
            Window.UpdateLayout();
            Pump();
        }

        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        public void Dispose()
        {
            Controller.Dispose();
            Window.Close();
        }
    }

    // The handle that exists now.

    [Fact]
    public void Layout_ControlDroppedAndRebuiltItsWindowAtTheSameBounds_GetsTheRegionAgain() => RunSta(() =>
    {
        var ops = new FakeOps();
        using var harness = new Harness(ops);
        Assert.Equal(new[] { new IntPtr(0x1111) }, ops.SetHandles);

        // Same control, same bounds, new child window. The new handle owns no region.
        ops.Handles[harness.HostElement] = new IntPtr(0x2222);
        harness.Layout();

        Assert.Equal(new[] { new IntPtr(0x1111), new IntPtr(0x2222) }, ops.SetHandles);
        Assert.Empty(ops.ClearHandles);
    });

    [Fact]
    public void Layout_HostWithNoWindowYet_IsClippedOnceItsWindowExists() => RunSta(() =>
    {
        var ops = new FakeOps();
        using var harness = new Harness(ops);

        ops.Handles[harness.HostElement] = IntPtr.Zero;
        harness.Layout();
        Assert.Equal(new[] { new IntPtr(0x1111) }, ops.SetHandles);

        ops.Handles[harness.HostElement] = new IntPtr(0x3333);
        harness.Layout();

        Assert.Equal(new[] { new IntPtr(0x1111), new IntPtr(0x3333) }, ops.SetHandles);
    });

    // A host another window owns is never touched.

    [Fact]
    public void Layout_HostAnotherWindowHolds_IsNeitherSetNorCleared() => RunSta(() =>
    {
        var ops = new FakeOps();
        using var harness = new Harness(ops);
        Assert.Single(ops.SetHandles);

        // The control moved to the other window's panel, which owns its region now. A pass
        // here that would turn the frame off must not strip the rounding that window set.
        ops.IsOwned = false;
        harness.Settings.RoundedWebContentFrameEnabled = false;
        harness.Layout();

        Assert.Single(ops.SetHandles);
        Assert.Empty(ops.ClearHandles);
    });

    [Fact]
    public void Dispose_HostAnotherWindowHolds_LeavesItsRegionAlone() => RunSta(() =>
    {
        var ops = new FakeOps();
        var harness = new Harness(ops);
        ops.IsOwned = false;

        harness.Dispose();

        Assert.Empty(ops.ClearHandles);
    });

    [Fact]
    public void Dispose_HostThisWindowStillHolds_ClearsTheRegion() => RunSta(() =>
    {
        var ops = new FakeOps();
        var harness = new Harness(ops);

        harness.Dispose();

        Assert.Equal(new[] { new IntPtr(0x1111) }, ops.ClearHandles);
        Assert.Equal(1, ops.ClearsThatLanded);
    });

    // A native call that did not land is not remembered as done. An extra layout pass can
    // run the whole pass more than once, so these count attempts made, not passes run.

    [Fact]
    public void Layout_NullRegion_IsRetriedUntilItLands() => RunSta(() =>
    {
        var ops = new FakeOps { Outcome = SetOutcome.RegionWasNull };
        using var harness = new Harness(ops);
        Assert.Equal(1, ops.NullRegionCount);

        var attempts = ops.NullRegionCount;
        harness.Layout();
        Assert.True(ops.NullRegionCount > attempts,
            "a null region was remembered as applied, so the next pass skipped the window");

        ops.Outcome = SetOutcome.Landed;
        harness.Layout();
        var landed = ops.SetHandles.Count;

        // Once a write lands it is remembered, so a further pass makes no call at all.
        harness.Layout();
        Assert.Equal(landed, ops.SetHandles.Count);
    });

    [Fact]
    public void Layout_RefusedSet_IsRetriedUntilItLands() => RunSta(() =>
    {
        var ops = new FakeOps { Outcome = SetOutcome.SetWasRefused };
        using var harness = new Harness(ops);
        Assert.Equal(1, ops.RefusedSetCount);

        var attempts = ops.RefusedSetCount;
        harness.Layout();
        Assert.True(ops.RefusedSetCount > attempts,
            "a refused set was remembered as applied, so the next pass skipped the window");

        ops.Outcome = SetOutcome.Landed;
        harness.Layout();
        var landed = ops.SetHandles.Count;
        harness.Layout();
        Assert.Equal(landed, ops.SetHandles.Count);
    });

    [Fact]
    public void Layout_RefusedClear_IsRetriedUntilItLands() => RunSta(() =>
    {
        var ops = new FakeOps();
        using var harness = new Harness(ops);

        ops.ClearLands = false;
        harness.Settings.RoundedWebContentFrameEnabled = false;
        Assert.Single(ops.ClearHandles);
        Assert.Equal(0, ops.ClearsThatLanded);

        var attempts = ops.ClearHandles.Count;
        harness.Layout();
        Assert.True(ops.ClearHandles.Count > attempts,
            "a refused clear was remembered as cleared, so the next pass skipped the window");

        ops.ClearLands = true;
        harness.Layout();
        var landed = ops.ClearHandles.Count;
        Assert.Equal(1, ops.ClearsThatLanded);

        harness.Layout();
        Assert.Equal(landed, ops.ClearHandles.Count);
    });

    [Fact]
    public void Layout_FrameOffThenOnAgain_WritesOnlyWhenTheStateChanges() => RunSta(() =>
    {
        var ops = new FakeOps();
        using var harness = new Harness(ops);

        harness.Layout();
        harness.Layout();
        Assert.Single(ops.SetHandles);
        Assert.Empty(ops.ClearHandles);

        harness.Settings.RoundedWebContentFrameEnabled = false;
        harness.Layout();
        Assert.Single(ops.ClearHandles);

        harness.Settings.RoundedWebContentFrameEnabled = true;
        harness.Layout();
        Assert.Equal(2, ops.SetHandles.Count);
    });

    [Fact]
    public void Layout_HostWithNoPositiveClientSize_IsNeverClipped() => RunSta(() =>
    {
        var ops = new FakeOps { HasClientSize = false };
        using var harness = new Harness(ops);

        // A host with no client area is never clipped, and no later pass clips it either,
        // because nothing about the host changed.
        Assert.Empty(ops.SetHandles);
        harness.Layout();
        harness.Layout();
        Assert.Empty(ops.SetHandles);
    });
}
