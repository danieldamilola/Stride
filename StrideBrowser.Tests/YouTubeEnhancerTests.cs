using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Covers the quality allowlist that gates values reaching the generated
/// YouTube enhancer JavaScript.
/// </summary>
public class YouTubeEnhancerTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData("highest")]
    [InlineData("lowest")]
    [InlineData("highres")]
    [InlineData("hd2160")]
    [InlineData("hd1440")]
    [InlineData("hd1080")]
    [InlineData("hd720")]
    [InlineData("large")]
    [InlineData("medium")]
    [InlineData("small")]
    [InlineData("tiny")]
    public void NormalizeQuality_WithValidValue_PassesThroughLowercased(string quality)
    {
        Assert.Equal(quality, YouTubeEnhancer.NormalizeQuality(quality));
    }

    [Fact]
    public void NormalizeQuality_WithMixedCase_Normalizes()
    {
        Assert.Equal("hd1080", YouTubeEnhancer.NormalizeQuality("HD1080"));
    }

    [Theory]
    [InlineData("8k")]
    [InlineData("4320p")]
    [InlineData("'; drop table")]
    [InlineData("\"}; alert(1); {\"")]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeQuality_WithUnknownValue_FallsBackToAuto(string quality)
    {
        Assert.Equal("auto", YouTubeEnhancer.NormalizeQuality(quality));
    }

    [Fact]
    public void NormalizeQuality_WithNull_FallsBackToAuto()
    {
        Assert.Equal("auto", YouTubeEnhancer.NormalizeQuality(null));
    }
}