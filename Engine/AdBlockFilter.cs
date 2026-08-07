using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace StrideBrowser.Engine;

/// <summary>
/// Network-level ad blocking — cancels known ad-serving requests before they load.
/// Downloads and caches a comprehensive blocklist (e.g. Peter Lowe's) for robust blocking.
/// </summary>
public static class AdBlockFilter
{
    private static HashSet<string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase);
    private static bool _isInitialized = false;
    
    // Fallback list of aggressive popup/popunder networks to ensure they are blocked
    // even if the downloaded list doesn't have them or fails to download.
    private static readonly string[] FallbackDomains =
    [
        "popads.net", "popcash.net", "adsterra.com", "juicyads.com", "exoclick.com", 
        "trafficjunky.com", "propellerads.com", "pushame.com", "monetag.com", "a-ads.com", 
        "hilltopads.net", "clickadu.com", "onclicka.com", "onclickmax.com", "tsyndicate.com", 
        "bidgear.com", "pushails.com", "dolohen.com", "betterads.org", "revenuehits.com", 
        "mgid.com", "revcontent.com", "taboola.com", "outbrain.com", "zedo.com", 
        "adcolony.com", "serve.popads.net", "ad.atdmt.com", "adnxs.com", "advertising.com", 
        "rubiconproject.com", "pubmatic.com", "openx.net", "criteo.com", "smartadserver.com", 
        "turn.com", "casalemedia.com", "3lift.com", "districtm.io", "media.net",
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "google-analytics.com",
        "moatads.com", "adsafeprotected.com", "ad.youtube.com", "ads.youtube.com"
    ];

    public static async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        foreach (var d in FallbackDomains)
            _blockedDomains.Add(d);

        try
        {
            string cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrideBrowser", "adblock_domains.txt");
            
            // Re-download list if older than 7 days, otherwise use cache
            if (!File.Exists(cachePath) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath)).TotalDays > 7)
            {
                var dir = Path.GetDirectoryName(cachePath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                
                // Peter Lowe's Ad and tracking server list (plaintext, one domain per line)
                string listData = await client.GetStringAsync("https://pgl.yoyo.org/adservers/serverlist.php?hostformat=nohtml&showintro=0&mimetype=plaintext");
                
                await File.WriteAllTextAsync(cachePath, listData);
            }

            if (File.Exists(cachePath))
            {
                var lines = await File.ReadAllLinesAsync(cachePath);
                foreach (var line in lines)
                {
                    string domain = line.Trim();
                    if (!string.IsNullOrEmpty(domain) && !domain.StartsWith("#"))
                        _blockedDomains.Add(domain);
                }
            }
            
            Trace.WriteLine($"AdBlockFilter initialized with {_blockedDomains.Count} blocked domains.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to initialize comprehensive AdBlock list: {ex.Message}");
        }
    }

    /// <summary>Registers request filters and blocks matching requests with an empty response.</summary>
    public static void Apply(CoreWebView2 core)
    {
        // Intercept ALL requests
        core.AddWebResourceRequestedFilter("*://*/*", CoreWebView2WebResourceContext.All);

        core.WebResourceRequested += (_, e) =>
        {
            try
            {
                if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri))
                {
                    string host = uri.Host;
                    
                    // Check if host or any of its parent domains are in the blocklist
                    // e.g. "ad.server.com" -> check "ad.server.com", then "server.com"
                    string[] parts = host.Split('.');
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        string domainToCheck = string.Join(".", parts, i, parts.Length - i);
                        if (_blockedDomains.Contains(domainToCheck))
                        {
                            // Block the request
                            e.Response = core.Environment.CreateWebResourceResponse(null, 200, "OK", "");
                            return;
                        }
                    }
                    
                    // Additional hardcoded Youtube ad paths since YT serves ads from same domain
                    if (host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = uri.AbsolutePath.ToLowerInvariant();
                        if (path.StartsWith("/api/stats/ads") || 
                            path.StartsWith("/pagead/") || 
                            path.StartsWith("/get_midroll_info") || 
                            path.StartsWith("/ptracking") || 
                            path.StartsWith("/api/stats/qoe"))
                        {
                            e.Response = core.Environment.CreateWebResourceResponse(null, 200, "OK", "");
                            return;
                        }
                    }
                    if (host.EndsWith("yt3.ggpht.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.Contains("/ads/"))
                    {
                        e.Response = core.Environment.CreateWebResourceResponse(null, 200, "OK", "");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AdBlockFilter error: {ex.Message}");
            }
        };
    }
}
