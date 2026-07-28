using System.Globalization;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// Generates JavaScript that enhances the YouTube viewing experience.
/// The script template lives in <c>Resources/Scripts/youtube-enhancer.js</c>;
/// this service injects the user's settings via placeholder substitution.
/// </summary>
public sealed class YouTubeEnhancer
{
    private const string TemplatePath = "Resources.Scripts.youtube-enhancer.js";

    /// <summary>Returns the JS to inject on youtube.com pages.</summary>
    public string GetScript(BrowserSettings settings)
    {
        if (!settings.YtEnhancerEnabled) return "";

        var replacements = new Dictionary<string, string>
        {
            ["QUALITY"]         = settings.YtDefaultQuality ?? "auto",
            ["SPEED"]           = settings.YtDefaultSpeed.ToString(CultureInfo.InvariantCulture),
            ["DISABLE_AUTO"]    = settings.YtDisableAutoplay ? "true" : "false",
            ["PAUSE_ON_SWITCH"] = settings.YtPauseOnTabSwitch ? "true" : "false",
            ["LOOP_VIDEO"]      = settings.YtLoopVideo ? "true" : "false",
        };

        return ResourceLoader.LoadTemplate(TemplatePath, replacements);
    }
}
