using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class TabShrinkPanelTests
{
    [Fact]
    public void ComputeTabWidth_FewTabs_CapsAtMax()
    {
        Assert.Equal(190, TabShrinkPanel.ComputeTabWidth(1000, 2, 190));
    }

    [Fact]
    public void ComputeTabWidth_ManyTabs_ShrinksEvenly()
    {
        Assert.Equal(100, TabShrinkPanel.ComputeTabWidth(1000, 10, 190));
    }

    [Fact]
    public void ComputeTabWidth_CrowdedStrip_KeepsShrinkingPastIconWidth()
    {
        Assert.Equal(10, TabShrinkPanel.ComputeTabWidth(1000, 100, 190));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ComputeTabWidth_NoTabs_FallsBackToMax(int count)
    {
        Assert.Equal(190, TabShrinkPanel.ComputeTabWidth(1000, count, 190));
    }

    [Fact]
    public void ComputeTabWidth_NoSpace_FallsBackToMax()
    {
        Assert.Equal(190, TabShrinkPanel.ComputeTabWidth(0, 5, 190));
    }
}
