using System.Windows;
using StrideBrowser.Converters;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class TabCloseVisibilityConverterTests
{
    private readonly TabCloseVisibilityConverter _converter = new();

    private object Convert(bool selected, bool fullNames, double width, bool hovered, bool pinned) =>
        _converter.Convert(new object[] { selected, fullNames, width, hovered, pinned },
            typeof(Visibility), null!, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void CompactInactive_HidesEndButton() =>
        Assert.Equal(Visibility.Collapsed, Convert(false, false, 100, true, false));

    [Fact]
    public void NotHovered_HidesEndButton() =>
        Assert.Equal(Visibility.Collapsed, Convert(true, true, 100, false, false));

    [Fact]
    public void ActiveWideTabHovered_ShowsEndButton() =>
        Assert.Equal(Visibility.Visible, Convert(true, false, 100, true, false));

    [Fact]
    public void FullNamesWideInactiveHovered_ShowsEndButton() =>
        Assert.Equal(Visibility.Visible, Convert(false, true, 100, true, false));

    [Fact]
    public void CrowdedTab_YieldsToOverlay() =>
        Assert.Equal(Visibility.Collapsed, Convert(false, true, 30, true, false));

    [Fact]
    public void CrowdedActiveTab_YieldsToOverlay() =>
        Assert.Equal(Visibility.Collapsed, Convert(true, true, 30, true, false));

    [Fact]
    public void PinnedTab_NeverShowsEndButton() =>
        Assert.Equal(Visibility.Collapsed, Convert(true, true, 100, true, true));
}
