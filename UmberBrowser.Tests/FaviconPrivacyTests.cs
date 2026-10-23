using System.IO;
using System.Net;
using System.Net.Http;
using UmberBrowser.Models;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// The favicon privacy boundary, driven through the production loader and the production
/// policy rather than a re-enactment of them.
///
/// The loader is built with a private cache folder and a fake HTTP handler, so these run
/// against real files on disk and a real request path with no network and no WPF
/// Application. That matters for the zero-request cases: the send path no longer depends
/// on a running Application, so a request that should happen does happen here, and a
/// zero-request assertion means the gate stopped it rather than a missing decode step.
/// </summary>
public sealed class FaviconPrivacyTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("umber-favicon");

    public void Dispose()
    {
        try { _root.Delete(true); } catch { /* best effort */ }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        public RecordingHandler(byte[]? body = null) => _body = body ?? new byte[] { 1, 2, 3, 4 };

        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_body)
            });
        }
    }

    private static byte[] SamplePng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private (FaviconLoader Loader, RecordingHandler Http) NewLoader(BrowserSettings settings, string? cacheDir = null)
    {
        var handler = new RecordingHandler(SamplePng());
        return (new FaviconLoader(settings, new HttpClient(handler), cacheDir ?? _root.FullName), handler);
    }

    // ── The settings file decides whether anything leaves the machine ──────────────────

    [Fact]
    public void ANewProfileStartsWithTheThirdPartyFallbackOff()
    {
        var settings = new BrowserSettings();

        Assert.False(settings.FaviconProxyEnabled);
        Assert.Equal(BrowserSettings.CurrentSchemaVersion, settings.SettingsSchemaVersion);
    }

    [Fact]
    public void ResetTurnsTheThirdPartyFallbackBackOff()
    {
        var settings = new BrowserSettings { FaviconProxyEnabled = true };

        settings.ResetToDefaults();

        Assert.False(settings.FaviconProxyEnabled);
    }

    [Fact]
    public void ASettingsFileFromTheDefaultOnEraIsMigratedToOffAndTheVersionIsPersisted()
    {
        // What a file written by the previous build looks like: no schema version, and a
        // saved true that came from the property's old default rather than from a choice.
        var dir = _root.CreateSubdirectory("legacy-profile").FullName;
        var file = Path.Combine(dir, "settings.json");
        Directory.CreateDirectory(dir);
        File.WriteAllText(file, """
            {
              "searchEngine": "Brave",
              "defaultZoom": 125,
              "accentColor": "#ff8800",
              "faviconProxyEnabled": true
            }
            """);

        var store = new SettingsStore(file);
        var loaded = store.Load();

        Assert.False(loaded.FaviconProxyEnabled);
        // Nothing else was touched.
        Assert.Equal("Brave", loaded.SearchEngine);
        Assert.Equal(125, loaded.DefaultZoom);
        Assert.DoesNotContain("accentColor", System.Text.Json.JsonSerializer.Serialize(loaded), StringComparison.OrdinalIgnoreCase);
        // And the new version is on disk, so the repair does not run again next start.
        Assert.Equal(BrowserSettings.CurrentSchemaVersion, loaded.SettingsSchemaVersion);
        var written = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file)).RootElement;
        Assert.Equal(BrowserSettings.CurrentSchemaVersion, written.GetProperty("settingsSchemaVersion").GetInt32());
        Assert.False(written.GetProperty("faviconProxyEnabled").GetBoolean());
    }

    [Fact]
    public void TheUserOptingInAfterTheMigrationKeepsThatChoiceOnEveryLaterStart()
    {
        var dir = _root.CreateSubdirectory("opted-in").FullName;
        var file = Path.Combine(dir, "settings.json");
        Directory.CreateDirectory(dir);
        File.WriteAllText(file, """{"faviconProxyEnabled": true, "settingsSchemaVersion": 1}""");
        var store = new SettingsStore(file);

        for (var start = 0; start < 3; start++)
            Assert.True(store.Load().FaviconProxyEnabled);
    }

    [Fact]
    public void AFileWithNoSettingsFileAtAllKeepsTheFallbackOff()
    {
        var store = new SettingsStore(Path.Combine(_root.CreateSubdirectory("empty").FullName, "settings.json"));

        Assert.False(store.Load().FaviconProxyEnabled);
    }

    // ── The request boundary ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AnOptedInPublicPageAsksTheIconService()
    {
        // The one case that has to reach the handler. If the loader went back to
        // requiring a WPF Application before it made the request, this would fail while
        // every zero-request test below still passed.
        var settings = new BrowserSettings { FaviconProxyEnabled = true };
        var (loader, http) = NewLoader(settings);

        await loader.LoadAsync("https://example.org/some/page");

        var request = Assert.Single(http.Requests);
        Assert.Equal("https://icons.duckduckgo.com/ip3/example.org.ico", request.ToString());
    }

    [Fact]
    public async Task TheDefaultProfileNeverReachesTheIconService()
    {
        var (loader, http) = NewLoader(new BrowserSettings());

        await loader.LoadAsync("https://example.org/some/page");

        Assert.Empty(http.Requests);
    }

