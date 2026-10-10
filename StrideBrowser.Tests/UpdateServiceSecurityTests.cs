using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// The release tag is interpolated into the download URL and passed to the updater process, so
/// it has to look like a version and nothing else.
/// </summary>
public sealed class UpdateServiceSecurityTests
{
    [Theory]
    [InlineData("v1.2.3")]
    [InlineData("v1.2")]
    [InlineData("v2")]
    [InlineData("v10.20.30")]
    [InlineData("v1.2.3-beta")]
    [InlineData("v1.2.3.4")]
    public void IsAcceptableTag_WellFormedVersion_IsAccepted(string tag)
    {
        Assert.True(UpdateService.IsAcceptableTag(tag));
    }

    [Theory]
    // No leading v, which the download URL builder assumes.
    [InlineData("1.2.3")]
    // Path and query injection through the interpolated tag.
    [InlineData("v1.2.3/../../evil")]
    [InlineData("v1.2.3?x=1")]
    [InlineData("v1.2.3#f")]
    [InlineData("v1.2.3&other=1")]
    [InlineData("v../../../etc/passwd")]
    // Not a dotted numeric version.
    [InlineData("vlatest")]
    [InlineData("v1.x.3")]
    [InlineData("v..")]
    [InlineData("v1..2")]
    [InlineData("v1.2.3.")]
    [InlineData("v 1.2.3")]
    // Over the length cap.
    [InlineData("v1234567.1234567.1234567")]
    [InlineData(null)]
    [InlineData("")]
    public void IsAcceptableTag_MalformedOrHostileTag_IsRejected(string? tag)
    {
        Assert.False(UpdateService.IsAcceptableTag(tag));
    }

    [Fact]
    public void MaxPackageBytes_IsBounded()
    {
        Assert.True(UpdateService.MaxPackageBytes > 0);
        Assert.True(UpdateService.MaxPackageBytes <= 1024L * 1024 * 1024);
    }
    [Theory]
    [InlineData("sha256:B5008D421E42998B26CA74329D068D0F9F0097237B8D616D1F40900BAA3BFDB2",
                "B5008D421E42998B26CA74329D068D0F9F0097237B8D616D1F40900BAA3BFDB2")]
    [InlineData("SHA256:b5008d421e42998b26ca74329d068d0f9f0097237b8d616d1f40900baa3bfdb2",
                "B5008D421E42998B26CA74329D068D0F9F0097237B8D616D1F40900BAA3BFDB2")]
    public void ParseAssetDigest_ValidDigest_IsReturned(string digest, string expected)
    {
        Assert.Equal(expected, UpdateService.ParseAssetDigest(digest));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("md5:b5008d421e42998b26ca74329d068d0f9f0097237b8d616d1f40900baa3bfdb2")]
    [InlineData("sha256:tooshort")]
    [InlineData("b5008d421e42998b26ca74329d068d0f9f0097237b8d616d1f40900baa3bfdb2")]
    [InlineData("sha256:zzzz84421e42998b26ca74329d068d0f9f0097237b8d616d1f40900baa3bfdb2")]
    public void ParseAssetDigest_MissingOrWrongAlgorithm_IsRejected(string? digest)
    {
        Assert.Null(UpdateService.ParseAssetDigest(digest));
    }
}