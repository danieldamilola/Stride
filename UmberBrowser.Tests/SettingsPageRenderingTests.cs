using System;
using UmberBrowser.Models;
using UmberBrowser.Services.Pages;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Tests for the Settings page HTML rendering - verifies the JavaScript message posting logic.
/// </summary>
public sealed class SettingsPageRenderingTests
{
    [Fact]
    public void SettingsPage_IncludesCorrectTokenFormat()
    {
        var settings = new BrowserSettings();
        var ipcToken = "test-token-abc123";
        var html = new SettingsPage().Render(settings, ipcToken);

        // The token should be embedded in the JavaScript
        Assert.Contains($"const __T = \"{ipcToken}:\";", html);

        // The s() function should use __T + "setting:" + key + ":" + val
        Assert.Contains("window.chrome.webview.postMessage(__T + \"setting:\" + key + \":\" + val);", html);
    }

    [Fact]
    public void SettingsPage_UnhookMasterToggle_HasCorrectHandler()
    {
        var settings = new BrowserSettings();
        var ipcToken = "test-token";
        var html = new SettingsPage().Render(settings, ipcToken);

        // Master toggle should call s('unhookEnabled', this)
        Assert.Contains("onchange=\"s('unhookEnabled', this)\"", html);
    }

    [Fact]
    public void SettingsPage_UnhookSubToggles_HaveCorrectHandlers()
    {
        var settings = new BrowserSettings();
        var ipcToken = "test-token";
        var html = new SettingsPage().Render(settings, ipcToken);

        // Sub-toggles should call s() with their respective keys
        Assert.Contains("onchange=\"s('unhookHomeFeed', this)\"", html);
        Assert.Contains("onchange=\"s('unhookShorts', this)\"", html);
        Assert.Contains("onchange=\"s('unhookSidebar', this)\"", html);
        Assert.Contains("onchange=\"s('unhookLiveChat', this)\"", html);
        Assert.Contains("onchange=\"s('unhookPlaylist', this)\"", html);
        Assert.Contains("onchange=\"s('unhookFundraiser', this)\"", html);
        Assert.Contains("onchange=\"s('unhookEndFeed', this)\"", html);
        Assert.Contains("onchange=\"s('unhookEndCards', this)\"", html);
        Assert.Contains("onchange=\"s('unhookComments', this)\"", html);
        Assert.Contains("onchange=\"s('unhookMixes', this)\"", html);
        Assert.Contains("onchange=\"s('unhookMerch', this)\"", html);
        Assert.Contains("onchange=\"s('unhookVideoInfo', this)\"", html);
        Assert.Contains("onchange=\"s('unhookTopHeader', this)\"", html);
        Assert.Contains("onchange=\"s('unhookNotifications', this)\"", html);
        Assert.Contains("onchange=\"s('unhookInaptSearch', this)\"", html);
        Assert.Contains("onchange=\"s('unhookExplore', this)\"", html);
        Assert.Contains("onchange=\"s('unhookMoreYT', this)\"", html);
        Assert.Contains("onchange=\"s('unhookSubscriptions', this)\"", html);
        Assert.Contains("onchange=\"s('unhookAnnotations', this)\"", html);
    }

    [Fact]
    public void SettingsPage_PrivacySettings_HaveCorrectHandlers()
    {
        var settings = new BrowserSettings();
        var ipcToken = "test-token";
        var html = new SettingsPage().Render(settings, ipcToken);

        Assert.Contains("onchange=\"s('adBlock', this)\"", html);
        Assert.Contains("onchange=\"s('forceHttps', this)\"", html);
        Assert.Contains("onchange=\"s('darkMode', this)\"", html);
        Assert.Contains("onchange=\"s('searchSuggestions', this)\"", html);
        Assert.Contains("onchange=\"s('smartScreen', this)\"", html);
        Assert.Contains("onchange=\"s('clearOnExit', this)\"", html);
        Assert.Contains("onchange=\"s('faviconProxy', this)\"", html);
    }