[Fact]
    public async Task TurningTheToggleOffStopsTheNextRequest()
    {
        var settings = new BrowserSettings { FaviconProxyEnabled = true };
        var (loader, http) = NewLoader(settings);

        await loader.LoadAsync("https://before-the-toggle.example.com/");
        Assert.Single(http.Requests);

        settings.FaviconProxyEnabled = false;
        await loader.LoadAsync("https://after-the-toggle.example.com/");

        Assert.Single(http.Requests);
    }

    // ── A non-web URL carrying a hostname is never sent ────────────────────────────────

    [Theory]
    // These carry an authority, so Uri gives them a host. The host is not something the
    // icon service needs, and a file URL names a path on this machine rather than a site.
    [InlineData("file://public.example.com/share/secret.txt")]
    [InlineData("ftp://public.example.com/pub/file")]
    [InlineData("ws://public.example.com/socket")]
    [InlineData("wss://public.example.com/socket")]
    [InlineData("gopher://public.example.com/1")]
    // Opaque forms have no host to extract at all.
    [InlineData("about:blank")]
    [InlineData("data:text/html,<title>x</title>")]
    [InlineData("file:///C:/Users/someone/secret.txt")]
    [InlineData("mailto:someone@public.example.com")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task ANonWebUrlNeverReachesTheIconService(string url)
    {
        var (loader, http) = NewLoader(new BrowserSettings { FaviconProxyEnabled = true });

        await loader.LoadAsync(url);

        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("https://public.example.com/page")]
    [InlineData("http://public.example.com/page")]
    public async Task AWebPageStillReachesTheIconService(string url)
    {
        // The control for the scheme rule. Refusing everything would pass every test above.
        var (loader, http) = NewLoader(new BrowserSettings { FaviconProxyEnabled = true });

        await loader.LoadAsync(url);

        Assert.Equal("https://icons.duckduckgo.com/ip3/public.example.com.ico", Assert.Single(http.Requests).ToString());
    }

    [Fact]
    public async Task ANonWebUrlForOneHostDoesNotStopTheWebPageForThatHostFromLoading()
    {
        // The in-flight work is keyed by whether the caller could send at all. A cache-only
        // caller must not be handed the network answer, and must not consume the network
        // task that an eligible page for the same host still needs.
        var (loader, http) = NewLoader(new BrowserSettings { FaviconProxyEnabled = true });

        await loader.LoadAsync("file://shared.example.com/share/notes.txt");
        Assert.Empty(http.Requests);

        await loader.LoadAsync("https://shared.example.com/page");

        Assert.Equal("https://icons.duckduckgo.com/ip3/shared.example.com.ico", Assert.Single(http.Requests).ToString());
    }

    [Fact]
    public async Task ANonWebUrlJoiningAWebPageInFlightDoesNotShareItsAnswerOrAddARequest()
    {
        // The page load is held inside the handler. The non-web load for the same host runs
        // against a loader that is already busy and must finish on its own, cache-only.
        var handler = new BlockingHandler();
        var loader = new FaviconLoader(new BrowserSettings { FaviconProxyEnabled = true },
            new HttpClient(handler), _root.CreateSubdirectory("mixed").FullName);

        var page = loader.LoadAsync("https://mixed.example.com/page");
        Assert.True(handler.RequestReached.Task.Wait(TimeSpan.FromSeconds(10)), "the page request never started");

        var nonWeb = await loader.LoadAsync("file://mixed.example.com/share/notes.txt");

        Assert.Null(nonWeb);
        Assert.Equal(1, handler.Count);

        handler.Release.Set();
        await page;

        // One request in total, and it was the page's.
        Assert.Equal(1, handler.Requests.Count);
        Assert.Equal("https://icons.duckduckgo.com/ip3/mixed.example.com.ico", handler.Requests[0].ToString());
    }

    [Fact]
    public async Task ARowJoiningAWebPageInFlightStillStaysCacheOnly()
    {
        var handler = new BlockingHandler();
        var loader = new FaviconLoader(new BrowserSettings { FaviconProxyEnabled = true },
            new HttpClient(handler), _root.CreateSubdirectory("rowrace").FullName);

        var page = loader.LoadAsync("https://rowrace.example.com/page");
        Assert.True(handler.RequestReached.Task.Wait(TimeSpan.FromSeconds(10)), "the page request never started");

        var row = await loader.LoadAsync("https://rowrace.example.com/", FaviconLoadScope.LocalListRow);

        Assert.Null(row);
        Assert.Equal(1, handler.Count);

        handler.Release.Set();
        await page;

        Assert.Equal(1, handler.Requests.Count);
    }

    [Fact]
    public async Task TheHomeArpaTreeIsNeverSentAtAnyDepth()
    {
        // RFC 8375 reserves the whole tree for residential networks. The apex is a name of
        // the same shape as a site and used to pass a suffix comparison.
        var urls = new[]
        {
            "http://home.arpa/",
            "https://HOME.ARPA./",
            "http://host.home.arpa/",
            "https://a.b.home.arpa/x",
            "http://xn--bcher-kva.home.arpa/",
        };

        foreach (var url in urls)
        {
            var (loader, http) = NewLoader(new BrowserSettings { FaviconProxyEnabled = true });

            await loader.LoadAsync(url);

            Assert.True(http.Requests.Count == 0, $"'{url}' was sent to the icon service");
        }
    }

    [Fact]
    public void TheHomeArpaApexIsRefusedByThePolicyWithDescendants()
    {
        foreach (var host in new[] { "home.arpa", "HOME.ARPA.", "host.home.arpa", "a.b.home.arpa" })
            Assert.False(FaviconRequestPolicy.EvaluateHost(host).Allowed, $"'{host}' was allowed");

        Assert.True(FaviconRequestPolicy.EvaluateHost("x.home.arpa.example.com").Allowed);
    }

    [Theory]
    // A lookalike is a different name and stays eligible. Matching a suffix as a substring
    // rather than on a label boundary would refuse these, which would be its own bug.
    [InlineData("myhome.arpa", true)]
    [InlineData("notlocal", false)]
    [InlineData("home.arpa.example.com", true)]
    [InlineData("xhome.arpa.example.com", true)]
    [InlineData("example.org", true)]
    [InlineData("sub.example.org", true)]
    [InlineData("example.org.", true)]
    public void ALookalikePublicHostIsNotSweptUpByTheSuffixRule(string host, bool allowed)
    {
        Assert.Equal(allowed, FaviconRequestPolicy.EvaluateHost(host).Allowed);
    }

    [Fact]
    public async Task ALookalikePublicPageStillReachesTheIconService()
    {
        var (loader, http) = NewLoader(new BrowserSettings { FaviconProxyEnabled = true });

        await loader.LoadAsync("https://myhome.arpa.example.com/page");

        Assert.Equal("https://icons.duckduckgo.com/ip3/myhome.arpa.example.com.ico",
            Assert.Single(http.Requests).ToString());
    }

    [Theory]
    // Loopback and private addresses, every form the loader or a user could type.
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.1/")]
    [InlineData("https://192.168.1.10/admin")]
    [InlineData("https://10.0.0.5/")]
    [InlineData("https://172.16.4.4/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fd00:ec2::254]/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    // Hostnames that only exist on a machine or a local network.
    [InlineData("http://localhost:8080/")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://nas.local/")]
    [InlineData("http://router/")]
    [InlineData("http://fileserver/share")]
    [InlineData("http://wiki.intranet/")]
    [InlineData("http://build.internal/")]
    [InlineData("http://host.home.arpa/")]
    [InlineData("http://db.lan/")]
    public async Task APrivateOrLocalHostIsNeverSentEvenWithTheToggleOn(string url)
    {
        var (loader, http) = NewLoader(new BrowserSettings { FaviconProxyEnabled = true });

        await loader.LoadAsync(url);

        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData("HTTP://EXAMPLE.ORG/")]           // scheme and host case
    [InlineData("https://example.org./")]         // trailing dot is the same site
    public async Task NormalizationCannotBeUsedToSneakAHostPast(string url)
    {
        var decision = FaviconRequestPolicy.Evaluate(url);

        Assert.True(decision.Allowed);
        Assert.Equal("example.org", decision.Host);
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("data:text/html,<title>x</title>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Users/someone/secret.txt")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void NonWebUrlsAreRefused(string? url)
    {
        Assert.False(FaviconRequestPolicy.Evaluate(url).Allowed);
    }

    [Fact]
    public void ThePolicyIsAboutTheShapeOfTheNameAndNotAboutResolvingIt()
    {
        // A name that looks public is allowed. Whether it resolves, and where, is not
        // something this app can know without asking a resolver, which would itself be a
        // disclosure. The name goes only to the icon service the user opted in to.
        var decision = FaviconRequestPolicy.Evaluate("https://nonexistent-host-abc123.example.com/");

        Assert.True(decision.Allowed);
        Assert.Equal("nonexistent-host-abc123.example.com", decision.Host);
    }

    [Fact]
    public void AnInternationalizedHostIsReducedToItsAsciiForm()
    {
var decision = FaviconRequestPolicy.Evaluate("https://BÜCHER.example.com/");

        Assert.True(decision.Allowed);
        Assert.Equal("xn--bcher-kva.example.com", decision.Host);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("fe80::1%12")]
    [InlineData("192.168.0.1")]
    [InlineData("2130706433")]
    [InlineData("0x7f.0.0.1")]
    public void EveryAddressFormIsRefusedByThePolicy(string host)
    {
        Assert.False(FaviconRequestPolicy.EvaluateHost(host).Allowed);
    }

    [Fact]
    public void TheRequestUrlIsBuiltOnlyFromAnAllowedHost()
    {
        var decision = FaviconRequestPolicy.Evaluate("https://example.org/");

        Assert.Equal("https://icons.duckduckgo.com/ip3/example.org.ico", FaviconRequestPolicy.BuildIconUrl(decision.Host));
    }

    // ── Local list rows never leave the machine ───────────────────────────────────────

    [Theory]
    [InlineData("https://example.org/")]
    [InlineData("http://192.168.0.9/")]
    [InlineData("https://a-site-only-in-history.example.com/")]
    public async Task ASuggestionOrHistoryRowNeverReachesTheIconService(string url)
    {
        // These are the rows the address bar paints on every keystroke, over hosts drawn
        // from the user's own history. Caching is allowed. The network is not.
        var (loader, http) = NewLoader(new BrowserSettings { FaviconProxyEnabled = true });

        await loader.LoadAsync(url, FaviconLoadScope.LocalListRow);

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task ASuggestionRowStillReadsTheCachedIcon()
    {
        // The cache is the whole point of the row path, so the negative case above is not
        // a row that stops working. The bytes are in the cache folder the loader was
        // given, which is the same read path a native favicon writes into.
        var cacheDir = _root.CreateSubdirectory("shared-cache").FullName;
        var settings = new BrowserSettings { FaviconProxyEnabled = true };
        var (writer, writerHttp) = NewLoader(settings, cacheDir);
        await writer.LoadAsync("https://cached-site.example.com/");
        Assert.Single(writerHttp.Requests);
        Assert.True(File.Exists(Path.Combine(cacheDir, "cached-site.example.com.png")));

        // A different loader instance, same cache folder: what a later session looks like.
        var (reader, readerHttp) = NewLoader(new BrowserSettings(), cacheDir);
        await reader.LoadAsync("https://cached-site.example.com/", FaviconLoadScope.LocalListRow);

        Assert.Empty(readerHttp.Requests);
    }

    [Fact]
    public async Task ACacheOnlyMissDoesNotBlockALaterPageLoad()
    {
        // A row lookup that finds nothing must not leave a remembered empty answer for
        // that host, or the page that follows would inherit the miss forever.
        var settings = new BrowserSettings { FaviconProxyEnabled = true };
        var (loader, http) = NewLoader(settings);

        Assert.Null(await loader.LoadAsync("https://later-page.example.com/", FaviconLoadScope.LocalListRow));
        Assert.Empty(http.Requests);

        await loader.LoadAsync("https://later-page.example.com/", FaviconLoadScope.PageNavigation);

        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task AMissIsNotRememberedAcrossScenarios()
    {
        var (loader, http) = NewLoader(new BrowserSettings());

        Assert.Null(await loader.LoadAsync("https://never-requested.example.com/"));
        Assert.Null(await loader.LoadAsync("https://never-requested.example.com/"));

        Assert.Empty(http.Requests);
    }

    // ── The cache keeps working the way it did ───────────────────────────────────────

[Fact]
    public async Task AnIconAlreadyOnDiskIsUsedWithoutAnyRequest()
    {
        var cacheDir = _root.CreateSubdirectory("disk-cache").FullName;
        Directory.CreateDirectory(cacheDir);
        File.WriteAllBytes(Path.Combine(cacheDir, "known.example.com.png"), SamplePng());
        var (loader, http) = NewLoader(new BrowserSettings { FaviconProxyEnabled = true }, cacheDir);

        // The decode needs a WPF Application, so null here is expected and is not a
        // failure. What matters is that the request never happened: a host whose icon is
        // already cached is never a reason to publish it.
        await loader.LoadAsync("https://known.example.com/page");

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task ACachedIconThatWillNotDecodeStillKeepsTheHostOffTheNetwork()
    {
        // A truncated or corrupt cache entry is a decode problem, not a reason to go and
        // tell a third party which site the user was looking at.
        var cacheDir = _root.CreateSubdirectory("corrupt-cache").FullName;
        Directory.CreateDirectory(cacheDir);
        File.WriteAllBytes(Path.Combine(cacheDir, "broken.example.com.png"), new byte[] { 9, 9, 9 });
        var (loader, http) = NewLoader(new BrowserSettings { FaviconProxyEnabled = true }, cacheDir);

        Assert.Null(await loader.LoadAsync("https://broken.example.com/page"));

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task AFetchedIconIsWrittenToTheCacheFolderForLater()
    {
        var cacheDir = _root.CreateSubdirectory("written-cache").FullName;
        var (loader, _) = NewLoader(new BrowserSettings { FaviconProxyEnabled = true }, cacheDir);

await loader.LoadAsync("https://stored.example.com/");

        var path = Path.Combine(cacheDir, "stored.example.com.png");
        Assert.True(File.Exists(path), "the fetched bytes were not kept");
        Assert.Equal(SamplePng(), File.ReadAllBytes(path));
    }

    [Fact]
    public async Task AnInternalPageOrBlankDocumentNeverAsksForAnIcon()
    {
        var (loader, http) = NewLoader(new BrowserSettings { FaviconProxyEnabled = true });

        await loader.LoadAsync("internal://settings");
        await loader.LoadAsync("internal://newtab");
        await loader.LoadAsync("about:blank");

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task AFailingIconServiceDoesNotThrow()
    {
        var settings = new BrowserSettings { FaviconProxyEnabled = true };
        var handler = new ThrowingHandler();
        var loader = new FaviconLoader(settings, new HttpClient(handler), _root.FullName);

        Assert.Null(await loader.LoadAsync("https://unreachable.example.com/"));
        Assert.Equal(1, handler.Attempts);
    }

private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Attempts++;
            throw new HttpRequestException("no network in this test");
        }
    }

    /// <summary>
    /// Holds the first request open so a second load can be started against a loader that
    /// is already busy. What a second load does while that happens is the thing that
    /// cannot be observed any other way.
    /// </summary>
    private sealed class BlockingHandler : HttpMessageHandler
    {
        private int _count;

        public ManualResetEventSlim Release { get; } = new(false);
        public TaskCompletionSource RequestReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<Uri> Requests { get; } = new();
        public int Count => Volatile.Read(ref _count);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add(request.RequestUri!);
            if (Interlocked.Increment(ref _count) == 1) RequestReached.TrySetResult();

            await Task.Run(() => Release.Wait(TimeSpan.FromSeconds(30)), ct);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SamplePng()) };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Release.Set();
            base.Dispose(disposing);
        }
    }
}
