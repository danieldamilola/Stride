using System.Windows;
using StrideBrowser.Converters;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ToolbarInteractionTests
{
    [Fact]
    public void HibernateMenu_ActiveTabTargetsOnlyAwakeBackgroundTabs()
    {
        var active = new BrowserTab { IsActive = true };
        var background = new BrowserTab();
        var pinned = new BrowserTab { IsPinned = true };
        var hibernated = new BrowserTab { IsHibernated = true };
        Assert.Equal(new[] { background, pinned }, TabHibernationManager.GetManualHibernateTargets(
            new[] { active, background, pinned, hibernated }, active));
    }

    [Fact]
    public void HibernateMenu_BackgroundTabTargetsOnlyItself()
    {
        var active = new BrowserTab { IsActive = true };
        var target = new BrowserTab();
        Assert.Equal(new[] { target }, TabHibernationManager.GetManualHibernateTargets(
            new[] { active, target, new BrowserTab() }, target));
    }

    [Fact]
    public void HibernateMenu_HasNoTargetsForClosedOrHibernatedTabsOrAnOnlyActiveTab()
    {
        var active = new BrowserTab { IsActive = true };
        var hibernated = new BrowserTab { IsHibernated = true };
        Assert.Empty(TabHibernationManager.GetManualHibernateTargets(new[] { active }, active));
        Assert.Empty(TabHibernationManager.GetManualHibernateTargets(new[] { active, hibernated }, active));
        Assert.Empty(TabHibernationManager.GetManualHibernateTargets(new[] { active, hibernated }, hibernated));
        Assert.Empty(TabHibernationManager.GetManualHibernateTargets(new[] { active }, new BrowserTab()));
    }

    [Fact]
    public void CompactTabs_OnlyActiveTabExpands()
    {
        Assert.Equal(new double[] { 32, 32, 160, 32 }, TabShrinkPanel.ComputeWidths(800, new[] { true, false, false, false }, 2, false));
    }

    [Fact]
    public void FullNames_UseViewportAndPreservePinnedWidths()
    {
        Assert.Equal(new double[] { 32, 160, 144 }, TabShrinkPanel.ComputeWidths(344, new[] { true, false, false }, 1, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrowdedTabs_ShrinkToTheIconFloorBeforeScrolling(bool fullNames)
    {
        var widths = TabShrinkPanel.ComputeWidths(80, new[] { true, false, false, false }, 2, fullNames);

        Assert.Equal(new[] { 32, TabShrinkPanel.IconTabWidth, TabShrinkPanel.IconTabWidth, TabShrinkPanel.IconTabWidth }, widths);
    }

    [Fact]
    public void AllPinnedTabs_StayUsableEvenWhenTheyOverflow()
    {
        Assert.Equal(new double[] { 32, 32, 32 }, TabShrinkPanel.ComputeWidths(40, new[] { true, true, true }, 1, true));
    }

    [Fact]
    public void EmptyViewport_KeepsTabsOnTheIconFloor()
    {
        Assert.Equal(new[] { TabShrinkPanel.IconTabWidth, TabShrinkPanel.IconTabWidth },
            TabShrinkPanel.ComputeWidths(0, new[] { false, false }, 1, true));
    }

    [Theory]
    [InlineData(100, 80, 800, 400, 100, 424)]
    [InlineData(400, 200, 800, 20, 32, 16)]
    [InlineData(80, 200, 800, 120, 32, 80)]
    [InlineData(0, 100, 80, 0, 80, 0)]
    public void SelectionScroll_OnlyMovesEnoughToRevealTheTab(double current, double viewport, double extent, double left, double width, double expected)
    {
        Assert.Equal(expected, TabStripController.ComputeVisibleOffset(current, viewport, extent, new Rect(left, 0, width, 31)));
    }

    [Theory]
    [InlineData(0, -12)]
    [InlineData(16, -6)]
    [InlineData(100, 0)]
    [InlineData(184, 6)]
    [InlineData(200, 12)]
    [InlineData(-1, 0)]
    [InlineData(201, 0)]
    public void DragScroll_SpeedsUpNearTheEdgeAndStopsOutside(double x, double expected) =>
        Assert.Equal(expected, TabStripDragDropBehavior.ComputeEdgeScroll(x, 200));

    [Fact]
    public void DragTarget_MixedWidthsKeepsTheTabAtRest()
    {
        var slots = new[] { new Rect(0, 0, 32, 31), new Rect(32, 0, 190, 31), new Rect(222, 0, 32, 31) };
        Assert.Equal(1, TabStripDragDropBehavior.ComputeTargetIndex(slots, 1, 32, 0, 2));
        Assert.Equal(1, TabStripDragDropBehavior.ComputeTargetIndex(slots, 1, 40, 0, 2));
        Assert.Equal(2, TabStripDragDropBehavior.ComputeTargetIndex(slots, 1, 60, 0, 2));
        Assert.Equal(0, TabStripDragDropBehavior.ComputeTargetIndex(slots, 1, 10, 0, 2));
    }

    [Fact]
    public void DragTarget_RespectsPinnedBoundary()
    {
        var slots = new[] { new Rect(0, 0, 32, 31), new Rect(32, 0, 32, 31), new Rect(64, 0, 190, 31) };
        Assert.Equal(1, TabStripDragDropBehavior.ComputeTargetIndex(slots, 0, 300, 0, 1));
        Assert.Equal(2, TabStripDragDropBehavior.ComputeTargetIndex(slots, 2, -100, 2, 2));
    }

    [Fact]
    public void AudioIndicator_RemainsVisibleWhenMuted()
    {
        var tab = new StrideBrowser.Models.BrowserTab { IsAudioPlaying = true };
        Assert.True(tab.HasAudioIndicator);
        tab.IsMuted = true;
        tab.IsAudioPlaying = false;
        Assert.True(tab.HasAudioIndicator);
        tab.IsMuted = false;
        Assert.False(tab.HasAudioIndicator);
    }

    [Fact]
    public void CompactTabs_SelectedTabKeepsItsShareWhenTheStripCanHoldIt()
    {
        var widths = TabShrinkPanel.ComputeWidths(400, new bool[10], 4, false);

        Assert.Equal(76, widths[4]);
        Assert.All(widths.Where((_, index) => index != 4), width => Assert.Equal(32, width));
        Assert.True(widths.Sum() + 9 * TabShrinkPanel.TabGap <= 400 + 0.01);
    }

    [Fact]
    public void CompactTabs_SelectedTabSharesTheCompactFloorOnceTheStripIsFull()
    {
        var widths = TabShrinkPanel.ComputeWidths(300, new bool[30], 20, false);

        Assert.All(widths, width => Assert.Equal(TabShrinkPanel.IconTabWidth, width));
    }

    [Fact]
    public void ActiveTab_CloseButtonRemainsVisibleWithoutHover()
    {
        var converter = new TabCloseVisibilityConverter();
        Assert.Equal(Visibility.Visible, converter.Convert(new object[] { false, false, 120.0, true },
            typeof(Visibility), "", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(false, true, 32, true, Visibility.Visible)]
    [InlineData(true, true, 32, true, Visibility.Collapsed)]
    [InlineData(false, false, 32, false, Visibility.Collapsed)]
    [InlineData(false, true, 32, false, Visibility.Collapsed)]
    [InlineData(false, true, 100, true, Visibility.Collapsed)]
    public void CloseOverlay_OnlyReplacesTheIconOnTheSelectedNarrowTab(bool pinned, bool hovered, double width, bool selected, Visibility expected)
    {
        var converter = new TabCloseVisibilityConverter();
        Assert.Equal(expected, converter.Convert(new object[] { pinned, hovered, width, selected },
            typeof(Visibility), "Narrow", System.Globalization.CultureInfo.InvariantCulture));
    }
}
