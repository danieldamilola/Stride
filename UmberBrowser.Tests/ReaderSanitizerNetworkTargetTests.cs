using UmberBrowser.Services.Reader;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Reader mode takes img src values out of an arbitrary page and renders them with the user's
/// cookie jar. A page can therefore aim the renderer at the reader's own network.
/// </summary>
public sealed class ReaderSanitizerNetworkTargetTests
{
    private readonly IReaderSanitizer _sanitizer = new ReaderSanitizer();
    private const string Base = "https://example.com/article";

    [Theory]
    [InlineData("http://127.0.0.1:8080/admin?delete=1")]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://169.254.169.254/latest/meta-data/iam/security-credentials/")]
    [InlineData("http://[::1]/x")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://192.168.1.1/admin")]
    [InlineData("http://172.16.5.4/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://[::ffff:169.254.169.254]/")]
    [InlineData("http://[::ffff:10.0.0.1]/")]
    public void Sanitize_DropsImageSourcesPointingAtLocalOrPrivateHosts(string url)
    {
        var result = _sanitizer.Sanitize($"<p>t</p><img src=\"{url}\">", Base);

        Assert.DoesNotContain("src=", result);
        Assert.DoesNotContain(url, result);
        Assert.Contains("<p>t</p>", result);
    }

    [Theory]
    [InlineData("https://example.com/a.png")]
    [InlineData("http://example.com/a.png")]
    [InlineData("https://cdn.example.org/img/1x2.jpg")]
    [InlineData("https://8.8.8.8/a.png")]
    public void Sanitize_KeepsPublicImageSources(string url)
    {
        var result = _sanitizer.Sanitize($"<img src=\"{url}\">", Base);

        Assert.Contains(url, result);
    }

    /// <summary>
    /// Only src is restricted. A link the reader chooses to click may legitimately point at an
    /// internal host, and the renderer does not fetch href on its own.
    /// </summary>
    [Fact]
    public void Sanitize_KeepsHrefToPrivateHostButDropsSrc()
    {
        var result = _sanitizer.Sanitize(
            "<a href=\"http://192.168.1.1/router\">admin</a><img src=\"http://192.168.1.1/pixel.png\">",
            Base);

        Assert.Contains("http://192.168.1.1/router", result);
        Assert.DoesNotContain("pixel.png", result);
    }

    /// <summary>
    /// A relative image path resolves against the article URL, which is public, so it stays.
    /// </summary>
    [Fact]
    public void Sanitize_KeepsRelativeImagePaths()
    {
        var result = _sanitizer.Sanitize("<img src=\"/images/photo.jpg\">", Base);

        Assert.Contains("photo.jpg", result);
    }
}
