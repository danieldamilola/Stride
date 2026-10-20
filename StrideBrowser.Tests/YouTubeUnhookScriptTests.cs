using System.Text.Json;
using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Covers the JavaScript YouTubeUnhook generates, which is the only part of the
/// unhook feature that runs on a YouTube page. The script's own behaviour against a
/// real page is covered by the executable replay in
/// tools/RecoveryVerification/YouTubeFeatures/js, which runs the exact text these
/// tests generate. What belongs here is the contract between the settings and that
/// text, because a wrong contract here produces a page that silently ignores the
/// user's choices and nothing else catches it.
/// </summary>
public class YouTubeUnhookScriptTests
{
    private const string StorageKey = "__stride_unhook";

    private static JsonElement ConfigFrom(string script)
    {
        var marker = $"localStorage.setItem('{StorageKey}', '";
        var start = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "the generated script does not write the unhook config");

        // The value is a JS single quoted string, so the JSON ends at the next
        // unescaped single quote. No key or value can contain one today, and the
        // generator would need to escape it if that ever changed.
        var valueStart = start + marker.Length;
        var end = script.IndexOf('\'', valueStart);
        Assert.True(end > valueStart, "the generated config value is empty");

        var json = script[valueStart..end].Replace("\\'", "'");
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>
    /// The config the generated script hands the document when the page refuses the
    /// write. It is a bare JS object literal, so it ends at the brace that closes the
    /// value rather than at a quote.
    /// </summary>
    private static JsonElement FallbackConfigFrom(string script)
    {
        const string marker = "window.__STRIDE_UNHOOK = ";
        var start = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "the generated script has no fallback for a refused write");

        var braceStart = start + marker.Length;
        Assert.Equal('{', script[braceStart]);

        var depth = 0;
        for (var i = braceStart; i < script.Length; i++)
        {
            if (script[i] == '{') depth++;
            else if (script[i] == '}' && --depth == 0)
                return JsonDocument.Parse(script[braceStart..(i + 1)]).RootElement.Clone();
        }

        throw new Xunit.Sdk.XunitException("the fallback config object is not closed");
    }

    [Fact]
    public void Disabled_ReturnsNoScriptAtAll()
    {
        var unhook = new YouTubeUnhook();
        Assert.Equal("", unhook.GetScript(new BrowserSettings { UnhookEnabled = false }));
        Assert.Equal("", unhook.GetScript(new BrowserSettings { UnhookEnabled = false }, forceConfig: true));
    }

    [Fact]
    public void ShippedDefaults_AreCarriedIntoTheConfig()
    {
        var config = ConfigFrom(new YouTubeUnhook().GetScript(new BrowserSettings()));

        // These ship on, so the page must be told to hide them.
        Assert.True(config.GetProperty("sidebar").GetBoolean());
        Assert.True(config.GetProperty("comments").GetBoolean());
        Assert.True(config.GetProperty("shorts").GetBoolean());
        Assert.True(config.GetProperty("moreYT").GetBoolean());
        Assert.True(config.GetProperty("autoplay").GetBoolean());

        // These ship off.
        Assert.False(config.GetProperty("homeFeed").GetBoolean());
        Assert.False(config.GetProperty("videoInfo").GetBoolean());
        Assert.False(config.GetProperty("topHeader").GetBoolean());
        Assert.False(config.GetProperty("subscriptions").GetBoolean());
    }

    [Fact]
    public void Sidebar_TakesEitherSettingThatMeansTheSidebar()
    {
        var unhook = new YouTubeUnhook();
        var neither = ConfigFrom(unhook.GetScript(new BrowserSettings
        {
            UnhookHideVideoSidebar = false,
            UnhookHideRecommended = false
        }));
        Assert.False(neither.GetProperty("sidebar").GetBoolean());

        var legacyOnly = ConfigFrom(unhook.GetScript(new BrowserSettings
        {
            UnhookHideVideoSidebar = false,
            UnhookHideRecommended = true
        }));
        Assert.True(legacyOnly.GetProperty("sidebar").GetBoolean());
    }

    /// <summary>
    /// The bootstrap has to write the settings it is given, not leave whatever an
    /// earlier visit left behind. A settings change made while no YouTube tab is open
    /// has no page to re-inject into, so seeding only when the key is missing carried
    /// the previous preferences into every later visit.
    /// </summary>
    [Fact]
    public void InitialInjection_AlwaysWritesTheConfig()
    {
        var script = new YouTubeUnhook().GetScript(new BrowserSettings());

        Assert.DoesNotContain("if (!localStorage.getItem", script, StringComparison.Ordinal);
        Assert.Contains($"localStorage.setItem('{StorageKey}'", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// A page that refuses the write still has to be handed the settings, so the
    /// fallback has to carry the config rather than something else or nothing.
    /// </summary>
    [Fact]
    public void RefusedStorage_FallsBackToTheConfigItWouldHaveStored()
    {
        var unhook = new YouTubeUnhook();
        var settings = new BrowserSettings { UnhookHideComments = false };

        var stored = ConfigFrom(unhook.GetScript(settings));
        var fallback = FallbackConfigFrom(unhook.GetScript(settings));

        Assert.Equal(
            JsonSerializer.Serialize(stored),
            JsonSerializer.Serialize(fallback));
        Assert.False(fallback.GetProperty("comments").GetBoolean());
        Assert.True(fallback.GetProperty("sidebar").GetBoolean());
    }

    [Fact]
    public void LiveReload_CarriesTheChangedValues()
    {
        var config = ConfigFrom(new YouTubeUnhook().GetScript(
            new BrowserSettings { UnhookHideComments = false, UnhookHideShorts = false },
            forceConfig: true));

        Assert.False(config.GetProperty("comments").GetBoolean());
        Assert.False(config.GetProperty("shorts").GetBoolean());
        Assert.True(config.GetProperty("sidebar").GetBoolean());
    }

    /// <summary>
    /// The script only gates CSS classes it has a rule for, so every feature it can
    /// toggle has to be a key the generator emits. A key the script never receives
    /// would leave the corresponding class switched on with nothing behind it.
    /// </summary>
    [Theory]
    [InlineData("homeFeed")]
    [InlineData("shorts")]
    [InlineData("mixes")]
    [InlineData("explore")]
    [InlineData("subscriptions")]
    [InlineData("sidebar")]
    [InlineData("comments")]
    [InlineData("videoInfo")]
    [InlineData("liveChat")]
    [InlineData("playlist")]
    [InlineData("merch")]
    [InlineData("fundraiser")]
    [InlineData("endFeed")]
    [InlineData("endCards")]
    [InlineData("annotations")]
    [InlineData("topHeader")]
    [InlineData("notifications")]
    [InlineData("moreYT")]
    [InlineData("inaptSearch")]
    public void EveryFeatureTheScriptToggles_IsCarriedIntoTheConfig(string feature)
    {
        var config = ConfigFrom(new YouTubeUnhook().GetScript(new BrowserSettings()));
        Assert.True(config.TryGetProperty(feature, out _), $"the config has no '{feature}' key");
    }

    [Fact]
    public void AutoplaySetting_IsCarriedSeparately()
    {
        var off = ConfigFrom(new YouTubeUnhook().GetScript(new BrowserSettings { UnhookDisableAutoplay = false }));
        Assert.False(off.GetProperty("autoplay").GetBoolean());
    }
}