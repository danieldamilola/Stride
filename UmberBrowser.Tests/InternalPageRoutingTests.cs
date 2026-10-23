using System;
using UmberBrowser.Engine;
using UmberBrowser.Models;
using UmberBrowser.Services.Pages;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Locks in the built in page routing map. Every internal URL the engine can
/// create must resolve to the page that renders it, so restore, hibernation
/// wake and transfer recycle all serve content without a caller remembering
/// a second navigation step. The popup sentinel and external URLs must never
/// resolve to a page.
/// </summary>
public sealed class InternalPageRoutingTests
{
    [Theory]
    [InlineData("internal://newtab", TabEngine.InternalPageKind.NewTab)]
    [InlineData("internal://settings", TabEngine.InternalPageKind.Settings)]
    [InlineData("internal://onetab", TabEngine.InternalPageKind.OneTab)]
    [InlineData("internal://history", TabEngine.InternalPageKind.History)]
    [InlineData("internal://downloads", TabEngine.InternalPageKind.Downloads)]
    [InlineData("internal://onboarding", TabEngine.InternalPageKind.Onboarding)]
    [InlineData("internal://releasenotes", TabEngine.InternalPageKind.ReleaseNotes)]
    [InlineData("internal://focus", TabEngine.InternalPageKind.Focus)]
    [InlineData("internal://health", TabEngine.InternalPageKind.Health)]
    [InlineData("internal://pending-native", TabEngine.InternalPageKind.PendingNative)]
    public void GetInternalPageKind_MapsKnownUrls(string url, TabEngine.InternalPageKind expected)
    {
        Assert.Equal(expected, TabEngine.GetInternalPageKind(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetInternalPageKind_EmptyUrl_MeansNewTab(string? url)
    {
        Assert.Equal(TabEngine.InternalPageKind.NewTab, TabEngine.GetInternalPageKind(url));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("about:blank")]
    [InlineData("data:text/html,hi")]
    [InlineData("internal://unknown")]
    public void GetInternalPageKind_NonPageUrls_MapToNone(string url)
    {
        Assert.Equal(TabEngine.InternalPageKind.None, TabEngine.GetInternalPageKind(url));
    }

    [Fact]
    public void GetInternalPageKind_CoversEveryDeclaredInternalUrl()
    {
        Assert.Equal(TabEngine.InternalPageKind.NewTab, TabEngine.GetInternalPageKind(InternalUrls.NewTab));
        Assert.Equal(TabEngine.InternalPageKind.Settings, TabEngine.GetInternalPageKind(InternalUrls.Settings));
        Assert.Equal(TabEngine.InternalPageKind.OneTab, TabEngine.GetInternalPageKind(InternalUrls.OneTab));
        Assert.Equal(TabEngine.InternalPageKind.History, TabEngine.GetInternalPageKind(InternalUrls.History));
        Assert.Equal(TabEngine.InternalPageKind.Downloads, TabEngine.GetInternalPageKind(InternalUrls.Downloads));
        Assert.Equal(TabEngine.InternalPageKind.Onboarding, TabEngine.GetInternalPageKind(InternalUrls.Onboarding));
        Assert.Equal(TabEngine.InternalPageKind.ReleaseNotes, TabEngine.GetInternalPageKind(InternalUrls.ReleaseNotes));
        Assert.Equal(TabEngine.InternalPageKind.Focus, TabEngine.GetInternalPageKind(InternalUrls.Focus));
        Assert.Equal(TabEngine.InternalPageKind.Health, TabEngine.GetInternalPageKind(InternalUrls.Health));
    }

    [Theory]
    [InlineData("internal://newtab")]
    [InlineData("internal://settings")]
    [InlineData("internal://onetab")]
    [InlineData("internal://history")]
    [InlineData("internal://downloads")]
    [InlineData("internal://onboarding")]
    [InlineData("internal://releasenotes")]
    [InlineData("internal://focus")]
    [InlineData("internal://health")]
    [InlineData("")]
    public void IsServableBuiltInPage_TrueForAllPages(string url)
    {
        Assert.True(TabEngine.IsServableBuiltInPage(url));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://localhost:5000/")]
    [InlineData("about:blank")]
    [InlineData("internal://pending-native")]
    public void IsServableBuiltInPage_FalseForNonPages(string url)
    {
        Assert.False(TabEngine.IsServableBuiltInPage(url));
    }

    [Fact]
    public void TryApplyOptionalCoreSetting_RunsHealthySetter()
    {
        var ran = false;
        TabEngine.TryApplyOptionalCoreSetting(() => ran = true, "probe", Guid.NewGuid());
        Assert.True(ran);
    }

    [Fact]
    public void TryApplyOptionalCoreSetting_FailingAccessorCannotHideHealthyView()
    {
        var order = new System.Collections.Generic.List<string>();
        var failing = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        TabEngine.TryApplyOptionalCoreSetting(() => { order.Add("failing"); throw new InvalidOperationException("stale core"); }, "MemoryUsageTargetLevel", failing);
        TabEngine.TryApplyOptionalCoreSetting(() => order.Add("healthy"), "MemoryUsageTargetLevel", healthy);
        Assert.Equal(new[] { "failing", "healthy" }, order);
    }

    [Fact]
    public void SettingsPageRender_LeavesNoTemplateTokens()
    {
        var html = new SettingsPage().Render(new BrowserSettings(), "token123");
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public void SettingsPageRender_EmbedsTokenWithoutAccentControls()
    {
        var settings = new BrowserSettings();
        var html = new SettingsPage().Render(settings, "token123");
        Assert.DoesNotContain("accentPicker", html);
        Assert.Contains("token123", html);
    }
}
