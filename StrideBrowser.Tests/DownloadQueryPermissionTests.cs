using StrideBrowser.Engine;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class DownloadQueryPermissionTests
{
    [Fact]
    public void DownloadQuery_IsAllowedOnlyForDownloadsDocument()
    {
        const string query = "download-sync:{\"page\":1,\"search\":\"example\"}";
        foreach (var kind in Enum.GetValues<TabEngine.InternalPageKind>())
            Assert.Equal(kind == TabEngine.InternalPageKind.Downloads,
                IpcSecurityPolicy.IsRouteAllowedForPage(kind, query));
    }

    [Theory]
    [InlineData("download-sync-extra")]
    [InlineData("download-syncx:{}")]
    [InlineData("download-sync {}")]
    public void SimilarRouteNames_AreDenied(string message)
    {
        Assert.False(IpcSecurityPolicy.IsRouteAllowedForPage(TabEngine.InternalPageKind.Downloads, message));
    }
}
