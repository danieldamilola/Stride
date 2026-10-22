using StrideBrowser.Models;
using StrideBrowser.Services.Extensions;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class BuiltInExtensionsTests
{
    [Fact]
    public void CatalogReflectsActualSettingsAndProtectsFocus()
    {
        var settings = new BrowserSettings { UnhookEnabled = true, YtEnhancerEnabled = false, AdaptiveToolbarTint = true, FocusLocked = true, ForceDarkMode = false };
        var items = BuiltInExtensions.List(settings);
        Assert.Equal(5, items.Count);
        Assert.All(items, x => { Assert.True(x.IsBuiltIn); Assert.True(x.HasOptions); Assert.False(x.HasPopup); });
        Assert.True(items.Single(x => x.Id == "stride:unhook").IsEnabled);
        Assert.False(items.Single(x => x.Id == "stride:youtube").IsEnabled);
        Assert.True(items.Single(x => x.Id == "stride:tint").IsEnabled);
        var focus = items.Single(x => x.Id == "stride:focus");
        Assert.True(focus.IsEnabled); Assert.False(focus.CanToggle);
        Assert.Throws<InvalidOperationException>(() => BuiltInExtensions.SettingKey(focus.Id));
    }
    [Theory]
    [InlineData("stride:unhook", "unhookEnabled")]
    [InlineData("stride:youtube", "ytEnhancerEnabled")]
    [InlineData("stride:tint", "adaptiveTint")]
    [InlineData("stride:dark", "darkMode")]
    public void BuiltInToggleUsesExistingSettingsPolicy(string id, string key)
    {
        var settings = new BrowserSettings();
        Assert.Equal(key, BuiltInExtensions.SettingKey(id));
        Assert.True(StrideBrowser.Services.SettingsChangePolicy.TryApply(settings, key, "true", out _));
        Assert.True(BuiltInExtensions.List(settings).Single(x => x.Id == id).IsEnabled);
        Assert.True(StrideBrowser.Services.SettingsChangePolicy.TryApply(settings, key, "false", out _));
        Assert.False(BuiltInExtensions.List(settings).Single(x => x.Id == id).IsEnabled);
    }
}
