using UmberBrowser.Engine;
using UmberBrowser.Models;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class ThemeColorDeduplicationTests
{
    [Fact]
    public void ReceiverOnlyChangesStateForANewValidEnabledValue()
    {
        var tab = new BrowserTab("https://example.com");
        Assert.True(WebViewIpcBridge.TryApplyThemeColor(tab, "#abcdef", true));
        Assert.False(WebViewIpcBridge.TryApplyThemeColor(tab, "#ABCDEF", true));
        Assert.False(WebViewIpcBridge.TryApplyThemeColor(tab, "red", true));
        Assert.False(WebViewIpcBridge.TryApplyThemeColor(tab, "#123456", false));
        Assert.Equal("#abcdef", tab.ThemeColor);
        Assert.True(WebViewIpcBridge.TryApplyThemeColor(tab, "", true));
        Assert.False(WebViewIpcBridge.TryApplyThemeColor(tab, "", true));
        Assert.True(WebViewIpcBridge.TryApplyThemeColor(tab, "#abcdef", true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BootstrapContainsTheCurrentSettingWithoutAnUnresolvedMarker(bool enabled)
    {
        var script = ContentScriptInjector.ThemeColorBootstrap(enabled);
        Assert.DoesNotContain("__STRIDE_THEME_ENABLED__", script);
        Assert.Contains("? true : " + (enabled ? "true" : "false") + ";", script);
        Assert.DoesNotContain("{{IPC_TOKEN}}", script);
    }
}

