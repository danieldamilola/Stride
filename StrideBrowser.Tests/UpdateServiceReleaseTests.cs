using System.Net;
using System.Net.Http;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

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
        var body = System.Text.Json.JsonSerializer.Serialize(new { tag_name = tag });
        using var http = new HttpClient(new ReleaseHandler(HttpStatusCode.OK, body));
        var service = new UpdateService(http);
        Assert.Equal(available, await service.CheckForUpdatesQuietlyAsync());
        Assert.Equal(available ? tag : null, service.LatestVersion);
    }

    private sealed class ReleaseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
