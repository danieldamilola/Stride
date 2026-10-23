using System.IO;
using UmberBrowser.Services;
using UmberBrowser.Services.Startup;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class ShellIntegrationPolicyTests
{
    [Fact]
    public void AppId_MatchesInstallerAndProcessId()
    {
        Assert.Equal("Umber", ShellIntegrationPolicy.AppId);
    }

    [Fact]
    public void DefaultBrowserRegistrar_TargetsUmberExecutable()
    {
        Assert.Equal("Umber.exe", DefaultBrowserRegistrar.ExecutableFileName);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void ShouldRefresh_RefreshesOnlyAfterUpdate(bool postUpdate, bool newVersion, bool expected)
    {
        Assert.Equal(expected, ShellIntegrationPolicy.ShouldRefresh(postUpdate, newVersion));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Other", true)]
    [InlineData("umber", true)]
    [InlineData("Umber", false)]
    public void NeedsRepair_OnlyExactAppIdPasses(string? current, bool expected)
    {
        Assert.Equal(expected, ShellIntegrationPolicy.NeedsRepair(current));
    }

    [Fact]
    public void GetStartMenuShortcutPath_CombinesUmberFolder()
    {
        var path = ShellIntegrationPolicy.GetStartMenuShortcutPath(@"C:\Start\Programs");
        Assert.Equal(Path.Combine(@"C:\Start\Programs", "Umber", "Umber.lnk"), path);
    }

    [Fact]
    public void GetTaskbarPinnedShortcutPath_PointsAtTaskBarFolder()
    {
        var path = ShellIntegrationPolicy.GetTaskbarPinnedShortcutPath(@"C:\Users\Test\AppData\Roaming");
        Assert.Equal(
            Path.Combine(@"C:\Users\Test\AppData\Roaming", "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar", "Umber.lnk"),
            path);
    }
}
