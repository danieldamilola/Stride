using StrideBrowser.Engine.Handlers;
using StrideBrowser.Models;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class TabDownloadHandlerTests
{
    [Fact]
    public void ResolveDownloadFileName_ReturnsFileNameFromResultPath()
    {
        Assert.Equal("setup.exe", TabDownloadHandler.ResolveDownloadFileName(@"C:\Users\Test\Downloads\setup.exe", "https://example.com/other.zip"));
    }

    [Fact]
    public void ResolveDownloadFileName_FallsBackToUrlWhenPathMissing()
    {
        Assert.Equal("mt5setup.exe", TabDownloadHandler.ResolveDownloadFileName(null, "https://cdn.example.com/files/mt5setup.exe"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData(null, "https://example.com/")]
    [InlineData("", "blob:https://example.com/550e8400-e29b-41d4-a716-446655440000")]
    public void ResolveDownloadFileName_ReturnsDefaultWhenNothingUsable(string? path, string? url)
    {
        Assert.Equal("download", TabDownloadHandler.ResolveDownloadFileName(path, url));
    }

    [Fact]
    public void CreateDownloadItem_MapsValuesWithoutTouchingNativeOp()
    {
        var item = TabDownloadHandler.CreateDownloadItem(@"C:\Users\Test\Downloads\app.exe", "https://example.com/app.exe", 1024);

        Assert.Equal("app.exe", item.FileName);
        Assert.Equal("https://example.com/app.exe", item.Url);
        Assert.Equal(1024, item.TotalBytes);
        Assert.Equal(0, item.ReceivedBytes);
        Assert.Equal(DownloadState.InProgress, item.State);
    }

    [Fact]
    public void CreateDownloadItem_NeverLeavesNullFileNameOrPath()
    {
        var item = TabDownloadHandler.CreateDownloadItem(null, null, null);

        Assert.Equal("download", item.FileName);
        Assert.Equal("", item.FilePath);
        Assert.Equal("", item.Url);
        Assert.Equal(0, item.TotalBytes);
    }
}
