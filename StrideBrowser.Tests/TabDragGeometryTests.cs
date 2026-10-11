using System.Windows;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class TabDragGeometryTests
{
    [Theory]
    [InlineData(150, 20, false)]
    [InlineData(150, 71, false)]
    [InlineData(150, 73, true)]
    [InlineData(-33, 20, true)]
    [InlineData(333, 20, true)]
    [InlineData(150, -33, true)]
    public void DetachRequiresLeavingStripMargin(double x, double y, bool expected)
    {
        Assert.Equal(expected, TabDragGeometry.ShouldDetach(new Rect(0, 0, 300, 40), new Point(x, y), false, false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CancelOrAcceptedDropPreventsDetach(bool cancelled, bool dropped)
    {
        Assert.False(TabDragGeometry.ShouldDetach(new Rect(0, 0, 300, 40), new Point(700, 700), cancelled, dropped));
    }

    [Fact]
    public void EmptyStripBoundsCannotDetach() =>
        Assert.False(TabDragGeometry.ShouldDetach(Rect.Empty, new Point(700, 700), false, false));

    [Theory]
    [InlineData(-20, 0)]
    [InlineData(49, 0)]
    [InlineData(50, 1)]
    [InlineData(149, 1)]
    [InlineData(250, 3)]
    [InlineData(700, 3)]
    public void DockPositionUsesTabMidpoints(double x, int expected) =>
        Assert.Equal(expected, TabDragGeometry.InsertionIndex(new double[] { 50, 150, 250 }, x));

    [Fact]
    public void EmptyDestinationInsertsAtStart() =>
        Assert.Equal(0, TabDragGeometry.InsertionIndex(Array.Empty<double>(), 100));
}
