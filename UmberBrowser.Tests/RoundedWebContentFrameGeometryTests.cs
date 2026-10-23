using UmberBrowser.Services.UI;
using System.Windows;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// The compact frame geometry. Wrong numbers here show up as a gap that is not the
/// Helium size, a square corner sliver past the clip, or a radius that collapses to
/// nothing at fractional DPI. The radii have to stay one stroke apart so the outline
/// inner edge and the clipped page corner describe the same arc.
/// </summary>
public sealed class RoundedWebContentFrameGeometryTests
{
    [Fact]
    public void GapAndStroke_MakeTheContentStartFourDipInAtTheSidesAndBottom()
    {
        Assert.Equal(3.0, RoundedWebContentFrameGeometry.GapDip);
        Assert.Equal(1.0, RoundedWebContentFrameGeometry.StrokeDip);
        Assert.Equal(4.0, RoundedWebContentFrameGeometry.ContentInsetDip);
    }

    [Fact]
    public void ContentRadius_IsTheOutlineRadiusLessTheStroke()
    {
        Assert.Equal(8.0, RoundedWebContentFrameGeometry.OutlineRadiusDip);
        Assert.Equal(7.0, RoundedWebContentFrameGeometry.ContentRadiusDip);
        Assert.Equal(RoundedWebContentFrameGeometry.OutlineRadiusDip - RoundedWebContentFrameGeometry.StrokeDip,
            RoundedWebContentFrameGeometry.ContentRadiusDip);
    }

    [Fact]
    public void OutlineGeometry_IsGapOutAtTheSidesAndBottomAndStrokeWide()
    {
        Assert.Equal(new Thickness(3, 0, 3, 3), RoundedWebContentFrameGeometry.OutlineMargin());
        Assert.Equal(new Thickness(1), RoundedWebContentFrameGeometry.OutlineThickness());
        Assert.Equal(new CornerRadius(8), RoundedWebContentFrameGeometry.OutlineCornerRadius());
    }

    [Fact]
    public void ContentMargin_IsTheOutlineBoundsPlusTheStrokeOnEveryEdge()
    {
        // The stroke lands between the frame surface and the page on every edge, so the
        // outline inner edge and the page edge are the same rectangle and the corner arcs
        // share a centre. The top gap is zero, so the page top is the stroke alone: three
        // DIP higher than the side and bottom inset alone would put it.
        var outline = RoundedWebContentFrameGeometry.OutlineMargin();
        var content = RoundedWebContentFrameGeometry.ContentMargin();
        var stroke = RoundedWebContentFrameGeometry.StrokeDip;

        Assert.Equal(0.0, RoundedWebContentFrameGeometry.TopGapDip);
        Assert.Equal(new Thickness(4, 1, 4, 4), content);
        Assert.Equal(outline.Left + stroke, content.Left);
        Assert.Equal(outline.Top + stroke, content.Top);
        Assert.Equal(outline.Right + stroke, content.Right);
        Assert.Equal(outline.Bottom + stroke, content.Bottom);
        Assert.Equal(content.Left - RoundedWebContentFrameGeometry.GapDip, content.Top);
    }

    [Theory]
    [InlineData(true, WindowState.Normal, ResizeMode.CanResize, true)]
    [InlineData(true, WindowState.Maximized, ResizeMode.CanResize, true)]
    [InlineData(true, WindowState.Normal, ResizeMode.NoResize, false)]
    [InlineData(true, WindowState.Maximized, ResizeMode.NoResize, false)]
    [InlineData(true, WindowState.Minimized, ResizeMode.CanResize, false)]
    [InlineData(false, WindowState.Normal, ResizeMode.CanResize, false)]
    [InlineData(false, WindowState.Maximized, ResizeMode.CanResize, false)]
    public void IsFrameOn_OnlyWhenEnabledAndResizableAndNotMinimized(
        bool enabled, WindowState state, ResizeMode mode, bool expected)
    {
        Assert.Equal(expected, RoundedWebContentFrameGeometry.IsFrameOn(enabled, state, mode));
    }

    [Theory]
    [InlineData(96, 7)]
    [InlineData(120, 9)]
    [InlineData(144, 11)]
    [InlineData(192, 14)]
    public void ContentRadiusPx_ScalesWithDpi(int dpi, int expected)
    {
        Assert.Equal(expected, RoundedWebContentFrameGeometry.ContentRadiusPx(dpi));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-96)]
    [InlineData(double.NaN)]
    public void ContentRadiusPx_BadDpi_FallsBackTo96(double dpi)
    {
        Assert.Equal(7, RoundedWebContentFrameGeometry.ContentRadiusPx(dpi));
    }

    [Fact]
    public void RegionEllipse_IsTwiceTheRadiusWhenTheHostIsBigEnough()
    {
        Assert.Equal(14, RoundedWebContentFrameGeometry.RegionEllipsePx(7, 400, 300));
    }

    [Fact]
    public void RegionEllipse_ClampsToTheSmallerHostSide()
    {
        // A host narrower than the diameter still has to produce a valid region.
        Assert.Equal(6, RoundedWebContentFrameGeometry.RegionEllipsePx(7, 6, 300));
        Assert.Equal(4, RoundedWebContentFrameGeometry.RegionEllipsePx(7, 400, 4));
    }

    [Fact]
    public void RegionEllipse_EmptyHost_IsSquare()
    {
        Assert.Equal(0, RoundedWebContentFrameGeometry.RegionEllipsePx(7, 0, 0));
    }
}
