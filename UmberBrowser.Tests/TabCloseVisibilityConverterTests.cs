using System.Windows;
using UmberBrowser.Converters;
using Xunit;
namespace UmberBrowser.Tests;

public sealed class TabCloseVisibilityConverterTests
{
    private readonly TabCloseVisibilityConverter _converter = new();

    private object Convert(bool pinned, bool hovered, double width) =>
        _converter.Convert(new object[] { pinned, hovered, width },
            typeof(Visibility), null!, System.Globalization.CultureInfo.InvariantCulture);

    private object ConvertSwap(bool selected, bool hovered, bool pinned, string mode) =>
        _converter.Convert(new object[] { selected, hovered, pinned },
            typeof(Visibility), mode, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void NotHovered_KeepsTheClickSlot()
    {
        // The button stays present at zero opacity so the card end is clickable
        // before the cross appears.
        Assert.Equal(Visibility.Visible, Convert(false, false, 100));
    }

    [Fact]
    public void HoveredWideUnpinned_ShowsEndButton()
    {
        Assert.Equal(Visibility.Visible, Convert(false, true, 100));
    }

    [Fact]
    public void CrowdedTab_HidesEndButton()
    {
        Assert.Equal(Visibility.Collapsed, Convert(false, true, 30));
    }

    [Theory]
    [InlineData(false, false, 0.0)]
    [InlineData(false, true, 1.0)]
    [InlineData(true, false, 1.0)]
    public void Opacity_FollowsHoverAndActive(bool hovered, bool active, double expected)
    {
        var converter = new TabCloseOpacityConverter();
        var value = converter.Convert(new object[] { hovered, active, 100.0 },
            typeof(double), null!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, (double)value!);
    }

    [Fact]
    public void Opacity_NarrowCardNeverDrawsTheCross()
    {
        var converter = new TabCloseOpacityConverter();
        var value = converter.Convert(new object[] { true, true, 40.0 },
            typeof(double), null!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(0.0, (double)value!);
    }

    [Fact]
    public void PinnedTab_NeverShowsEndButton() =>
        Assert.Equal(Visibility.Collapsed, Convert(true, true, 100));

    [Fact]
    public void Swap_ActiveHovered_Shows() =>
        Assert.Equal(Visibility.Visible, ConvertSwap(true, true, false, "Swap"));

    [Theory]
    [InlineData(true, true, Visibility.Visible)]
    [InlineData(true, false, Visibility.Visible)]
    [InlineData(false, true, Visibility.Collapsed)]
    [InlineData(false, false, Visibility.Collapsed)]
    public void NarrowOverlay_OnlyShowsForTheSelectedTab(bool selected, bool hovered, Visibility expected)
    {
        var converter = new TabCloseVisibilityConverter();
        var value = converter.Convert(new object[] { false, hovered, 40.0, selected },
            typeof(Visibility), "Narrow", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Swap_IdleTab_Hides() =>
        Assert.Equal(Visibility.Collapsed, ConvertSwap(false, false, false, "Swap"));

    [Fact]
    public void Swap_InactiveHovered_Hides() =>
        Assert.Equal(Visibility.Collapsed, ConvertSwap(false, true, false, "Swap"));

    [Fact]
    public void Swap_ActiveNotHovered_Hides() =>
        Assert.Equal(Visibility.Collapsed, ConvertSwap(true, false, false, "Swap"));

    [Fact]
    public void Swap_PinnedActiveHovered_Hides() =>
        Assert.Equal(Visibility.Collapsed, ConvertSwap(true, true, true, "Swap"));

    [Fact]
    public void SwapInverse_HidesIconWhenSwapShows() =>
        Assert.Equal(Visibility.Collapsed, ConvertSwap(true, true, false, "SwapInverse"));

    [Fact]
    public void SwapInverse_ShowsIconWhenSwapHides() =>
        Assert.Equal(Visibility.Visible, ConvertSwap(false, false, false, "SwapInverse"));
}
