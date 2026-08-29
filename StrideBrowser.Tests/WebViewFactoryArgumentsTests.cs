using System.Reflection;
using StrideBrowser.Engine;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Locks in security-relevant properties of the WebView2 launch arguments.
/// A regression here (e.g. someone re-adding <c>--allow-file-access-from-files</c>)
/// would re-open the file:// origin-isolation hole.
/// </summary>
public class WebViewFactoryArgumentsTests
{
    private static string BuildArgs(bool smartScreen, bool dark) =>
        WebViewFactory.BuildBrowserArguments(smartScreen, dark);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BuildBrowserArguments_NeverIncludesFileAccessFlags(bool smartScreen, bool dark)
    {
        var args = BuildArgs(smartScreen, dark);
        // The file:// sandbox is enforced by Chromium by default; do not re-enable the bypass.
        Assert.DoesNotContain("allow-file-access-from-files", args);
        Assert.DoesNotContain("allow-file-access", args);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BuildBrowserArguments_DisablesChromiumPhoning(bool smartScreen, bool dark)
    {
        var args = BuildArgs(smartScreen, dark);
        Assert.Contains("disable-background-networking", args);
        Assert.Contains("disable-breakpad", args);
        Assert.Contains("disable-component-update", args);
        Assert.Contains("disable-default-apps", args);
        Assert.Contains("disable-domain-reliability", args);
        Assert.Contains("disable-sync", args);
        Assert.Contains("metrics-recording-only", args);
    }

    [Fact]
    public void BuildBrowserArguments_SmartScreenOff_AddsDisableFlag()
    {
        var args = BuildArgs(smartScreen: false, dark: false);
        Assert.Contains("msSmartScreenProtection", args);
    }

    [Fact]
    public void BuildBrowserArguments_SmartScreenOn_OmitsDisableFlag()
    {
        var args = BuildArgs(smartScreen: true, dark: false);
        Assert.DoesNotContain("msSmartScreenProtection", args);
    }

    [Fact]
    public void BuildBrowserArguments_ForceDark_AddsForceDarkFeature()
    {
        var args = BuildArgs(smartScreen: true, dark: true);
        Assert.Contains("WebContentsForceDark", args);
    }
}
