using System.IO;
using System.Text.Json;
using UmberBrowser.Models;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class SettingsStoreAccentColorTests
{
    [Theory]
    [InlineData("#c47b6a")]
    [InlineData("#abc")]
    [InlineData("</style><script>alert(1)</script>")]
    [InlineData(null)]
    public void OldAccentIsIgnoredWhileOtherPreferencesSurviveLoadAndSave(string? accent)
    {
        var directory = Path.Combine(Path.GetTempPath(), "UmberThemeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { accentColor = accent, appTheme = 2, searchEngine = "Brave", schemaVersion = 100 }));
            var store = new SettingsStore(path);
            var settings = store.Load();
            Assert.Equal(AppThemeMode.Dark, settings.AppTheme);
            Assert.Equal("Brave", settings.SearchEngine);
            Assert.True(store.TrySave(settings));
            using var saved = JsonDocument.Parse(File.ReadAllText(path));
            Assert.False(saved.RootElement.TryGetProperty("accentColor", out _));
            Assert.Equal("Brave", store.Load().SearchEngine);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("#ff00ff")]
    [InlineData("red; background:url(https://example.com)")]
    public void RemovedAccentSettingCannotChangeTheme(string value)
    {
        var settings = new BrowserSettings { AppTheme = AppThemeMode.Light };
        Assert.False(SettingsChangePolicy.TryApply(settings, "accentColor", value, out var error));
        Assert.NotEmpty(error);
        Assert.Equal(AppThemeMode.Light, settings.AppTheme);
    }
}
