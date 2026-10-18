using System;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// The minimum track size Windows clamps interactive resizes to, converted
/// from the declared DIP floor at the window's current scale. Every interactive
/// edge, corner and snap resize asks for this reply, so wrong values here let
/// the window shrink past the toolbar's own layout floor.
/// </summary>
public sealed class WindowMinTrackSizeTests
{
    [Theory]
    [InlineData(1.0, 400, 480)]
    [InlineData(1.25, 500, 600)]
    [InlineData(1.5, 600, 720)]
    [InlineData(2.0, 800, 960)]
    public void DeclaredFloors_ConvertAtEverySupportedScale(
        double scale, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(expectedWidth, WindowMinTrackSize.OneAxis(400, scale));
        Assert.Equal(expectedHeight, WindowMinTrackSize.OneAxis(480, scale));
    }

    [Theory]
    [InlineData(400, 1.5, 600)]
    [InlineData(367.1, 1.5, 551)]
    [InlineData(400.1, 2, 801)]
    [InlineData(1, 1.75, 2)]
    public void FractionalPixels_RoundUpSoTheReplyNeverLandsShort(
        double minimumDip, double scale, int expectedPixels)
    {
        Assert.Equal(expectedPixels, WindowMinTrackSize.OneAxis(minimumDip, scale));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-400)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void UnusableMinimum_YieldsTheSmallestLegalFloor(double minimumDip)
    {
        Assert.Equal(1, WindowMinTrackSize.OneAxis(minimumDip, 1.0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void UnusableScale_YieldsTheSmallestLegalFloor(double scale)
    {
        Assert.Equal(1, WindowMinTrackSize.OneAxis(400, scale));
    }

    [Fact]
    public void Axes_AreConvertedIndependently()
    {
        // The width scale and the height scale can disagree on a mixed DPI
        // setup, and one unusable axis must not poison the other.
        Assert.Equal(1, WindowMinTrackSize.OneAxis(400, 0));
        Assert.Equal(480, WindowMinTrackSize.OneAxis(480, 1.0));
        Assert.Equal(300, WindowMinTrackSize.OneAxis(200, 1.5));
        Assert.Equal(1, WindowMinTrackSize.OneAxis(200, double.NaN));
    }

    [Theory]
    [InlineData(double.MaxValue)]
    [InlineData(2147483545)]
    public void ConversionsBeyondTheFieldRange_SaturateNotWrap(double minimumDip)
    {
        Assert.Equal(int.MaxValue, WindowMinTrackSize.OneAxis(minimumDip, 2));
    }

    [Fact]
    public void NegativeFiniteMinimum_YieldsTheSmallestLegalFloorNotNegative()
    {
        Assert.Equal(1, WindowMinTrackSize.OneAxis(double.MinValue, 2));
    }
}
