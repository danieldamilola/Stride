using System.Net;
using System.Net.Http;
using System.Text.Json;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class UpdateServiceReleaseTests
{
    [Fact]
    public async Task NoPublishedReleases_ReturnsNoUpdateWithoutFailure()
    {
        using var http = new HttpClient(new ReleaseHandler(HttpStatusCode.NotFound, "{}"));
        var service = new UpdateService(http);
        var notified = false;
        service.UpdateAvailable += (_, _) => notified = true;
        service.UpdateFailed += (_, _) => throw new Exception("An empty releases page is not an update failure.");

        Assert.Equal(false, await service.CheckForUpdatesQuietlyAsync());
        Assert.Null(service.LatestVersion);
        Assert.False(notified);
    }

    [Fact]
    public async Task ServerFailure_RemainsAnUnsuccessfulCheck()
    {
        using var http = new HttpClient(new ReleaseHandler(HttpStatusCode.InternalServerError, "{}"));
        var service = new UpdateService(http);
        Assert.Null(await service.CheckForUpdatesQuietlyAsync());
        Assert.Null(service.LatestVersion);
    }

    [Theory]
    [InlineData("v0.1.0", false)]
    [InlineData("v0.1.1", true)]
    public async Task NewVersionSequence_OnlyOffersNewerVersions(string tag, bool available)
    {
        var body = JsonSerializer.Serialize(new { tag_name = tag });
        using var http = new HttpClient(new ReleaseHandler(HttpStatusCode.OK, body));
        var service = new UpdateService(http);
        Assert.Equal(available, await service.CheckForUpdatesQuietlyAsync());
        Assert.Equal(available ? tag : null, service.LatestVersion);
    }

    [Fact]
    public async Task ReleaseWithoutAsset_BuildsDownloadUrlFromTheTag()
    {
        var body = ReleaseJson("v0.1.1");
        using var http = new HttpClient(new ReleaseHandler(HttpStatusCode.OK, body));
        var service = new UpdateService(http);

        Assert.True(await service.CheckForUpdatesQuietlyAsync());
        Assert.Equal(
            "https://github.com/danieldamilola/Umber/releases/download/v0.1.1/Umber-win-x64.zip",
            service.BuildDownloadUrl());
    }

    [Fact]
    public async Task AssetUrlFromUnrelatedHost_IsRejected()
    {
        var body = ReleaseJson("v0.1.1", ("Umber-win-x64.zip", "https://packages.example.test/Umber-win-x64.zip"));
        using var http = new HttpClient(new ReleaseHandler(HttpStatusCode.OK, body));
        var service = new UpdateService(http);

        Assert.True(await service.CheckForUpdatesQuietlyAsync());
        Assert.Equal(
            "https://github.com/danieldamilola/Umber/releases/download/v0.1.1/Umber-win-x64.zip",
            service.BuildDownloadUrl());
    }

    private static string ReleaseJson(string tag, params (string Name, string Url)[] assets)
        => JsonSerializer.Serialize(new
        {
            tag_name = tag,
            assets = assets.Select(a => new { name = a.Name, browser_download_url = a.Url }).ToArray()
        });

    private sealed class ReleaseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
