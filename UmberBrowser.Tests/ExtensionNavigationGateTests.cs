using UmberBrowser.Engine;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Which URLs the registration gate waits for.
///
/// The gate exists so the first external request on a control leaves after uBlock is
/// registered. Anything the browser serves itself is not external, and holding an
/// internal page, a background asset host or the popup handoff sentinel behind an
/// extension registration would be a regression in its own right.
/// </summary>
public sealed class ExtensionNavigationGateTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ")]
    [InlineData("http://example.com/")]
    [InlineData("https://example.com")]
    public void RealPagesAreGated(string url) =>
        Assert.True(TabEngine.IsExternalRequestUrl(url));

    [Theory]
    [InlineData("internal://newtab")]
    [InlineData("internal://settings")]
    [InlineData("internal://onetab")]
    [InlineData("internal://history")]
    [InlineData("internal://downloads")]
    [InlineData("internal://onboarding")]
    [InlineData("internal://releasenotes")]
    [InlineData("internal://focus")]
    [InlineData("internal://health")]
    [InlineData("internal://pending-native")]
    public void InternalPagesAreNotGated(string url) =>
        Assert.False(TabEngine.IsExternalRequestUrl(url));

    [Fact]
    public void ThePopupSentinelIsNotGated() =>
        // The requesting renderer adopts this core, so nothing may be in flight and
        // nothing may be waited for before the handoff.
        Assert.False(TabEngine.IsExternalRequestUrl("internal://pending-native"));

    [Theory]
    [InlineData("about:blank")]
    [InlineData("data:text/html,<p>hi</p>")]
    [InlineData("")]
    [InlineData(null)]
    public void DocumentsTheBrowserServesItselfAreNotGated(string? url) =>
        Assert.False(TabEngine.IsExternalRequestUrl(url));

    [Theory]
    [InlineData("https://local.assets/pdfjs/web/viewer.html?file=x")]
    [InlineData("https://user.assets/photo.jpg")]
    public void HostMappedAssetFoldersAreNotGated(string url) =>
        Assert.False(TabEngine.IsExternalRequestUrl(url));

    [Fact]
    public void TheAssetHostsAreTheOnlyNonInternalWebUrlsExcluded()
    {
        // Anything else under those hosts still leaves the machine.
        Assert.False(TabEngine.IsExternalRequestUrl("https://local.assets/"));
        Assert.True(TabEngine.IsExternalRequestUrl("https://local.assets.example.com/x.png"));
    }
}
