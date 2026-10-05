using StrideBrowser.Helpers;
using StrideBrowser.Services.LinkPreview;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class NetworkTargetGuardTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("app.localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("[::1]")]
    [InlineData("::1")]
    [InlineData("0.0.0.0")]
    [InlineData("[::]")]
    [InlineData("169.254.169.254")]
    [InlineData("169.254.1.1")]
    [InlineData("router.local")]
    [InlineData("db.internal")]
    [InlineData("224.0.0.1")]
    [InlineData("ff02::1")]
    public void IsForbiddenHost_LocalAndMetadataTargets_AreBlocked(string host)
    {
        Assert.True(NetworkTargetGuard.IsForbiddenHost(host));
    }

[Theory]
    [InlineData("example.com")]
    [InlineData("www.wallhaven.cc")]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.32.0.1")]
    [InlineData("172.15.0.1")]
    [InlineData("100.63.0.1")]
    [InlineData("100.128.0.1")]
    public void IsForbiddenHost_PublicTargets_AreAllowed(string host)
    {
        Assert.False(NetworkTargetGuard.IsForbiddenHost(host));
    }

    /// <summary>
    /// A missing host is treated as forbidden so a caller that forgets to check for emptiness
    /// fails closed instead of allowing the request.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void IsForbiddenHost_MissingHost_IsBlocked(string? host)
    {
        Assert.True(NetworkTargetGuard.IsForbiddenHost(host));
    }

    /// <summary>
    /// Reader mode pulls img src out of remote articles, so it also refuses RFC1918. Nothing in
    /// an article legitimately points at 10/8, 172.16/12 or 192.168/16 on the reader's network.
    /// </summary>
    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.1.254")]
    [InlineData("[fc00::1]")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    public void IsPrivateHost_PrivateAndLocalTargets_AreBlocked(string host)
    {
        Assert.True(NetworkTargetGuard.IsPrivateHost(host));
    }

    [Theory]
    [InlineData("https://example.com/a.png")]
    [InlineData("http://8.8.8.8/a.png")]
    public void IsPrivateHost_PublicTargets_AreAllowed(string host)
    {
        Assert.False(NetworkTargetGuard.IsPrivateHost(host));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/admin?delete=1")]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://169.254.169.254/latest/meta-data/iam/security-credentials/")]
    [InlineData("http://192.168.1.1/admin")]
    [InlineData("http://[::1]/x")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsPageSuppliedUrl_NonPublicOrNonHttp_IsRejected(string? url)
    {
        Assert.True(NetworkTargetGuard.RejectsPageSuppliedUrl(url));
    }

    [Fact]
    public void RejectsPageSuppliedUrl_PublicHttps_IsAllowed()
    {
        Assert.False(NetworkTargetGuard.RejectsPageSuppliedUrl("https://example.com/img.png"));
    }

    /// <summary>
    /// Link preview is opened by an Alt+click, so previewing a LAN device is a real use case
    /// that should keep working. Only loopback and link-local are refused there.
    /// </summary>
[Theory]
    [InlineData("http://192.168.1.1/admin")]
    [InlineData("http://10.0.0.5/")]
    public void IsForbiddenHost_Rfc1918_StaysAllowedForUserInitiatedPreview(string url)
    {
        var policy = new LinkPreviewPolicy();
        Assert.True(policy.IsPreviewableUrl(url, "https://example.com/page"));
        Assert.True(NetworkTargetGuard.IsSafePreviewTarget(url));
    }

    /// <summary>
    /// The window re-checks immediately before it navigates, so the decision holds even if a
    /// caller reaches the preview without going through the policy.
    /// </summary>
    [Theory]
    [InlineData("http://127.0.0.1:9222/")]
    [InlineData("http://localhost/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("about:blank")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSafePreviewTarget_LoopbackNonHttpOrMissing_IsRejected(string? url)
    {
        Assert.False(NetworkTargetGuard.IsSafePreviewTarget(url));
    }

    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://example.com/page")]
    [InlineData("http://192.168.1.1/admin")]
    public void IsSafePreviewTarget_PublicAndLanTargets_AreAllowed(string url)
    {
        Assert.True(NetworkTargetGuard.IsSafePreviewTarget(url));
    }

    [Theory]
    [InlineData("http://127.0.0.1:9222/")]
    [InlineData("http://localhost/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public void IsPreviewableUrl_LoopbackAndLinkLocal_AreRefused(string url)
    {
        var policy = new LinkPreviewPolicy();
        Assert.False(policy.IsPreviewableUrl(url, "https://example.com/page"));
    }
}

