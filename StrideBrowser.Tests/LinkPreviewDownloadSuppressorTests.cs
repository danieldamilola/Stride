using StrideBrowser.Services.LinkPreview;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class LinkPreviewDownloadSuppressorTests
{
    [Fact]
    public void ShouldSuppress_ReturnsTrueForRecentlyAddedUrl()
    {
        var suppressor = new LinkPreviewDownloadSuppressor();
        var url = "https://example.com/file.zip";

        suppressor.Add(url);

        Assert.True(suppressor.ShouldSuppress(url));
    }

    [Fact]
    public void ShouldSuppress_ReturnsFalseForUnaddedUrl()
    {
        var suppressor = new LinkPreviewDownloadSuppressor();

        Assert.False(suppressor.ShouldSuppress("https://example.com/other.zip"));
    }

    [Fact]
    public void ShouldSuppress_MatchesUrlIgnoringFragment()
    {
        var suppressor = new LinkPreviewDownloadSuppressor();
        var baseUrl = "https://example.com/document.pdf";

        suppressor.Add(baseUrl);

        Assert.True(suppressor.ShouldSuppress("https://example.com/document.pdf#page=2"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ShouldSuppress_HandlesNullOrWhitespaceSafely(string? url)
    {
        var suppressor = new LinkPreviewDownloadSuppressor();
        suppressor.Add("https://example.com/file.zip");

        Assert.False(suppressor.ShouldSuppress(url!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Add_HandlesNullOrWhitespaceSafely(string? url)
    {
        var suppressor = new LinkPreviewDownloadSuppressor();

        suppressor.Add(url!);

        Assert.False(suppressor.ShouldSuppress(""));
    }

    [Fact]
    public void ShouldSuppress_RemovesEntryImmediatelyOnSuppression()
    {
        var suppressor = new LinkPreviewDownloadSuppressor();
        var url = "https://example.com/file.zip";
        suppressor.Add(url);

        Assert.True(suppressor.ShouldSuppress(url));
        Assert.False(suppressor.ShouldSuppress(url));
    }

    [Fact]
    public void ShouldSuppress_RemovesFragmentStrippedEntryImmediatelyOnSuppression()
    {
        var suppressor = new LinkPreviewDownloadSuppressor();
        var baseUrl = "https://example.com/document.pdf";
        suppressor.Add(baseUrl);

        Assert.True(suppressor.ShouldSuppress("https://example.com/document.pdf#page=2"));
        Assert.False(suppressor.ShouldSuppress("https://example.com/document.pdf#page=2"));
    }
}
