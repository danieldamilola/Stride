using Microsoft.Web.WebView2.Core;
using StrideBrowser.Helpers;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine;

public sealed class ContentScriptInjector
{
    private readonly YouTubeUnhook _youtubeUnhook;

    private static readonly HashSet<string> _trustedExternalOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "wallhaven.cc",
        "www.wallhaven.cc",
    };

    public ContentScriptInjector(YouTubeUnhook youtubeUnhook)
    {
        _youtubeUnhook = youtubeUnhook;
    }

    /// <remarks>
    /// The ipcToken parameter is kept for call-site compatibility and deliberately unused.
    /// Injected scripts run in the page's world, so any value substituted into them is
    /// readable by page script. The token now stays in the binary and in the internal-page
    /// templates, which are loaded through NavigateToString and never exposed to a remote page.
    /// </remarks>
    public async Task InjectAsync(CoreWebView2 core, BrowserSettings settings, string ipcToken)
    {
        // YouTube Unhook is injected. The Shorts and sidebar features were
        // removed from it because they broke scrolling; see YouTubeUnhook.cs.
        // The enhancer and adnuke scripts stay parked in Parked/youtube.
        var unhook = _youtubeUnhook.GetScript(settings);
        if (!string.IsNullOrEmpty(unhook))
            await core.AddScriptToExecuteOnDocumentCreatedAsync(unhook);

        // Wallhaven Direct Downloader.
        //
        // The token parameter is deliberately unused for the injected scripts. It used to be
        // substituted into wallhaven-token.js as a global called window.__T, so any script
        // running on an allowlisted origin could read the session token and then drive every
        // privileged route. The scripts below carry no secret now: the bridge authorises the
        // wallhaven download by checking the message source origin, and the other two scripts
        // send values the sending page already knows.
        var hostsJson = string.Join(", ", _trustedExternalOrigins.Select(h => $"\"{h}\""));
        var hostGateScript = ResourceLoader.Load("Resources.Scripts.wallhaven-token.js")
            .Replace("__ALLOWED_HOSTS__", hostsJson);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(hostGateScript);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            ResourceLoader.Load("Resources.Scripts.wallhaven-downloader.js"));

        // Adaptive Theme Color Extractor (Ported from Adaptive-Tab-Bar-Colour Extension)
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            ResourceLoader.Load("Resources.Scripts.theme-color.js"));

        // Link Preview - Alt plus click, on demand, no background timer
        if (settings.LinkPreviewEnabled)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                ResourceLoader.Load("Resources.Scripts.link-preview.js"));
        }
    }
}