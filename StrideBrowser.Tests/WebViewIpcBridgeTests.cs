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

    /// <summary>
    /// Only the application's own read-only asset folder may post web messages without the
    /// per-session IPC token.
    /// </summary>
    [Theory]
    [InlineData("https://local.assets/Settings.html")]
    [InlineData("http://local.assets/NewTab.html")]
    [InlineData("https://LOCAL.ASSETS/Anything.html")]
    public void IsTrustedAssetOrigin_AppOwnAssetFolder_IsTrusted(string source)
    {
        Assert.True(WebViewIpcBridge.IsTrustedAssetOrigin(source));
    }

    /// <summary>
    /// user.assets maps %APPDATA%\Stride\Backgrounds, a user-writable folder the Settings page
    /// opens in Explorer. Trusting it would let any HTML file dropped there call the privileged
    /// route table (install-update, download-request, download-open, open, setting) with no
    /// secret, so it must require the token like every other origin.
    /// </summary>
    [Theory]
    [InlineData("https://user.assets/payload.html")]
    [InlineData("https://user.assets/Backgrounds/payload.html")]
    [InlineData("http://user.assets/payload.html")]
    public void IsTrustedAssetOrigin_UserWritableBackgroundsFolder_IsNotTrusted(string source)
    {
        Assert.False(WebViewIpcBridge.IsTrustedAssetOrigin(source));
    }

    /// <summary>
    /// A lookalike host must not inherit trust from a prefix match.
    /// </summary>
    [Theory]
    [InlineData("https://local.assets.evil.test/x.html")]
    [InlineData("https://notlocal.assets/x.html")]
    [InlineData("https://evil.test/?u=https://local.assets/")]
    [InlineData("file:///C:/local.assets/x.html")]
    [InlineData("https://127.0.0.1/local.assets/")]
    public void IsTrustedAssetOrigin_LookalikeOrNonHttpOrigin_IsNotTrusted(string source)
    {
        Assert.False(WebViewIpcBridge.IsTrustedAssetOrigin(source));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a uri")]
    public void IsTrustedAssetOrigin_MissingOrUnparseableSource_IsNotTrusted(string? source)
    {
        Assert.False(WebViewIpcBridge.IsTrustedAssetOrigin(source));
    }

    /// <summary>
    /// The session token used to be published as window.__T on wallhaven.cc, so any script on
    /// that origin could drive every privileged route. Authorisation is by exact host instead.
    /// </summary>
    [Theory]
    [InlineData("https://wallhaven.cc/wallpaper/abc")]
    [InlineData("https://www.wallhaven.cc/tag/x")]
    [InlineData("http://wallhaven.cc/")]
    public void IsWallhavenSource_AllowlistedHosts_AreAccepted(string source)
    {
        Assert.True(WebViewIpcBridge.IsWallhavenSource(source));
    }

    [Theory]
    [InlineData("https://wallhaven.cc.evil.test/x")]
    [InlineData("https://notwallhaven.cc/x")]
    [InlineData("https://evil.test/?r=https://wallhaven.cc/")]
    [InlineData("file:///wallhaven.cc/x")]
    [InlineData("https://example.com/")]
    [InlineData(null)]
    [InlineData("")]
    public void IsWallhavenSource_LookalikeOrOtherOrigins_AreRejected(string? source)
    {
        Assert.False(WebViewIpcBridge.IsWallhavenSource(source));
    }

    /// <summary>
    /// A cross-origin iframe posts into the same bridge, so an ad frame must not be able to
    /// repaint the toolbar or force a preview for the page it sits inside.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/page", "https://example.com/page")]
    [InlineData("https://example.com/page#section", "https://example.com/page")]
    [InlineData("https://example.com/page?utm=1", "https://example.com/page")]
    [InlineData("https://EXAMPLE.com/page", "https://example.com/page")]
    public void IsSameTopLevelDocument_SameDocument_IsAccepted(string source, string topLevel)
    {
        Assert.True(WebViewIpcBridge.IsSameTopLevelDocument(source, topLevel));
    }

    [Theory]
    [InlineData("https://ads.evil.test/frame.html", "https://example.com/page")]
    [InlineData("https://example.com/other", "https://example.com/page")]
    [InlineData("http://example.com/page", "https://example.com/page")]
    [InlineData("https://example.com:8443/page", "https://example.com/page")]
    public void IsSameTopLevelDocument_FrameOrDifferentDocument_IsRejected(string source, string topLevel)
    {
        Assert.False(WebViewIpcBridge.IsSameTopLevelDocument(source, topLevel));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("https://example.com/", null)]
    [InlineData("https://example.com/", "")]
    [InlineData("not a uri", "https://example.com/")]
    public void IsSameTopLevelDocument_UnparseableSide_FailsOpen(string? source, string? topLevel)
    {
        Assert.True(WebViewIpcBridge.IsSameTopLevelDocument(source, topLevel));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#D4A574")]
    [InlineData("#d4a574")]
    [InlineData("#FFFFFF")]
    public void IsThemeColorValue_EmptyOrHex_IsAccepted(string value)
    {
        Assert.True(WebViewIpcBridge.IsThemeColorValue(value));
    }

    [Theory]
    [InlineData("#GGG")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("123456")]
    [InlineData("red; background: url(http://evil.test)")]
    [InlineData("</style><script>alert(1)</script>")]
    [InlineData("#abc\n#def")]
    public void IsThemeColorValue_MalformedValue_IsRejected(string value)
    {
        Assert.False(WebViewIpcBridge.IsThemeColorValue(value));
    }
}
