using System.Windows;
using UmberBrowser.Services.UI;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Covers the pure part of the edge resize fix: how the WindowChrome resize
/// border turns into a page host inset, and when that inset is in use. The
/// WebView2 child window covers the border below the toolbar and answers
/// HTCLIENT, so the host is kept clear of the border while the border is live.
/// </summary>
public class WindowEdgeResizeInsetTests
{
    [Fact]
    public void InsetFor_UniformBorder_KeepsSidesAndBottomAndDropsTop()
    {
        var inset = WindowEdgeResizeInset.InsetFor(new Thickness(6));

        Assert.Equal(new Thickness(6, 0, 6, 6), inset);
    }

    [Fact]
    public void InsetFor_AsymmetricBorder_PreservesLeftRightBottomAndZeroesTop()
    {
        var inset = WindowEdgeResizeInset.InsetFor(new Thickness(4, 9, 5, 7));

        Assert.Equal(new Thickness(4, 0, 5, 7), inset);
    }

    [Fact]
    public void InsetFor_ZeroBorder_IsEmptySoNothingIsApplied()
    {
        var inset = WindowEdgeResizeInset.InsetFor(new Thickness(0));

        Assert.Equal(new Thickness(0, 0, 0, 0), inset);
    }

    [Fact]
    public void WantsInset_NormalResizableWindow_True()
    {
        Assert.True(WindowEdgeResizeInset.WantsInset(WindowState.Normal, ResizeMode.CanResize));
    }

    [Fact]
    public void WantsInset_NormalResizableGripWindow_True()
    {
        Assert.True(WindowEdgeResizeInset.WantsInset(WindowState.Normal, ResizeMode.CanResizeWithGrip));
    }

    [Fact]
    public void WantsInset_FullscreenNoResize_False()
    {
        // FullscreenController sets NoResize, so the inset must not survive into it.
        Assert.False(WindowEdgeResizeInset.WantsInset(WindowState.Normal, ResizeMode.NoResize));
    }

    [Fact]
    public void WantsInset_Maximized_False()
    {
        // A maximized window has no side border to resize, so no inset is wanted.
        Assert.False(WindowEdgeResizeInset.WantsInset(WindowState.Maximized, ResizeMode.CanResize));
    }

    [Fact]
    public void WantsInset_Minimized_False()
    {
        Assert.False(WindowEdgeResizeInset.WantsInset(WindowState.Minimized, ResizeMode.CanResize));
    }

    [Fact]
    public void WantsInset_MaximizedNoResize_False()
    {
        Assert.False(WindowEdgeResizeInset.WantsInset(WindowState.Maximized, ResizeMode.NoResize));
    }

    [Fact]
    public void CombinedInset_RoundedFrameOn_IsTheFrameContentMarginWithNoTopGap()
    {
        // Normal and maximized both keep the frame; only fullscreen drops it, and the
        // caller signals that by passing frameOn false.
        Assert.Equal(new Thickness(4, 1, 4, 4),
            WindowEdgeResizeInset.CombinedInset(true, WindowState.Normal, ResizeMode.CanResize, new Thickness(6)));
        Assert.Equal(new Thickness(4, 1, 4, 4),
            WindowEdgeResizeInset.CombinedInset(true, WindowState.Maximized, ResizeMode.CanResize, new Thickness(6)));
    }

    [Fact]
    public void CombinedInset_FrameOn_NormalWindow_HoldsThePageOneStrokeUnderTheToolbar()
    {
        // The top gap is gone, so the page starts at the outline stroke under the toolbar
        // instead of a further gap down. That is the three DIP the frame top came up.
        var inset = WindowEdgeResizeInset.CombinedInset(true, WindowState.Normal, ResizeMode.CanResize, new Thickness(6));

        Assert.Equal(RoundedWebContentFrameGeometry.StrokeDip, inset.Top);
        Assert.Equal(inset.Left - RoundedWebContentFrameGeometry.GapDip, inset.Top);
    }

    [Fact]
    public void CombinedInset_FrameOn_DoesNotAddTheSixDipBorderOnTopOfTheGap()
    {
        // The frame gap replaces the resize inset while the frame is on. Adding the two
        // would give a nine dip visible band, which is not the Helium size.
        var inset = WindowEdgeResizeInset.CombinedInset(true, WindowState.Normal, ResizeMode.CanResize, new Thickness(6));

        Assert.NotEqual(new Thickness(9, 0, 9, 9), inset);
        Assert.Equal(4, inset.Left);
        Assert.Equal(1, inset.Top);
    }

    [Fact]
    public void CombinedInset_FrameOff_NormalWindow_KeepsTheRestoredResizeInset()
    {
        Assert.Equal(new Thickness(6, 0, 6, 6),
            WindowEdgeResizeInset.CombinedInset(false, WindowState.Normal, ResizeMode.CanResize, new Thickness(6)));
    }

    [Fact]
    public void CombinedInset_FrameOff_MaximizedAndFullscreen_AreEmpty()
    {
        Assert.Equal(new Thickness(0),
            WindowEdgeResizeInset.CombinedInset(false, WindowState.Maximized, ResizeMode.CanResize, new Thickness(6)));
        Assert.Equal(new Thickness(0),
            WindowEdgeResizeInset.CombinedInset(false, WindowState.Normal, ResizeMode.NoResize, new Thickness(6)));
    }
}
