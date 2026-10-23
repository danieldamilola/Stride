using System.Windows;
using UmberBrowser.Services.UI;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Locks in the decisions the shipped drag makes, so a later change to the strip cannot
/// quietly move a pinned tab past the unpinned ones, scroll from the middle of the strip,
/// or tear a tab off while the pointer is still over its own strip.
/// </summary>
public sealed class TabStripDragDecisionTests
{
    private static Rect[] Slots(int count, double width = 100, double left = 0) =>
        Enumerable.Range(0, count)
            .Select(i => new Rect(left + i * width, 0, width, 40))
            .ToArray();

    [Fact]
    public void PinnedTabCannotMoveRightOfThePinnedGroup()
    {
        var slots = Slots(4);
        // One pinned tab at index 0, so the pinned group is 0..0.
        var target = TabStripDragDropBehavior.ComputeTargetIndex(slots, 0, 350, min: 0, max: 0);
        Assert.Equal(0, target);
    }

    [Fact]
    public void PinnedTabCannotMoveLeftOfTheStart()
    {
        var slots = Slots(4);
        Assert.Equal(0, TabStripDragDropBehavior.ComputeTargetIndex(slots, 0, -80, min: 0, max: 0));
    }

    [Fact]
    public void UnpinnedTabCannotMoveBeforeThePinnedGroup()
    {
        var slots = Slots(4);
        // Tab 1 is unpinned and the pinned group ends at index 0, so min is 1.
        Assert.Equal(1, TabStripDragDropBehavior.ComputeTargetIndex(slots, 1, -400, min: 1, max: 3));
    }

    [Fact]
    public void UnpinnedTabCannotMovePastTheLastSlot()
    {
        var slots = Slots(4);
        Assert.Equal(3, TabStripDragDropBehavior.ComputeTargetIndex(slots, 1, 900, min: 1, max: 3));
    }

    [Fact]
    public void UnpinnedTabFollowsThePointerWithinItsRange()
    {
        var slots = Slots(4);
        // Left edge past slot 2's midpoint pushes the drop index to 3.
        Assert.Equal(3, TabStripDragDropBehavior.ComputeTargetIndex(slots, 1, 260, min: 1, max: 3));
        Assert.Equal(1, TabStripDragDropBehavior.ComputeTargetIndex(slots, 1, 120, min: 1, max: 3));
    }

    [Fact]
    public void EmptyStripHasNoTarget()
    {
        Assert.Equal(0, TabStripDragDropBehavior.ComputeTargetIndex(Array.Empty<Rect>(), 0, 100, 0, 0));
    }

    [Theory]
    [InlineData(0, -12)]
    [InlineData(16, -6)]
    [InlineData(32, 0)]
    [InlineData(400, 0)]
    [InlineData(768, 0)]
    [InlineData(784, 6)]
    [InlineData(800, 12)]
    public void EdgeScrollOnlyRunsInTheOuterMargin(double x, double expected)
    {
        Assert.Equal(expected, TabStripDragDropBehavior.ComputeEdgeScroll(x, 800), 3);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(801)]
    [InlineData(1000)]
    public void PointerOutsideTheStripDoesNotScroll(double x) =>
        Assert.Equal(0, TabStripDragDropBehavior.ComputeEdgeScroll(x, 800));

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void StripWithNoWidthDoesNotScroll(double viewport) =>
        Assert.Equal(0, TabStripDragDropBehavior.ComputeEdgeScroll(5, viewport));
}