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

    public async Task InjectAsync(CoreWebView2 core, BrowserSettings settings, string ipcToken)
    {
        // YouTube Unhook is injected. The Shorts and sidebar features were
        // removed from it because they broke scrolling; see YouTubeUnhook.cs.
        // The enhancer and adnuke scripts stay parked in Parked/youtube.
        var unhook = _youtubeUnhook.GetScript(settings);
        if (!string.IsNullOrEmpty(unhook))
            await core.AddScriptToExecuteOnDocumentCreatedAsync(unhook);

        // Wallhaven Direct Downloader
        var hostsJson = string.Join(", ", _trustedExternalOrigins.Select(h => $"\"{h}\""));
        var tokenScript = ResourceLoader.Load("Resources.Scripts.wallhaven-token.js")
            .Replace("__ALLOWED_HOSTS__", hostsJson)
            .Replace("__IPC_TOKEN__", ipcToken);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(tokenScript);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            ResourceLoader.Load("Resources.Scripts.wallhaven-downloader.js"));

        // Adaptive Theme Color Extractor (Ported from Adaptive-Tab-Bar-Colour Extension)
        // The token lets the bridge authenticate the posts against arbitrary page spoofing.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            ResourceLoader.Load("Resources.Scripts.theme-color.js")
                .Replace("__STRIDE_IPC_TOKEN__", ipcToken));

        // Link Preview - Alt plus click, on demand, no background timer
        if (settings.LinkPreviewEnabled)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                ResourceLoader.Load("Resources.Scripts.link-preview.js")
                    .Replace("__STRIDE_IPC_TOKEN__", ipcToken));
        }
    }
}