using System;
using StrideBrowser.Models;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Covers the active identity activation guard. Only a newly active tab
/// activates. Metadata-only changes on the active tab never do, and
/// returning to a previously seen tab activates again so the right view
/// shows. Inactive tabs never activate.
/// </summary>
public sealed class TabActivationGuardTests
{
    private static BrowserTab ActiveTab(string url) =>
        new() { Url = url, Title = "Tab", IsActive = true };

    [Fact]
    public void FirstSightingOfActiveTab_Activates()
    {
        var guard = new TabActivationGuard();
        Assert.True(guard.ShouldActivate(ActiveTab("https://github.com")));
    }

    [Fact]
    public void InactiveTab_NeverActivates()
    {
        var guard = new TabActivationGuard();
        var tab = new BrowserTab { Url = "https://github.com", IsActive = false };
        Assert.False(guard.ShouldActivate(tab));
        Assert.False(guard.ShouldActivate(tab));
    }

    [Theory]
    [InlineData("https://github.com changed", "Tab", "#ffffff")]
    [InlineData("https://github.com", "Tab changed", "#ffffff")]
    [InlineData("https://github.com", "Tab", "#000000")]
    public void MetadataOnlyChanges_DoNotActivate(string url, string title, string theme)
    {
        var guard = new TabActivationGuard();
        var tab = ActiveTab("https://github.com");
        tab.ThemeColor = "#ffffff";
        Assert.True(guard.ShouldActivate(tab));
        tab.Url = url;
        tab.Title = title;
        tab.ThemeColor = theme;
        Assert.False(guard.ShouldActivate(tab));
    }

    [Fact]
    public void AudioAndSleepFlags_DoNotActivate()
    {
        var guard = new TabActivationGuard();
        var tab = ActiveTab("https://github.com");
        Assert.True(guard.ShouldActivate(tab));
        tab.IsAudioPlaying = true;
        tab.IsMuted = true;
        tab.IsSleeping = true;
        Assert.False(guard.ShouldActivate(tab));
    }

    [Fact]
    public void SwitchBackAndForth_ActivatesEverySwitch()
    {
        var guard = new TabActivationGuard();
        var a = ActiveTab("https://a.example");
        var b = ActiveTab("https://b.example");
        Assert.True(guard.ShouldActivate(a));
        b.IsActive = true;
        a.IsActive = false;
        Assert.True(guard.ShouldActivate(b));
        a.IsActive = true;
        b.IsActive = false;
        Assert.True(guard.ShouldActivate(a));
        Assert.False(guard.ShouldActivate(a));
    }

    [Fact]
    public void ClosedActiveTab_AllowsNeighborActivation()
    {
        var guard = new TabActivationGuard();
        var a = ActiveTab("https://a.example");
        var b = new BrowserTab { Url = "https://b.example", IsActive = false };
        Assert.True(guard.ShouldActivate(a));
        guard.Forget(a.Id);
        b.IsActive = true;
        Assert.True(guard.ShouldActivate(b));
    }

    [Fact]
    public void ForgetOfOtherTab_KeepsCurrentActivation()
    {
        var guard = new TabActivationGuard();
        var a = ActiveTab("https://a.example");
        Assert.True(guard.ShouldActivate(a));
        guard.Forget(Guid.NewGuid());
        Assert.False(guard.ShouldActivate(a));
    }
}
