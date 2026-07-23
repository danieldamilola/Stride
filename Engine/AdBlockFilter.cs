using Microsoft.Web.WebView2.Core;

namespace StrideBrowser.Engine;

/// <summary>
/// Network-level ad blocking — cancels known ad-serving requests before they load.
/// Extracted from <see cref="TabEngine"/> so the static block-list isn't mixed into
/// tab lifecycle logic.
/// </summary>
public static class AdBlockFilter
{
    // YouTube ad video/tracking URL patterns
    private static readonly string[] AdPatterns =
    [
        "*://*.doubleclick.net/*",
        "*://*.googlesyndication.com/*",
        "*://*.googleadservices.com/*",
        "*://*.google-analytics.com/*",
        "*://www.youtube.com/api/stats/ads*",
        "*://www.youtube.com/pagead/*",
        "*://www.youtube.com/get_midroll_info*",
        "*://*.youtube.com/ptracking*",
        "*://*.youtube.com/api/stats/qoe*",
        "*://yt3.ggpht.com/*/ads/*",
        "*://*.googleads.g.doubleclick.net/*",
        "*://ad.youtube.com/*",
        "*://ads.youtube.com/*",
        "*://*.moatads.com/*",
        "*://*.adsafeprotected.com/*"
    ];

    /// <summary>Registers request filters and blocks matching requests with an empty response.</summary>
    public static void Apply(CoreWebView2 core)
    {
        foreach (var pattern in AdPatterns)
            core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All);

        core.WebResourceRequested += (_, e) =>
        {
            e.Response = core.Environment.CreateWebResourceResponse(null, 200, "OK", "");
        };
    }
}
