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
    public void FloatingCommandBar_StartsOff()
    {
        // The floating bar breaks two finger trackpad swipe, so it is opt in.
        Assert.False(new BrowserSettings().UseFloatingCommandBar);
    }
}