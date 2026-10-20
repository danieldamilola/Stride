using System.IO;
using System.Text.RegularExpressions;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Pages;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class InternalPageSettingsTests
{
    public static IEnumerable<object[]> BooleanSettings()
    {
        yield return new object[] { "restoreSession", nameof(BrowserSettings.RestoreSessionOnStartup) };
        yield return new object[] { "blockDupes", nameof(BrowserSettings.BlockDuplicateTabs) };
        yield return new object[] { "darkMode", nameof(BrowserSettings.ForceDarkMode) };
        yield return new object[] { "hwAccel", nameof(BrowserSettings.HardwareAccelerationEnabled) };
        yield return new object[] { "forceHttps", nameof(BrowserSettings.ForceHttps) };
        yield return new object[] { "smartScreen", nameof(BrowserSettings.SmartScreenEnabled) };
        yield return new object[] { "adBlock", nameof(BrowserSettings.AdBlockEnabled) };
        yield return new object[] { "clearOnExit", nameof(BrowserSettings.ClearDataOnExit) };
        yield return new object[] { "searchSuggestions", nameof(BrowserSettings.SearchSuggestionsEnabled) };
        yield return new object[] { "faviconProxy", nameof(BrowserSettings.FaviconProxyEnabled) };
        yield return new object[] { "tabHibernation", nameof(BrowserSettings.TabHibernationEnabled) };
        yield return new object[] { "tabSleep", nameof(BrowserSettings.TabSleepEnabled) };
        yield return new object[] { "tabSleepDim", nameof(BrowserSettings.TabSleepDimEnabled) };
        yield return new object[] { "tabHibernationDim", nameof(BrowserSettings.TabHibernationDimEnabled) };
        yield return new object[] { "adaptiveTint", nameof(BrowserSettings.AdaptiveToolbarTint) };
        yield return new object[] { "showBackArrow", nameof(BrowserSettings.ShowBackArrow) };
        yield return new object[] { "showForwardArrow", nameof(BrowserSettings.ShowForwardArrow) };
        yield return new object[] { "showRefreshButton", nameof(BrowserSettings.ShowRefreshButton) };
        yield return new object[] { "showDownloadsIcon", nameof(BrowserSettings.ShowDownloadsIcon) };
        yield return new object[] { "showSettingsIcon", nameof(BrowserSettings.ShowSettingsIcon) };
        yield return new object[] { "showReaderIcon", nameof(BrowserSettings.ShowReaderIcon) };
        yield return new object[] { "roundedFrame", nameof(BrowserSettings.RoundedWebContentFrameEnabled) };
        yield return new object[] { "autoCheckForUpdates", nameof(BrowserSettings.AutoCheckForUpdates) };
        yield return new object[] { "unhookEnabled", nameof(BrowserSettings.UnhookEnabled) };
        yield return new object[] { "unhookHomeFeed", nameof(BrowserSettings.UnhookHideHomeFeed) };
        yield return new object[] { "unhookShorts", nameof(BrowserSettings.UnhookHideShorts) };
        yield return new object[] { "unhookSidebar", nameof(BrowserSettings.UnhookHideVideoSidebar) };
        yield return new object[] { "unhookLiveChat", nameof(BrowserSettings.UnhookHideLiveChat) };
        yield return new object[] { "unhookPlaylist", nameof(BrowserSettings.UnhookHidePlaylist) };
        yield return new object[] { "unhookFundraiser", nameof(BrowserSettings.UnhookHideFundraiser) };
        yield return new object[] { "unhookEndFeed", nameof(BrowserSettings.UnhookHideEndScreenFeed) };
        yield return new object[] { "unhookEndCards", nameof(BrowserSettings.UnhookHideEndScreenCards) };
        yield return new object[] { "unhookComments", nameof(BrowserSettings.UnhookHideComments) };
        yield return new object[] { "unhookMixes", nameof(BrowserSettings.UnhookHideMixes) };
        yield return new object[] { "unhookMerch", nameof(BrowserSettings.UnhookHideMerch) };
        yield return new object[] { "unhookVideoInfo", nameof(BrowserSettings.UnhookHideVideoInfo) };
        yield return new object[] { "unhookTopHeader", nameof(BrowserSettings.UnhookHideTopHeader) };
        yield return new object[] { "unhookNotifications", nameof(BrowserSettings.UnhookHideNotifications) };
        yield return new object[] { "unhookInaptSearch", nameof(BrowserSettings.UnhookHideInaptSearch) };
        yield return new object[] { "unhookExplore", nameof(BrowserSettings.UnhookHideExplore) };
        yield return new object[] { "unhookMoreYT", nameof(BrowserSettings.UnhookHideMoreYT) };
        yield return new object[] { "unhookSubscriptions", nameof(BrowserSettings.UnhookHideSubscriptions) };
        yield return new object[] { "unhookAnnotations", nameof(BrowserSettings.UnhookDisableAnnotations) };
    }

    [Theory]
    [MemberData(nameof(BooleanSettings))]
    public void EveryVisibleToggle_RoundtripsBothValues(string key, string propertyName)
    {
        var file = Path.Combine(Path.GetTempPath(), "StrideSettingsTest-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new SettingsStore(file);
            var settings = new BrowserSettings();
            var property = typeof(BrowserSettings).GetProperty(propertyName)!;
            foreach (var value in new[] { false, true, false })
            {
                Assert.True(SettingsChangePolicy.TryApply(settings, key, value ? "true" : "false", out var error), error);
                Assert.True(store.TrySave(settings));
                settings = store.Load();
                Assert.Equal(value, property.GetValue(settings));
            }
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void EveryRenderedToggleHasAnExpectedSettingProperty()
    {
        var known = BooleanSettings().Select(row => (string)row[0]).ToHashSet();
        var html = new SettingsPage().Render(new BrowserSettings(), "test");
        foreach (Match control in Regex.Matches(html, "<input[^>]*type=\"checkbox\"[^>]*onchange=\"(?:s|toggleDim)\\('([^']+)'[^>]*>"))
            Assert.Contains(control.Groups[1].Value, known);
    }

    [Fact]
    public void FocusLock_SavesCurrentDomainsAndSurvivesReset()
    {
        var settings = new BrowserSettings { FocusDomains = "old.example" };
        Assert.True(SettingsChangePolicy.TryApply(settings, "focusLock", "new.example%0Aother.example", out _));
        settings.ResetToDefaults();
        Assert.True(settings.FocusLocked);
        Assert.Equal("new.example\nother.example", settings.FocusDomains);
        Assert.False(SettingsChangePolicy.TryApply(settings, "focusLocked", "false", out _));
        Assert.False(SettingsChangePolicy.TryApply(settings, "focusDomains", "replacement.example", out _));
    }

    [Fact]
    public void Shortcut_UsesActionNameAndRejectsConflicts()
    {
        var settings = new BrowserSettings();
        Assert.True(SettingsChangePolicy.TryApply(settings, "shortcut", "NewTab:Ctrl+K", out _));
        Assert.Equal("Ctrl+K", settings.CustomShortcuts["NewTab"]);
        Assert.False(SettingsChangePolicy.TryApply(settings, "shortcut", "CloseTab:Ctrl+K", out _));
        Assert.False(SettingsChangePolicy.TryApply(settings, "shortcut", "Ctrl+T:Ctrl+K", out _));
        Assert.True(SettingsChangePolicy.TryApply(settings, "shortcutReset", "NewTab", out _));
        Assert.Empty(settings.CustomShortcuts);
    }

    [Theory]
    [InlineData("autoCheckForUpdates", "maybe")]
    [InlineData("appTheme", "unknown")]
    [InlineData("accentColor", "#broken")]
    [InlineData("tabSleepOpacity", "NaN")]
    [InlineData("unknown", "true")]
    public void InvalidChange_IsRejected(string key, string value)
    {
        Assert.False(SettingsChangePolicy.TryApply(new BrowserSettings(), key, value, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void InternalIdentity_AcceptsOnlyItsPreparedNavigationOnce()
    {
        const string html = "<html><body>Settings</body></html>";
        var identity = new InternalDocumentIdentity(Guid.NewGuid(), TabEngine.InternalPageKind.Settings, "token", 1, html);
        var dataUri = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(html));
        Assert.True(identity.TryStartNavigation(dataUri));
        Assert.False(identity.TryStartNavigation(dataUri));
        Assert.False(identity.TryStartNavigation("about:blank"));
        var other = new InternalDocumentIdentity(Guid.NewGuid(), TabEngine.InternalPageKind.Settings, "token", 2, html);
        Assert.False(other.TryStartNavigation("data:text/html;base64,another-document"));
        Assert.False(other.TryStartNavigation(dataUri));
    }
}
