using System.Text.Json;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// Generates JavaScript that enhances the YouTube viewing experience.
/// The script template lives in <c>Resources/Scripts/youtube-enhancer.js</c>;
/// this service serializes the user's settings into a config object the
/// template reads at runtime.
/// </summary>
public sealed class YouTubeEnhancer
{
    private const string TemplatePath = "Resources.Scripts.youtube-enhancer.js";
    private const string StorageKey = "__stride_yt_enhancer";

    /// <summary>Quality values YouTube's player API accepts plus our meta-values.</summary>
    private static readonly HashSet<string> ValidQualities = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto", "highest", "lowest", "highres", "hd2160", "hd1440",
        "hd1080", "hd720", "large", "medium", "small", "tiny",
    };

    /// <summary>
    /// Maps unknown or missing quality values to auto so nothing unvalidated
    /// from settings can reach the generated JavaScript.
    /// </summary>
    public static string NormalizeQuality(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality)) return "auto";
        var normalized = quality.Trim().ToLowerInvariant();
        return ValidQualities.Contains(normalized) ? normalized : "auto";
    }

    /// <summary>
    /// Returns the config line plus JS to inject on youtube.com pages.
    /// Returns empty string when the enhancer is disabled.
    /// <paramref name="forceConfig"/> overwrites the persisted config for live
    /// reload; initial injection only seeds it so live changes survive
    /// navigation instead of being reverted by the registered bootstrap.
    /// </summary>
    public string GetScript(BrowserSettings settings, bool forceConfig = false)
    {
        if (!settings.YtEnhancerEnabled) return "";

        var config = new
        {
            enabled = true,
            quality = NormalizeQuality(settings.YtDefaultQuality),
            speed = settings.YtDefaultSpeed,
            disableAuto = settings.YtDisableAutoplay,
            pauseOnSwitch = settings.YtPauseOnTabSwitch,
            loop = settings.YtLoopVideo,
        };
        var json = JsonSerializer.Serialize(config);

        // Config persists in the page's localStorage so live-reloaded values
        // survive SPA navigations. Initial injection only seeds it.
        var seed = forceConfig ? "" : "if (!localStorage.getItem('__stride_yt_enhancer')) ";
        var configLine =
            "try { " + seed + "localStorage.setItem('" + StorageKey + "', '" + json + "'); } catch (e) {}";

        // Live reload also points the running document at the new config.
        if (forceConfig)
            configLine += " window.__STRIDE_YT_CONFIG = " + json + ";";

        return configLine + "\n" + ResourceLoader.Load(TemplatePath);
    }
}
