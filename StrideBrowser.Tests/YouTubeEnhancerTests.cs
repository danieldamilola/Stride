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

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.5)]
    [InlineData(0.25, 0.25)]
    [InlineData(2.0, 2.0)]
    public void NormalizeSpeed_WithValueInPlayerRange_PassesThrough(double speed, double expected)
    {
        Assert.Equal(expected, YouTubeEnhancer.NormalizeSpeed(speed));
    }

    [Fact]
    public void NormalizeSpeed_AbovePlayerRange_ClampsToTwo()
    {
        Assert.Equal(2.0, YouTubeEnhancer.NormalizeSpeed(3.0));
    }

    [Fact]
    public void NormalizeSpeed_BelowPlayerRange_ClampsToQuarter()
    {
        Assert.Equal(0.25, YouTubeEnhancer.NormalizeSpeed(0.1));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NormalizeSpeed_WithNonFiniteValue_FallsBackToNormal(double speed)
    {
        Assert.Equal(1.0, YouTubeEnhancer.NormalizeSpeed(speed));
    }

    [Fact]
    public void NormalizeSpeed_WithNull_FallsBackToNormal()
    {
        Assert.Equal(1.0, YouTubeEnhancer.NormalizeSpeed(null));
    }
}