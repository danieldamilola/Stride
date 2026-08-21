using System.Windows;
using StrideBrowser.Models.LinkPreview;
using StrideBrowser.Services.LinkPreview;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class LinkPreviewPolicyTests
{
    private readonly LinkPreviewPolicy _policy = new();

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?q=1#hash")]
    [InlineData("https://sub.domain.example.co.uk/article")]
    public void IsPreviewableUrl_AllowsHttpAndHttps(string url)
    {
        Assert.True(_policy.IsPreviewableUrl(url, "https://current.com"));
    }

    [Theory]
    [InlineData("internal://newtab")]
    [InlineData("internal://settings")]
    [InlineData("data:text/html,hello")]
    [InlineData("javascript:alert(1)")]
    [InlineData("blob:https://example.com/abc")]
    [InlineData("mailto:user@example.com")]
    [InlineData("tel:+123456")]
    [InlineData("file:///C:/test.html")]
    [InlineData("about:blank")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsPreviewableUrl_RejectsNonPreviewable(string url)
    {
        Assert.False(_policy.IsPreviewableUrl(url, "https://current.com"));
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("ws://example.com")]
    public void IsPreviewableUrl_RejectsNonHttpSchemes(string url)
    {
        Assert.False(_policy.IsPreviewableUrl(url, "https://current.com"));
    }

    [Fact]
    public void IsPreviewableUrl_RejectsSameUrl()
    {
        var url = "https://example.com/page";
        Assert.False(_policy.IsPreviewableUrl(url, url));
        Assert.False(_policy.IsPreviewableUrl(url, url.ToUpperInvariant()));
    }

    [Fact]
    public void IsPreviewableUrl_RejectsSameDocumentFragmentOnly()
    {
        var current = "https://example.com/page?a=1";
        var withFragment = "https://example.com/page?a=1#section";
        Assert.False(_policy.IsPreviewableUrl(withFragment, current));
    }

    [Fact]
    public void IsPreviewableUrl_RejectsSameDocumentWhenCurrentHasFragmentAndTargetDoesNot()
    {
        var current = "https://example.com/page#section";
        var target = "https://example.com/page";
        Assert.False(_policy.IsPreviewableUrl(target, current));
    }

    [Fact]
    public void IsPreviewableUrl_AllowsDifferentPathWithFragment()
    {
        var current = "https://example.com/page?a=1";
        var different = "https://example.com/other#section";
        Assert.True(_policy.IsPreviewableUrl(different, current));
    }

    [Fact]
    public void ShouldPeek_RequiresEnabled()
    {
        var opts = new LinkPreviewOptions(false, "Alt", true);
        var req = new LinkPreviewRequest(Guid.NewGuid(), "https://example.com", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, DateTime.UtcNow);
        Assert.False(_policy.ShouldPeek(req, opts));
    }

    [Fact]
    public void ShouldPeek_RequiresAltHotkey()
    {
        var opts = new LinkPreviewOptions(true, "Ctrl", true);
        var req = new LinkPreviewRequest(Guid.NewGuid(), "https://example.com", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, DateTime.UtcNow);
        Assert.False(_policy.ShouldPeek(req, opts));
    }

    [Fact]
    public void ShouldPeek_RespectsAllowPress()
    {
        var pressEnabled = new LinkPreviewOptions(true, "Alt", true);
        var pressDisabled = new LinkPreviewOptions(true, "Alt", false);
        var req = new LinkPreviewRequest(Guid.NewGuid(), "https://example.com", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, DateTime.UtcNow);

        Assert.True(_policy.ShouldPeek(req, pressEnabled));
        Assert.False(_policy.ShouldPeek(req, pressDisabled));
    }

    [Fact]
    public void ShouldPeek_RejectsEmptyRect()
    {
        var opts = LinkPreviewOptions.Default;
        var req = new LinkPreviewRequest(Guid.NewGuid(), "https://example.com", Rect.Empty, LinkPreviewTrigger.AltPress, DateTime.UtcNow);
        Assert.False(_policy.ShouldPeek(req, opts));
    }

    [Fact]
    public void ShouldPeek_RejectsEmptyTabId()
    {
        var opts = LinkPreviewOptions.Default;
        var req = new LinkPreviewRequest(Guid.Empty, "https://example.com", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, DateTime.UtcNow);
        Assert.False(_policy.ShouldPeek(req, opts));
    }

    [Fact]
    public void ShouldPeek_RejectsNonPreviewableUrl()
    {
        var opts = LinkPreviewOptions.Default;
        var req = new LinkPreviewRequest(Guid.NewGuid(), "javascript:alert(1)", new Rect(10, 10, 100, 20), LinkPreviewTrigger.AltPress, DateTime.UtcNow);
        Assert.False(_policy.ShouldPeek(req, opts));
    }
}
