using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Pages;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Optional Strict tracking prevention, with Balanced as the app default.
///
/// The unit tests here cover the persisted value, the rendered control and the engine
/// mapping. They do not claim that any real site works under Strict: Microsoft documents
/// that Strict "could cause some websites to not behave as expected", and confirming the
/// sites Stride users visit is a separate runtime walk.
/// </summary>
public sealed class TrackingPreventionTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("stride-tracking");

    public void Dispose()
    {
        try { _root.Delete(true); } catch { /* best effort */ }
    }

    private string SettingsFile(string name)
    {
        var dir = _root.CreateSubdirectory(name).FullName;
        return Path.Combine(dir, "settings.json");
    }


    [Fact]
    public void ANewProfileRunsBalanced()
    {
        Assert.Equal(TrackingPreventionMode.Balanced, new BrowserSettings().TrackingPreventionMode);
        Assert.Equal(CoreWebView2TrackingPreventionLevel.Balanced,
            TrackingPreventionModes.ToWebView2Level(TrackingPreventionModes.Default));
    }

    [Fact]
    public void ASettingsFileWrittenBeforeTheSettingExistedStillRunsBalanced()
    {
        var file = SettingsFile("old");
        File.WriteAllText(file, """{"searchEngine":"DuckDuckGo","defaultZoom":90}""");

        var loaded = new SettingsStore(file).Load();

        Assert.Equal(TrackingPreventionMode.Balanced, loaded.TrackingPreventionMode);
    }

    [Theory]
    [InlineData("\"trackingPrevention\": \"NotAMode\"")]
    [InlineData("\"trackingPrevention\": 7")]
    [InlineData("\"trackingPrevention\": null")]
    [InlineData("\"trackingPrevention\": 3")]
    public void AValueThatIsNotAModeFallsBackToBalancedWithoutLosingOtherPreferences(string pair)
    {
        var file = SettingsFile("bad-" + Math.Abs(pair.GetHashCode()));
        File.WriteAllText(file, "{" + pair + ",\"searchEngine\":\"Brave\",\"defaultZoom\":110}");

        var loaded = new SettingsStore(file).Load();

        Assert.Equal(TrackingPreventionMode.Balanced, loaded.TrackingPreventionMode);
        // The point of a tolerant read: one bad token must not discard the rest.
        Assert.Equal("Brave", loaded.SearchEngine);
        Assert.Equal(110, loaded.DefaultZoom);
    }

    [Theory]
    [InlineData("Balanced", TrackingPreventionMode.Balanced)]
    [InlineData("balanced", TrackingPreventionMode.Balanced)]
    [InlineData(" strict ", TrackingPreventionMode.Strict)]
    [InlineData("STRICT", TrackingPreventionMode.Strict)]
    public void AValueFromTheSettingsPageIsParsed(string value, TrackingPreventionMode expected)
    {
        Assert.Equal(expected, TrackingPreventionModes.Parse(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("basic")]
    [InlineData("off")]
    [InlineData("0")]
    public void AnythingOutsideTheTwoOfferedModesBecomesBalanced(string? value)
    {
        // There is no Off choice to fall into. A value naming one is not honoured.
        Assert.Equal(TrackingPreventionMode.Balanced, TrackingPreventionModes.Parse(value));
    }


    [Theory]
    [InlineData(TrackingPreventionMode.Balanced)]
    [InlineData(TrackingPreventionMode.Strict)]
    public void TheChoiceSurvivesARestart(TrackingPreventionMode mode)
    {
        var file = SettingsFile("roundtrip-" + mode);

        var saved = new BrowserSettings { TrackingPreventionMode = mode };
        Assert.True(new SettingsStore(file).TrySave(saved));
        var loaded = new SettingsStore(file).Load();

        Assert.Equal(mode, loaded.TrackingPreventionMode);
    }

    [Fact]
    public void TheStoredFormIsTheReadableName()
    {
        var file = SettingsFile("shape");

        new SettingsStore(file).TrySave(new BrowserSettings { TrackingPreventionMode = TrackingPreventionMode.Strict });

        var json = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
        Assert.Equal("Strict", json.GetProperty("trackingPreventionMode").GetString());
    }

    [Fact]
    public void ResetGoesBackToBalanced()
    {
        var settings = new BrowserSettings { TrackingPreventionMode = TrackingPreventionMode.Strict };

        settings.ResetToDefaults();

        Assert.Equal(TrackingPreventionMode.Balanced, settings.TrackingPreventionMode);
    }

    [Fact]
    public void ResetDoesNotLookLikeAChangeWhenTheValueIsAlreadyBalanced()
    {
        // The engine only follows a change that was raised. A reset that reports one for
        // an unchanged value would be noise in every window.
        var settings = new BrowserSettings { TrackingPreventionMode = TrackingPreventionMode.Balanced };
        var raised = 0;
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowserSettings.TrackingPreventionMode)) raised++;
        };

        settings.ResetToDefaults();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void RaisingThePropertyIsWhatSignalsTheEngines()
    {
        // BrowserSettings is a process singleton every window shares, so its change
        // notification is the only thing that carries the choice to windows the user is
        // not looking at.
        var settings = new BrowserSettings();
        var raised = 0;
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowserSettings.TrackingPreventionMode)) raised++;
        };

        settings.TrackingPreventionMode = TrackingPreventionMode.Strict;
        settings.TrackingPreventionMode = TrackingPreventionMode.Strict;
        settings.TrackingPreventionMode = TrackingPreventionMode.Balanced;

        Assert.Equal(2, raised);
    }


    [Theory]
    [InlineData(TrackingPreventionMode.Balanced)]
    [InlineData(TrackingPreventionMode.Strict)]
    public void TheSettingsPageShowsExactlyTheTwoChoicesWithTheCurrentOneSelected(TrackingPreventionMode mode)
    {
        var html = new SettingsPage().Render(new BrowserSettings { TrackingPreventionMode = mode }, "test-token");

        Assert.Contains("onchange=\"s('trackingPrevention', this)\"", html);
        Assert.Contains("<option value=\"balanced\"", html);
        Assert.Contains("<option value=\"strict\"", html);
        // No third option, and no way to turn protection off from here.
        Assert.Equal(2, CountOccurrences(html, "<option value=\"balanced\"") + CountOccurrences(html, "<option value=\"strict\""));
        Assert.Contains($"value=\"balanced\"{(mode == TrackingPreventionMode.Balanced ? " selected" : "")}", html);
        Assert.Contains($"value=\"strict\"{(mode == TrackingPreventionMode.Strict ? " selected" : "")}", html);
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public void TheSettingsPageWarnsThatStrictCanBreakSites()
    {
        var html = new SettingsPage().Render(new BrowserSettings(), "test-token");

        Assert.Contains("behave unexpectedly", html);
        Assert.Contains("Tracking prevention", html);
    }

    [Fact]
    public void ThereIsExactlyOneFaviconToggleAndItStillBinds()
    {
        // The migration changed what the toggle means, not its wiring, so the existing
        // switch is kept rather than duplicated.
        var html = new SettingsPage().Render(new BrowserSettings { FaviconProxyEnabled = true }, "test-token");

        Assert.Equal(1, CountOccurrences(html, "s('faviconProxy', this)"));
        Assert.Equal(1, CountOccurrences(html, "checked onchange=\"s('faviconProxy', this)\""));
    }


    [Theory]
    [InlineData(TrackingPreventionMode.Balanced, CoreWebView2TrackingPreventionLevel.Balanced)]
    [InlineData(TrackingPreventionMode.Strict, CoreWebView2TrackingPreventionLevel.Strict)]
    public void EachModeMapsToItsEngineLevel(TrackingPreventionMode mode, CoreWebView2TrackingPreventionLevel expected)
    {
        Assert.Equal(expected, TrackingPreventionModes.ToWebView2Level(mode));
    }

    [Fact]
    public void TheProductionEnvironmentLeavesTrackingPreventionOn()
    {
        // The environment option is what makes the per-profile level apply at all. It is
        // on for both modes and no setting turns it off.
        Assert.True(Engine.WebViewFactory.BuildEnvironmentOptions(new BrowserSettings()).EnableTrackingPrevention);
        Assert.True(Engine.WebViewFactory.BuildEnvironmentOptions(
            new BrowserSettings { TrackingPreventionMode = TrackingPreventionMode.Strict }).EnableTrackingPrevention);
    }

    [Fact]
    public void TheApplicationSourceWritesTheLevelOnEveryControlItConfigures()
    {
        // ConfigureCoreWebView is what a newly created control runs through, and it is
        // where the level has to be written because the engine persists the last value per
        // profile. Without that write a profile that once ran Strict would keep Strict
        // behind a setting that says Balanced.
        var source = File.ReadAllText(LocateTabEngineSource());
        var factory = File.ReadAllText(LocateFactorySource());

        Assert.Contains("ApplyTrackingPreventionLevel(core)", factory);
        Assert.Contains("PreferredTrackingPreventionLevel = level", factory);
        Assert.Contains("ApplyTrackingPreventionToLiveProfiles", source);
    }

    [Fact]
    public void TheEngineUnsubscribesFromSettingsWhenItIsDisposed()
    {
        // BrowserSettings outlives every window. An engine that stayed subscribed would be
        // called for profiles it no longer owns, and torn-off windows come and go.
        var source = File.ReadAllText(LocateTabEngineSource());

        Assert.Contains("_settings.PropertyChanged += OnSettingsPropertyChanged", source);
        Assert.Contains("_settings.PropertyChanged -= OnSettingsPropertyChanged", source);
    }

    [Fact]
    public void TheSettingRouteAcceptsTheTwoValuesAndSurvivesAnythingElse()
    {
        // The setter table is private, so it is reached the way the router reaches it.
        var setters = SettingSetters();
        Assert.True(setters.TryGetValue("trackingPrevention", out var found));
        var setter = found!;

        var accepted = new BrowserSettings();
        setter(accepted, "strict");
        Assert.Equal(TrackingPreventionMode.Strict, accepted.TrackingPreventionMode);

        var rejected = new BrowserSettings();
        setter(rejected, "<script>");
        Assert.Equal(TrackingPreventionMode.Balanced, rejected.TrackingPreventionMode);
    }

    [Fact]
    public void TheSettingIsAllowedOnThePagesThatPostSettings()
    {
        // The settings page reaches it through the setting prefix. A page without that
        // permission still cannot change it, and the new key must not have widened any
        // other route.
        Assert.True(Engine.IpcSecurityPolicy.IsRouteAllowedForPage(
            Engine.TabEngine.InternalPageKind.Settings, "setting:trackingPrevention:strict"));
        Assert.False(Engine.IpcSecurityPolicy.IsRouteAllowedForPage(
            Engine.TabEngine.InternalPageKind.History, "setting:trackingPrevention:strict"));
        Assert.False(Engine.IpcSecurityPolicy.IsRouteAllowedForPage(
            Engine.TabEngine.InternalPageKind.Downloads, "setting:trackingPrevention:strict"));
    }

    private static Dictionary<string, Action<BrowserSettings, string>> SettingSetters()
    {
        return typeof(Engine.Handlers.SettingsMessageHandler)
            .GetField("SettingSetters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as Dictionary<string, Action<BrowserSettings, string>>
            ?? throw new InvalidOperationException("the setting setter table was not found");
    }

    private static string LocateFactorySource()
    {
        var candidate = FindUp("Engine", "WebViewFactory.cs");
        Assert.NotNull(candidate);
        return candidate!;
    }

    private static string LocateTabEngineSource()
    {
        var candidate = FindUp("Engine", "TabEngine.cs");
        Assert.NotNull(candidate);
        return candidate!;
    }

    /// <summary>Walks up from the test output folder to the repository root.</summary>
    private static string? FindUp(string folder, string file)
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, folder, file);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
