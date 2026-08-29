using Microsoft.Web.WebView2.Core;
using StrideBrowser.Helpers;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine;

public sealed class ContentScriptInjector
{
    private readonly YouTubeEnhancer _youtubeEnhancer;
    private readonly YouTubeUnhook _youtubeUnhook;

    private static readonly HashSet<string> _trustedExternalOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "wallhaven.cc",
        "www.wallhaven.cc",
    };

    public ContentScriptInjector(YouTubeEnhancer youtubeEnhancer, YouTubeUnhook youtubeUnhook)
    {
        _youtubeEnhancer = youtubeEnhancer;
        _youtubeUnhook = youtubeUnhook;
    }

    public async Task InjectAsync(CoreWebView2 core, BrowserSettings settings, string ipcToken)
    {
        if (settings.AdBlockEnabled)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                ResourceLoader.Load("Resources.Scripts.youtube-adnuke.js"));
        }

        var enhancer = _youtubeEnhancer.GetScript(settings);
        if (!string.IsNullOrEmpty(enhancer))
            await core.AddScriptToExecuteOnDocumentCreatedAsync(enhancer);

        // Wallhaven Direct Downloader
        var hostsJson = string.Join(", ", _trustedExternalOrigins.Select(h => $"\"{h}\""));
        var tokenScript = ResourceLoader.Load("Resources.Scripts.wallhaven-token.js")
            .Replace("__ALLOWED_HOSTS__", hostsJson)
            .Replace("__IPC_TOKEN__", ipcToken);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(tokenScript);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            ResourceLoader.Load("Resources.Scripts.wallhaven-downloader.js"));

        var unhook = _youtubeUnhook.GetScript(settings);
        if (!string.IsNullOrEmpty(unhook))
            await core.AddScriptToExecuteOnDocumentCreatedAsync(unhook);

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