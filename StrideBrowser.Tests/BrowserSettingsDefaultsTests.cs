using System.Text.Json;
using StrideBrowser.Models;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// The toolbar layout defaults a new profile starts with. These are the values a
/// first run gets before anything is written to settings.json, so a wrong default
/// ships to every new user and only shows up as "the toolbar looks wrong".
/// </summary>
public sealed class BrowserSettingsDefaultsTests
{
    [Fact]
    public void AddressBar_StartsOnTheLeft()
    {
        Assert.True(new BrowserSettings().AddressBarOnLeft);
    }

    [Fact]
    public void AdaptiveTint_StaysOn()
    {
        Assert.True(new BrowserSettings().AdaptiveToolbarTint);
    }

    [Fact]
    public void RoundedWebContentFrame_StaysOn()
    {
        Assert.True(new BrowserSettings().RoundedWebContentFrameEnabled);
    }

    [Fact]
    public void RoundedWebContentFrame_ResetTurnsItBackOn()
    {
        var settings = new BrowserSettings { RoundedWebContentFrameEnabled = false };

        settings.ResetToDefaults();

        Assert.True(settings.RoundedWebContentFrameEnabled);
    }

    [Fact]
    public void RoundedWebContentFrame_MissingFromSavedJson_KeepsTheDefault()
    {
        // A settings.json written before the frame existed has no key for it. The
        // deserializer runs the property initializer and only overwrites keys that
        // are present, so an existing profile still gets the frame.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        var loaded = JsonSerializer.Deserialize<BrowserSettings>("""{"adaptiveToolbarTint":false}""", options);

        Assert.NotNull(loaded);
        Assert.True(loaded!.RoundedWebContentFrameEnabled);
    }
}