    [Fact]
    public void SettingsPage_PerformanceSettings_HaveCorrectHandlers()
    {
        var settings = new BrowserSettings();
        var ipcToken = "test-token";
        var html = new SettingsPage().Render(settings, ipcToken);

        Assert.Contains("onchange=\"s('hwAccel', this)\"", html);
        Assert.Contains("onchange=\"s('tabHibernation', this)\"", html);
        Assert.Contains("onchange=\"s('tabSleep', this)\"", html);
        // Dim toggles use toggleDim function, not s()
        Assert.Contains("onchange=\"toggleDim('tabSleepDim', this, 'sleepSlider')\"", html);
        Assert.Contains("onchange=\"toggleDim('tabHibernationDim', this, 'hibSlider')\"", html);
    }

    [Fact]
    public void SettingsPage_GeneralSettings_HaveCorrectHandlers()
    {
        var settings = new BrowserSettings();
        var ipcToken = "test-token";
        var html = new SettingsPage().Render(settings, ipcToken);

        Assert.Contains("onchange=\"s('search', this)\"", html);
        Assert.Contains("onchange=\"s('zoom', this)\"", html);
        Assert.Contains("onchange=\"s('restoreSession', this)\"", html);
        Assert.Contains("onchange=\"s('blockDupes', this)\"", html);
        Assert.Contains("onchange=\"s('sidebarPosition', this)\"", html);
    }

    [Fact]
    public void SettingsPage_AppearanceSettings_HaveCorrectHandlers()
    {
        var settings = new BrowserSettings();
        var ipcToken = "test-token";
        var html = new SettingsPage().Render(settings, ipcToken);

        Assert.Contains("onchange=\"s('appTheme', this)\"", html);
        Assert.DoesNotContain("accentPicker", html);
        Assert.Contains("onchange=\"s('addressBarOnLeft', this)\"", html);
        Assert.Contains("onchange=\"s('adaptiveTint', this)\"", html);
        Assert.Contains("onchange=\"s('roundedFrame', this)\"", html);
    }

    [Fact]
    public void SettingsPage_NoUnresolvedTemplateTokens()
    {
        var settings = new BrowserSettings();
        var ipcToken = "test-token";
        var html = new SettingsPage().Render(settings, ipcToken);

        // No {{...}} tokens should remain
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public void SettingsPage_CheckboxValue_UsesCheckedProperty()
    {
        var settings = new BrowserSettings();
        var ipcToken = "test-token";
        var html = new SettingsPage().Render(settings, ipcToken);

        // The s() function should use el.checked for checkboxes
        Assert.Contains("el.type === \"checkbox\" ? el.checked : el.value", html);
    }

    [Fact]
    public void SettingsPage_UsesThemesWithoutCustomAccentControls()
    {
        var settings = new BrowserSettings();
        var ipcToken = "test-token";
        var html = new SettingsPage().Render(settings, ipcToken);

        Assert.Contains("onchange=\"s('appTheme', this)\"", html);
        Assert.DoesNotContain("accentPicker", html);
        Assert.DoesNotContain("setAccent", html);
        Assert.DoesNotContain("setting:accentColor", html);

    }

    [Fact]
    public void SettingsPage_OpacitySliders_UseUpdateOpacityFunction()
    {
        var settings = new BrowserSettings();
        var ipcToken = "test-token";
        var html = new SettingsPage().Render(settings, ipcToken);

        // Opacity sliders should use updateOpacity function
        Assert.Contains("oninput=\"updateOpacity('tabSleepOpacity', this, 'sleepSliderVal')\"", html);
        Assert.Contains("oninput=\"updateOpacity('tabHibernationOpacity', this, 'hibSliderVal')\"", html);
        Assert.Contains("function updateOpacity(key, el, labelId)", html);
        Assert.Contains("postSetting(key, el.value, el)", html);
    }

    [Fact]
    public void SettingsPage_DimToggles_UseToggleDimFunction()
    {
        var settings = new BrowserSettings();
        var ipcToken = "test-token";
        var html = new SettingsPage().Render(settings, ipcToken);

        // Dim toggles should use toggleDim function
        Assert.Contains("onchange=\"toggleDim('tabSleepDim', this, 'sleepSlider')\"", html);
        Assert.Contains("onchange=\"toggleDim('tabHibernationDim', this, 'hibSlider')\"", html);
        Assert.Contains("function toggleDim(key, el, sliderId)", html);
        Assert.Contains("s(key, el);", html);
    }
}
