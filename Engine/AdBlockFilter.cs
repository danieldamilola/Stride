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
        "*://*.adsafeprotected.com/*",
        // Generic ad networks used by streaming sites
        "*://*.popads.net/*",
        "*://*.popcash.net/*",
        "*://*.adsterra.com/*",
        "*://*.juicyads.com/*",
        "*://*.exoclick.com/*",
        "*://*.trafficjunky.com/*",
        "*://*.propellerads.com/*",
        "*://*.pushame.com/*",
        "*://*.monetag.com/*",
        "*://*.a-ads.com/*",
        "*://*.hilltopads.net/*",
        "*://*.clickadu.com/*",
        "*://*.onclicka.com/*",
        "*://*.onclickmax.com/*",
        "*://*.tsyndicate.com/*",
        "*://*.bidgear.com/*",
        "*://*.pushails.com/*",
        "*://*.dolohen.com/*",
        "*://*.betterads.org/*",
        "*://*.revenuehits.com/*",
        "*://*.mgid.com/*",
        "*://*.revcontent.com/*",
        "*://*.taboola.com/*",
        "*://*.outbrain.com/*",
        "*://*.zedo.com/*",
        "*://*.adcolony.com/*",
        "*://*.serve.popads.net/*",
        "*://*.ad.atdmt.com/*",
        "*://*.adnxs.com/*",
        "*://*.advertising.com/*",
        "*://*.rubiconproject.com/*",
        "*://*.pubmatic.com/*",
        "*://*.openx.net/*",
        "*://*.criteo.com/*",
        "*://*.smartadserver.com/*",
        "*://*.turn.com/*",
        "*://*.casalemedia.com/*",
        "*://*.3lift.com/*",
        "*://*.districtm.io/*",
        "*://*.media.net/*"
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
