using System;
using System.Linq;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class TabShrinkPanelTests
{
    [Fact]
    public void ComputeWidths_FewTabs_HaveFullTitles()
    {
        var widths = TabShrinkPanel.ComputeWidths(1000, new[] { false, false }, 0, showNames: true);

        Assert.Equal(new double[] { TabShrinkPanel.MaxTabWidth, TabShrinkPanel.MaxTabWidth }, widths);
    }

    [Fact]
    public void ComputeWidths_CrowdedStrip_SelectedTabKeepsTheRemainder()
    {
        // Two pinned tabs take 32 each, the selected card claims the rest and
        // the one remaining card splits what is left. All four fill the strip.
        var widths = TabShrinkPanel.ComputeWidths(228, new[] { true, true, false, false }, 3, showNames: true);

        Assert.Equal(120, widths[3]);
        Assert.Equal(32, widths[2]);
        Assert.True(widths.Sum() + 3 * TabShrinkPanel.TabGap <= 228 + 0.01);
    }

    [Fact]
    public void ComputeWidths_OpeningOneTab_ShrinksTheOthersGradually()
    {
        // Each added tab must shrink the strip a little, never halve it in one
        // step and never bottom out on the icon floor while it is still shrinking.
        double previous = -1;
        for (var count = 3; count <= 12; count++)
        {
            var widths = TabShrinkPanel.ComputeWidths(600, new bool[count], count - 1, showNames: true);
            var inactive = widths[0];
            if (previous >= 0)
            {
                Assert.True(inactive <= previous + 0.01, $"{count} tabs must not widen the strip");
                if (inactive > TabShrinkPanel.IconTabWidth)
                    Assert.True(previous - inactive < previous * 0.5,
                        $"{count} tabs must not step the width from {previous:F1} down to {inactive:F1}");
            }
            previous = inactive;
        }
    }

    [Fact]
    public void ComputeWidths_AlwaysFitsWhileEveryTabKeepsATitle()
    {
        for (var viewport = 200.0; viewport <= 1600; viewport += 37)
        {
            for (var count = 3; count <= 40; count++)
            {
                var pinned = Enumerable.Range(0, count).Select(i => i < 2).ToArray();
                var unpinned = count - pinned.Count(p => p);
                var available = viewport - pinned.Count(p => p) * TabShrinkPanel.IconTabWidth - (count - 1) * TabShrinkPanel.TabGap;
                if (available / unpinned < TabShrinkPanel.MinNamedTabWidth) continue;
                var widths = TabShrinkPanel.ComputeWidths(viewport, pinned, count - 1, showNames: true);
                Assert.True(widths.Sum() + (count - 1) * TabShrinkPanel.TabGap <= viewport + 0.01,
                    $"viewport {viewport} with {count} tabs must hold every title");
            }
        }
    }

    [Fact]
    public void ComputeWidths_TooManyFavicons_FallsBackToTheIconFloor()
    {
        var widths = TabShrinkPanel.ComputeWidths(200, Enumerable.Repeat(false, 10).ToArray(), -1, showNames: false);

        Assert.All(widths, w => Assert.Equal(TabShrinkPanel.IconTabWidth, w));
    }

    [Fact]
    public void ComputeWidths_WithoutAnActiveTab_KeepsOneWidthForEveryone()
    {
        var widths = TabShrinkPanel.ComputeWidths(200, new[] { false, false, false }, -1, showNames: true);

        Assert.All(widths, w => Assert.Equal(64, w));
    }

    [Fact]
    public void ComputeWidths_OnlyPinnedTabs_ReturnsIconWidths()
    {
        var widths = TabShrinkPanel.ComputeWidths(400, new[] { true, true, true }, 0, showNames: true);

        Assert.All(widths, w => Assert.Equal(TabShrinkPanel.IconTabWidth, w));
    }

    [Fact]
    public void ComputeWidths_NoTabs_ReturnsNothing()
    {
        Assert.Empty(TabShrinkPanel.ComputeWidths(400, Array.Empty<bool>(), 0, showNames: true));
    }
}
