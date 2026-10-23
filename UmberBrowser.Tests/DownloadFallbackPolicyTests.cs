using UmberBrowser.Services.Downloads;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Guards the decisions behind the inline-response fallback for downloads.
/// Hidden transfer windows only adopt attachments; a URL that answers with a
/// renderable body never raises DownloadStarting, so the browser fetches the
/// bytes with HTTP. The policy decides whether a response body is a file at
/// all, how a part-written download resumes, and what failures say.
/// </summary>
public sealed class DownloadFallbackPolicyTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("IMAGE/PNG; charset=binary", true)]
    [InlineData("application/octet-stream", true)]
    [InlineData("application/pdf", true)]
    [InlineData("text/html", false)]
    [InlineData("text/html; charset=utf-8", false)]
    [InlineData("text/plain", false)]
    [InlineData("application/json", false)]
    [InlineData("text/xml", false)]
    public void ShouldAttempt_AcceptsBodiesAndRefusesRenderedDocuments(string? mediaType, bool expected)
    {
        Assert.Equal(expected, DownloadFallbackPolicy.ShouldAttempt(mediaType));
    }

    [Fact]
    public void PlanResume_NothingDownloaded_RestartsFromZero()
    {
        var (offset, ifRange) = DownloadFallbackPolicy.PlanResume(true, 0, "\"e1\"", "yesterday");

        Assert.Equal(0, offset);
        Assert.Null(ifRange);
    }

    [Fact]
    public void PlanResume_ServerRejectsRanges_RestartsFromZero()
    {
        var (offset, ifRange) = DownloadFallbackPolicy.PlanResume(false, 500, "\"e1\"", null);

        Assert.Equal(0, offset);
        Assert.Null(ifRange);
    }

    [Fact]
    public void PlanResume_RangesAndETag_AppendsWithETag()
    {
        var (offset, ifRange) = DownloadFallbackPolicy.PlanResume(true, 500, "\"e1\"", null);

        Assert.Equal(500, offset);
        Assert.Equal("\"e1\"", ifRange);
    }

    [Fact]
    public void PlanResume_RangesAndLastModified_AppendsWithDate()
    {
        var (offset, ifRange) = DownloadFallbackPolicy.PlanResume(true, 500, null, "Wed, 21 Oct 2015 07:28:00 GMT");

        Assert.Equal(500, offset);
        Assert.Equal("Wed, 21 Oct 2015 07:28:00 GMT", ifRange);
    }

    [Fact]
    public void PlanResume_RangesWithoutValidator_RestartsFromZero()
    {
        // Range without If-Range can append a changed source onto the old
        // bytes, so no validator means no append.
        var (offset, ifRange) = DownloadFallbackPolicy.PlanResume(true, 500, null, null);

        Assert.Equal(0, offset);
        Assert.Null(ifRange);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(407)]
    public void DescribeStatus_PermissionFailures_PointAtTheWebsite(int status)
    {
        var message = DownloadFallbackPolicy.DescribeStatus(status);

        Assert.Equal("This download needs permission. Sign in on the website and download the file again.", message);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    [InlineData(500)]
    [InlineData(503)]
    public void DescribeStatus_ServerFailures_PointAtTheLink(int status)
    {
        var message = DownloadFallbackPolicy.DescribeStatus(status);

        Assert.Equal("The server could not provide the file. Try the download link on the website again.", message);
    }

    [Fact]
    public void DescribeStatus_OtherStatuses_Unmapped()
    {
        Assert.Null(DownloadFallbackPolicy.DescribeStatus(200));
        Assert.Null(DownloadFallbackPolicy.DescribeStatus(302));
    }
}
