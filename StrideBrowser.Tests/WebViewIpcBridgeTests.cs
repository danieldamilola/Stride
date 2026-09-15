using System.Linq;
using System.Reflection;
using StrideBrowser.Engine;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class WebViewIpcBridgeTests
{
    [Fact]
    public void TryDetachHostWindowClose_NullInputs_ReturnsFalse()
    {
        Assert.False(WebViewIpcBridge.TryDetachHostWindowClose(null, null));
        Assert.False(WebViewIpcBridge.TryDetachHostWindowClose(new object(), null));
        Assert.False(WebViewIpcBridge.TryDetachHostWindowClose(null, new object()));
    }

    [Fact]
    public void TryDetachHostWindowClose_UnknownObjects_ReturnsFalseWithoutThrowing()
    {
        Assert.False(WebViewIpcBridge.TryDetachHostWindowClose(new object(), new object()));
    }

    /// <summary>
    /// Locks the assumptions behind TryDetachHostWindowClose. The shipped WebView2 package
    /// keeps a WebView2Base engine inside each WPF control, that engine carries a
    /// CoreWebView2_WindowCloseRequested handler that closes the host window, and
    /// CoreWebView2 exposes the WindowCloseRequested event it is attached to. If a
    /// WebView2 update moves any of these, this test goes red and the detach must be
    /// revisited before download pages start closing the browser again.
    /// </summary>
    [Fact]
    public void ShippedWebView2_HasDetachableWindowCloseHandler()
    {
        var wpf = typeof(Microsoft.Web.WebView2.Wpf.WebView2).Assembly;
        var innerBase = wpf.GetType("Microsoft.Web.WebView2.Wpf.WebView2Base");
        Assert.NotNull(innerBase);
        Assert.NotNull(innerBase.GetMethod("CoreWebView2_WindowCloseRequested",
            BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.NotNull(typeof(Microsoft.Web.WebView2.Core.CoreWebView2).GetEvent("WindowCloseRequested"));

        foreach (var controlType in new[]
                 {
                     typeof(Microsoft.Web.WebView2.Wpf.WebView2),
                     typeof(Microsoft.Web.WebView2.Wpf.WebView2CompositionControl)
                 })
        {
            var field = null as FieldInfo;
            for (var t = controlType; t is not null && t != typeof(object); t = t.BaseType)
                field ??= t.GetField("m_webview2Base", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.NotNull(field);
            Assert.Equal(innerBase, field.FieldType);
        }
    }
}
