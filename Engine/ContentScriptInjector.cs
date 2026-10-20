using System.Diagnostics;
using System.Runtime.CompilerServices;
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

    public static bool IsTrustedExternalOrigin(string host) =>
        !string.IsNullOrEmpty(host) && _trustedExternalOrigins.Contains(host);

    /// <summary>
    /// The id WebView2 gave the registered YouTube bootstrap on each core.
    ///
    /// AddScriptToExecuteOnDocumentCreatedAsync freezes the string it is handed. The
    /// settings in that string are the ones from the moment the control was built, so
    /// without a record of the id there is no way to replace it when the user changes
    /// a setting, and no way to take it away when the feature is switched off. The
    /// entry dies with the core, so a closed or discarded tab leaves nothing behind,
    /// and a tab that moves between windows keeps the same core and the same record.
    /// </summary>
    private readonly ConditionalWeakTable<CoreWebView2, StrongBox<string?>> _youTubeScriptIds = new();

    public ContentScriptInjector(YouTubeUnhook youtubeUnhook)
    {
        _youtubeUnhook = youtubeUnhook;
    }

    /// <summary>
    /// Replaces the registered YouTube bootstrap on one core with one built from the
    /// settings as they are now, and removes it entirely when the feature is off.
    ///
    /// Every live core needs this, not only the ones already on YouTube: a core created
    /// while the feature was off has nothing registered, and a core created earlier
    /// carries settings that have since changed. Either way the next document it
    /// creates is the one that would run the stale text.
    /// </summary>
    public async Task UpdateYouTubeScriptAsync(CoreWebView2 core, BrowserSettings settings)
    {
        try
        {
            if (_youTubeScriptIds.TryGetValue(core, out var existing) && !string.IsNullOrEmpty(existing.Value))
            {
                core.RemoveScriptToExecuteOnDocumentCreated(existing.Value);
                existing.Value = null;
            }

            var script = _youtubeUnhook.GetScript(settings);
            if (string.IsNullOrEmpty(script)) return;

            var id = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            if (_youTubeScriptIds.TryGetValue(core, out var slot)) slot.Value = id;
            else _youTubeScriptIds.Add(core, new StrongBox<string?>(id));
        }
        catch (Exception ex)
        {
            // A core being torn down throws here. The next control built from the same
            // settings registers a correct bootstrap anyway, so this is not fatal.
            Trace.WriteLine($"ContentScriptInjector: YouTube bootstrap update failed: {ex.Message}");
        }
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
        {
            var id = await core.AddScriptToExecuteOnDocumentCreatedAsync(unhook);
            _youTubeScriptIds.Add(core, new StrongBox<string?>(id));
        }

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