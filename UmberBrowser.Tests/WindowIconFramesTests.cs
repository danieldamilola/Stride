using UmberBrowser.Helpers;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Guards frame selection for multi-size window icons. BitmapImage decodes
/// frame 0 of an .ico, which the generator writes smallest, so the window must
/// pick the frame nearest the rendered size or the taskbar upscales a 16px
/// frame and the mark reads small.
/// </summary>
public sealed class WindowIconFramesTests
{
    [Fact]
    public void SelectIndex_WithExactMatch_PicksExactFrame()
    {
        var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

        var index = WindowIconFrames.SelectIndex(sizes, 32);

        Assert.Equal(3, index);
    }

    [Fact]
    public void SelectIndex_WhenNoExactMatch_PicksNearest()
    {
        var sizes = new[] { 16, 32, 48, 64, 128, 256 };

        var index = WindowIconFrames.SelectIndex(sizes, 40);

        Assert.Equal(2, index);
    }

    [Fact]
    public void SelectIndex_OnEqualDistance_TieGoesToLargerFrame()
    {
        // Distance 10 to both 32 and 52. Half the tie should land on 52 so the
        // taskbar never receives an upscaled smaller bitmap.
        var sizes = new[] { 32, 52 };

        var index = WindowIconFrames.SelectIndex(sizes, 42);

        Assert.Equal(1, index);
    }

    [Fact]
    public void SelectIndex_TargetAboveAllFrames_PicksLargest()
    {
        var sizes = new[] { 16, 32, 64 };

        var index = WindowIconFrames.SelectIndex(sizes, 96);

        Assert.Equal(2, index);
    }

    [Fact]
    public void SelectIndex_TargetBelowAllFrames_PicksSmallest()
    {
        var sizes = new[] { 16, 32, 64 };

        var index = WindowIconFrames.SelectIndex(sizes, 4);

        Assert.Equal(0, index);
    }

    [Fact]
    public void SelectIndex_SingleFrameAlwaysWins()
    {
        var sizes = new[] { 256 };

        var index = WindowIconFrames.SelectIndex(sizes, 32);

        Assert.Equal(0, index);
    }

    [Fact]
    public void SelectIndex_EmptyFrameList_Throws()
    {
        var sizes = Array.Empty<int>();

        Assert.Throws<ArgumentException>(() => WindowIconFrames.SelectIndex(sizes, 32));
    }

    [Theory]
    [InlineData(1.0, 32)]
    [InlineData(1.25, 40)]
    [InlineData(1.5, 48)]
    [InlineData(2.0, 64)]
    public void TargetSize_ScalesLogicalSizeByDpi(double dpiScale, int expected)
    {
        Assert.Equal(expected, WindowIconFrames.TargetSize(dpiScale));
    }

    [Fact]
    public void TargetSize_NonIntegralScale_Rounds()
    {
        Assert.Equal(44, WindowIconFrames.TargetSize(1.375));
    }

    [Fact]
    public void TargetSize_ZeroDpi_NeverBelowOnePixel()
    {
        Assert.Equal(1, WindowIconFrames.TargetSize(0.0));
    }
}
