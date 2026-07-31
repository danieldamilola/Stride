using Microsoft.Web.WebView2.Core;
using StrideBrowser.Helpers;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine;

/// <summary>
/// Injects per-tab content scripts (YouTube ad-nuke, enhancer, unhook, force-dark-mode)
/// at document-created time, based on current settings. Extracted from <see cref="TabEngine"/>
/// to isolate script-injection policy from tab lifecycle management.
/// </summary>
public sealed class ContentScriptInjector
{
    private readonly YouTubeEnhancer _youtubeEnhancer;
    private readonly YouTubeUnhook _youtubeUnhook;

    public ContentScriptInjector(YouTubeEnhancer youtubeEnhancer, YouTubeUnhook youtubeUnhook)
    {
        _youtubeEnhancer = youtubeEnhancer;
        _youtubeUnhook = youtubeUnhook;
    }

    public async Task InjectAsync(CoreWebView2 core, BrowserSettings settings)
    {
        // YouTube ad nuker — injected first so ads are killed before anything else
        if (settings.AdBlockEnabled)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                ResourceLoader.Load("Resources.Scripts.youtube-adnuke.js"));
        }

        // YouTube enhancer (quality, speed, loop — self-guards)
        var enhancer = _youtubeEnhancer.GetScript(settings);
        if (!string.IsNullOrEmpty(enhancer))
            await core.AddScriptToExecuteOnDocumentCreatedAsync(enhancer);

        // YouTube unhook (hide distractions — self-guards)
        var unhook = _youtubeUnhook.GetScript(settings);
        if (!string.IsNullOrEmpty(unhook))
            await core.AddScriptToExecuteOnDocumentCreatedAsync(unhook);

    }
}
