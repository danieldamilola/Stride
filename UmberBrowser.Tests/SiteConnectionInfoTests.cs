using System.Windows;
using UmberBrowser.Services.UI;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class SiteConnectionInfoTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a URL")]
    [InlineData("internal://settings")]
    [InlineData("about:blank")]
    [InlineData("data:text/html,hello")]
    [InlineData("file:///C:/page.html")]
    [InlineData("javascript:alert(1)")]
    public void NonWebAddresses_HaveNoSiteInformation(string? url)
    {
        Assert.Null(SiteConnectionInfo.FromUrl(url));
    }

    [Theory]
    [InlineData("https://example.com", false)]
    [InlineData("HTTPS://example.com/path", false)]
    [InlineData("http://example.com", true)]
    [InlineData("http://localhost:8080", true)]
    public void HttpAddresses_ShowAnUnencryptedConnectionWarning(string url, bool insecure)
    {
        var info = Assert.IsType<SiteConnectionInfo>(SiteConnectionInfo.FromUrl(url));
        Assert.Equal(insecure, info.IsInsecure);
        Assert.Equal(insecure ? "Connection is not encrypted" : "HTTPS connection", info.Summary);
    }

    [Theory]
    [InlineData("https://user:secret@example.com/path?token=private#fragment", "example.com")]
    [InlineData("https://www.example.com:8443/path", "www.example.com:8443")]
    [InlineData("https://example.com:443/path", "example.com")]
    [InlineData("https://b\u00fccher.example/", "xn--bcher-kva.example")]
    [InlineData("http://[::1]:8080/", "[::1]:8080")]
    public void SiteIdentity_KeepsTheHostAndPortWithoutCredentialsOrPageData(string url, string host)
    {
        var info = Assert.IsType<SiteConnectionInfo>(SiteConnectionInfo.FromUrl(url));
        Assert.Equal(host, info.Host);
    }
    [Theory]
    [InlineData(24, 1100, 24)]
    [InlineData(700, 1100, 700)]
    [InlineData(900, 1100, 812)]
    [InlineData(370, 400, 112)]
    [InlineData(0, 400, 8)]
    public void Popup_OpensRightwardAndStaysInsideTheWindow(double targetLeft, double windowWidth, double expectedLeft)
    {
        var placements = SiteInfoController.GetPopupPlacements(new Size(280, 180), new Size(20, 24), targetLeft, windowWidth);
        Assert.Equal(expectedLeft, targetLeft + placements[0].Point.X);
        Assert.Equal(30, placements[0].Point.Y);
        Assert.Equal(-186, placements[1].Point.Y);
        Assert.Equal(placements[0].Point.X, placements[1].Point.X);
    }
}
