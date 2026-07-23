using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

public class FocusDomainMatcherTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("www.example.com", "example.com")]
    [InlineData("EXAMPLE.com", "example.com")]
    public void MatchesCustomDomain_MatchesExactAndSubdomains(string host, string customDomain)
    {
        Assert.True(FocusDomainMatcher.MatchesCustomDomain(host, new[] { customDomain }));
    }

    [Fact]
    public void MatchesCustomDomain_DoesNotMatchUnrelatedDomain()
    {
        Assert.False(FocusDomainMatcher.MatchesCustomDomain("example.com", new[] { "other.com" }));
    }

    [Fact]
    public void MatchesCustomDomain_DoesNotFalsePositiveOnSuffixCollision()
    {
        // "notexample.com" must not match a block on "example.com"
        Assert.False(FocusDomainMatcher.MatchesCustomDomain("notexample.com", new[] { "example.com" }));
    }

    [Fact]
    public void MatchesBlockedDomain_MatchesDeepSubdomain()
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "badsite.com" };

        var result = FocusDomainMatcher.MatchesBlockedDomain("m.images.badsite.com", blocked, out var matched);

        Assert.True(result);
        Assert.Equal("badsite.com", matched);
    }

    [Fact]
    public void MatchesBlockedDomain_ReturnsFalseWhenNotPresent()
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "badsite.com" };

        var result = FocusDomainMatcher.MatchesBlockedDomain("goodsite.com", blocked, out var matched);

        Assert.False(result);
        Assert.Null(matched);
    }
}
