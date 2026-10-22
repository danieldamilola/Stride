using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ExtensionActionPopupTests
{
    [Fact]
    public void UBlockTargetsTheWebsiteInsteadOfItsOwnPopupTab()
    {
        const string tabs = """[{"id":1,"url":"chrome-extension://test/popup.html","lastAccessed":99},{"id":2,"url":"https://example.com/","title":"Page","lastAccessed":10},{"id":3,"url":"https://example.com/","title":"Page","lastAccessed":20}]""";
        Assert.Equal(3, ExtensionActionPopup.FindTargetTabId(tabs, "https://example.com/", "Page"));
        Assert.Null(ExtensionActionPopup.FindTargetTabId(tabs, "https://other.test/", "Page"));
        Assert.Null(ExtensionActionPopup.FindTargetTabId("null", "https://example.com/", "Page"));
    }
    [Theory]
    [InlineData(252, 340, 1100, 700, 252, 340)]
    [InlineData(900, 900, 1100, 700, 800, 600)]
    [InlineData(900, 900, 400, 250, 380, 238)]
    [InlineData(-10, -10, 1100, 700, 80, 60)]
    [InlineData(double.NaN, double.PositiveInfinity, 1100, 700, 320, 400)]
    public void PopupFitsContentWithinBrowserBounds(double width, double height, double ownerWidth, double availableHeight, double expectedWidth, double expectedHeight)
    {
        var size = ExtensionActionPopup.ConstrainSize(width, height, ownerWidth, availableHeight);
        Assert.Equal(expectedWidth, size.Width); Assert.Equal(expectedHeight, size.Height);
    }
}
