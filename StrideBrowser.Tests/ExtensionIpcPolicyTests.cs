using StrideBrowser.Engine;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ExtensionIpcPolicyTests
{
    [Theory]
    [InlineData("extensions-sync")]
    [InlineData("extensions-load")]
    [InlineData("extensions-reload-all")]
    [InlineData("extensions-developer:true")]
    [InlineData("extensions-toggle:{\"id\":\"id\",\"enabled\":false}")]
    [InlineData("extensions-pin:{\"id\":\"id\",\"pinned\":true}")]
    [InlineData("extensions-remove:id")]
    [InlineData("extensions-reload:id")]
    [InlineData("extensions-open-options:id")]
    [InlineData("extensions-open-popup:id")]
    public void ExtensionCommands_AreRestrictedToTheExtensionsDocument(string route)
    {
        foreach (var kind in Enum.GetValues<TabEngine.InternalPageKind>())
            Assert.Equal(kind == TabEngine.InternalPageKind.Extensions, IpcSecurityPolicy.IsRouteAllowedForPage(kind, route));
        Assert.False(IpcSecurityPolicy.IsRouteAllowedForLocalAsset(route));
    }

    [Theory]
    [InlineData("extensions-load:C:\\untrusted")]
    [InlineData("extensions-sync-extra")]
    [InlineData("setting:adBlock:true")]
    [InlineData("download-open:id")]
    [InlineData("open:file:///C:/Windows")]
    public void ExtensionsDocument_CannotSendUnrelatedOrExtendedExactCommands(string route) =>
        Assert.False(IpcSecurityPolicy.IsRouteAllowedForPage(TabEngine.InternalPageKind.Extensions, route));
}
