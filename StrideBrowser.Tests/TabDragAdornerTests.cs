using System.Collections.Generic;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class TabDragAdornerTests
{
    [Fact]
    public void ComputeShiftedIndex_EmptyStrip_ReturnsZero()
    {
        Assert.Equal(0, TabDragAdorner.ComputeShiftedIndex(new List<double>(), 0, 100, 100, 14));
    }

    [Theory]
    // Dragging the middle tab right: the card's right edge must pass a neighbor center.
    [InlineData(42.0, 42.0, 1)] // at rest, holds
    [InlineData(44.0, 42.0, 1)] // slight nudge, card has not reached C yet
    [InlineData(60.0, 42.0, 2)] // card body covers C, C yields
    [InlineData(200.0, 42.0, 2)] // far right, lands last
    public void ComputeShiftedIndex_DragRight_FollowsCardEdge(double x, double lastX, int expected)
    {
        var centers = new List<double> { 14, 42, 70 };

        Assert.Equal(expected, TabDragAdorner.ComputeShiftedIndex(centers, 1, x, lastX, 14));
    }

    [Theory]
    // Dragging the last tab left: the card's left edge must pass a neighbor center.
    [InlineData(70.0, 70.0, 2)] // at rest, holds
    [InlineData(60.0, 70.0, 2)] // slight nudge, B still holds most of its slot
    [InlineData(50.0, 70.0, 1)] // card body covers B, B yields
    [InlineData(-10.0, 70.0, 0)] // far left, lands first
    public void ComputeShiftedIndex_DragLeft_FollowsCardEdge(double x, double lastX, int expected)
    {
        var centers = new List<double> { 14, 42, 70 };

        Assert.Equal(expected, TabDragAdorner.ComputeShiftedIndex(centers, 2, x, lastX, 14));
    }
}
