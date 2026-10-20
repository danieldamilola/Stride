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

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BuildBrowserArguments_LeavesAutoplayPolicyAtTheWebView2Default(bool smartScreen, bool dark)
    {
        // Stride is a browser, so Chromium's normal autoplay policy applies: unmuted
        // autoplay waits for user engagement and a muted video is allowed to start.
        // Overriding the policy is not a product setting. Chrome documents
        // --autoplay-policy=no-user-gesture-required as disabling the policy entirely
        // for developer testing, and disabling the policy is not the same as fixing a
        // playback stall. This test exists to stop that override creeping back in as a
        // blanket change without a reproduction of the real symptom.
        var args = BuildArgs(smartScreen, dark);
        Assert.DoesNotContain("--autoplay-policy", args);
    }

    [Fact]
    public void BuildBrowserArguments_KeepsFileAccessIsolated()
    {
        // No origin isolation bypass may appear in the launch arguments.
        var args = BuildArgs(smartScreen: true, dark: false);
        Assert.DoesNotContain("allow-file-access-from-files", args);
        Assert.DoesNotContain("--disable-web-security", args);
    }
}
