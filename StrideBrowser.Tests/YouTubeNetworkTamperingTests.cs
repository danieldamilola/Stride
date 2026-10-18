using System.IO;
using System.Reflection;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Guards the removal of the browser side YouTube tampering. Ad blocking belongs to
/// the uBlock Origin extension, and the built in YouTube Unhook script is the only
/// other thing allowed to touch a YouTube page. These tests fail if the request
/// filter comes back, if its hardcoded rules come back, if the ad blocking preference
/// stops reaching the extension, or if Unhook stops being injected.
/// </summary>
public sealed class YouTubeNetworkTamperingTests
{
    /// <summary>
    /// Production source roots. The test project, tools, scratch and Parked are left
    /// out on purpose: nothing there ships in the app, and Parked holds scripts the
    /// product does not inject.
    /// </summary>
    private static readonly string[] ProductionRoots =
    [
        "Controls", "Converters", "Engine", "Helpers", "Interop", "Models",
        "Services", "ViewModels"
    ];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Stride.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static IEnumerable<string> ProductionSources()
    {
        var root = RepoRoot();
        var files = new List<string>();
        foreach (var relative in ProductionRoots)
        {
            var dir = Path.Combine(root, relative);
            if (!Directory.Exists(dir)) continue;
            files.AddRange(Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories));
        }
        files.AddRange(Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly));
        return files
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }

    private static string Text(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative));

    /// <summary>
    /// Comments are stripped first. A comment that describes a removed filter is
    /// allowed and useful; what must not exist is code that registers one.
    /// </summary>
    private static string CodeOnly(string text)
    {
        var withoutBlocks = System.Text.RegularExpressions.Regex.Replace(text, @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        return string.Join("\n", withoutBlocks
            .Split('\n')
            .Select(line =>
            {
                var comment = line.IndexOf("//", StringComparison.Ordinal);
                return comment >= 0 ? line[..comment] : line;
            }));
    }

    /// <summary>
    /// The one API that lets host code answer a request with its own response. It was
    /// registered for every request in every context, so a host handler saw page loads
    /// and media alike.
    /// </summary>
    [Fact]
    public void NoProductionSourceRegistersAHostRequestFilter()
    {
        var offenders = new List<string>();
        foreach (var file in ProductionSources())
        {
            var text = CodeOnly(File.ReadAllText(file));
            foreach (var token in new[]
            {
                "AddWebResourceRequestedFilter",
                "WebResourceRequested",
                "CreateWebResourceResponse",
                "SetUserAgent"
            })
                if (text.Contains(token, StringComparison.Ordinal))
                    offenders.Add($"{Path.GetRelativePath(RepoRoot(), file)}: {token}");
        }
        Assert.True(offenders.Count == 0,
            "browser side request interception is back:\n" + string.Join("\n", offenders));
    }

    /// <summary>The compiled artifact itself, not just the text that was searched.</summary>
    [Fact]
    public void TheLegacyFilterTypeIsNotInTheBuiltAssembly()
    {
        var types = typeof(TabEngine).Assembly.GetTypes();

        Assert.DoesNotContain(types, t => t.Name.Contains("AdBlockFilter", StringComparison.OrdinalIgnoreCase));

        var applied = types.SelectMany(t =>
            t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
             .Where(m => m.Name is "Apply" or "InitializeAsync")
             .Where(m => m.ReturnType == typeof(void) || m.ReturnType == typeof(System.Threading.Tasks.Task))
             .Select(m => $"{t.FullName}.{m.Name}"))
            .Where(signature => signature.Contains("AdBlock", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(applied.Count == 0, "legacy ad filter entry points still compiled: " + string.Join(", ", applied));
    }

    /// <summary>
    /// The specific rules the filter carried. They were a fixed list of YouTube ad
    /// endpoints and ad image hosts, none of which anything in the product needs now.
    /// </summary>
    [Fact]
    public void NoProductionSourceCarriesHardcodedYouTubeNetworkRules()
    {
        var banned = new[]
        {
            "/api/stats/ads", "/api/stats/qoe", "/pagead/", "/get_midroll_info",
            "/ptracking", "yt3.ggpht", "ad.youtube.com", "ads.youtube.com",
            "googlevideo", "youtubei"
        };

        var files = ProductionSources()
            .Concat(new[]
            {
                Path.Combine(RepoRoot(), "Resources", "Scripts", "youtube-unhook.js")
            });
        var offenders = new List<string>();
        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;
            var raw = File.ReadAllText(file);
            // The injected script is scanned whole, because every line of it runs.
            // C# is scanned for code only, so a comment may name what was removed.
            var text = file.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ? raw : CodeOnly(raw);
            foreach (var token in banned)
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                    offenders.Add($"{Path.GetRelativePath(RepoRoot(), file)}: {token}");
        }
        Assert.True(offenders.Count == 0,
            "hardcoded YouTube network rules are back:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// No browser argument overrides the YouTube player either. Chromium's own autoplay
    /// policy is the product's policy.
    /// </summary>
    [Fact]
    public void NoBrowserArgumentOverridesAutoplayOrUserAgent()
    {
        var args = WebViewFactory.BuildBrowserArguments(smartScreenEnabled: true);
        foreach (var token in new[] { "--autoplay-policy", "--user-agent", "--disable-web-security" })
            Assert.DoesNotContain(token, args);
    }

    /// <summary>
    /// AdBlockEnabled is now only an extension switch, and it still reaches the
    /// extension. Removing the legacy filter must not have taken the extension path
    /// with it: the setting page still writes the key, the handler still lands it on
    /// BrowserSettings, and the factory still applies the profile from it.
    /// </summary>
    [Fact]
    public void AdBlockPreferenceStillReachesTheExtension()
    {
        Assert.Contains("AdBlockEnabled", Text("Models/BrowserSettings.cs"));

        var handler = Text("Engine/Handlers/SettingsMessageHandler.cs");
        Assert.Contains("\"adBlock\"", handler);
        Assert.Contains("s.AdBlockEnabled", handler);
        // The preference is applied to the live profile when it changes, not left for
        // the next page load to discover.
        Assert.Contains("await _engine.ApplyAdBlockPreferenceAsync(_settings.AdBlockEnabled)", handler);

        var factory = Text("Engine/WebViewFactory.cs");
        Assert.Contains("_settings.AdBlockEnabled", factory);
        Assert.Contains("_extensionManager.ReconcileAsync(core, desiredEnabled)", factory);
        // Registration is no longer inferred from a folder being present. The old code
        // set a loaded flag from GetHealth().HasFolder, which says nothing about
        // whether WebView2 holds a registration or whether it is enabled.
        Assert.DoesNotContain("health.HasFolder", factory);

        // The manager keeps both directions of the enable state, and reads the state
        // back from the profile instead of assuming the call took effect.
        var manager = Text("Services/ExtensionManager.cs");
        Assert.Contains("listed.IsEnabled != desiredEnabled", manager);
        Assert.Contains("EnableAsync(desiredEnabled)", manager);
        Assert.Contains("GetBrowserExtensionsAsync", manager);
        Assert.Contains("new ExtensionNativeState(true, live.IsEnabled", manager);
        // A repair is written over the folder the platform is holding, with no native
        // remove in front of it. Removing and re-adding the same folder is a reinstall, and
        // a reinstall discards the extension's own storage; that was measured with a
        // durable sentinel key and a non-default list selection, both gone after a remove
        // and add and both surviving an in-place write. So the bytes are computed by
        // staging and committed in place, guarded by a comparison, and the only guarantee
        // offered is the rollback: a failed write puts every file back.
        var code = CodeOnly(manager);
        Assert.Contains("staged = StageFolderRepair(existingManifest);", code);
        Assert.Contains("if (mayWriteFolder)", code);
        Assert.Contains("public CommitResult CommitFolderRepair(StagedRepair staged)", code);
        Assert.Contains("ReconcileAction.RepairInPlace", code);
        var guardAt = code.IndexOf(
            "if (plan.Action == ReconcileAction.RepairInPlace", StringComparison.Ordinal);
        var commitAt = code.IndexOf("var commit = CommitFolderRepair(repair);", StringComparison.Ordinal);
        Assert.True(guardAt >= 0, "the in-place repair branch exists");
        Assert.True(commitAt > guardAt, "the repair is committed inside the in-place branch");
        var removeAt = code.IndexOf("await RemoveExtensionAsync(ublock);", StringComparison.Ordinal);
        Assert.True(removeAt > commitAt,
            "the native remove belongs to a genuine folder change, not to a repair");
        var repairText = Text("Services/UBlockAssetRepair.cs");
        Assert.Contains("string.Equals(text, current, StringComparison.Ordinal)", repairText);

        // The tab creation path no longer applies any filter of its own.
        Assert.DoesNotContain("_settings.AdBlockEnabled", Text("Engine/TabEngine.cs"));
    }

    /// <summary>
    /// The registration is awaited before the first external navigation, and the
    /// built in pages and the popup sentinel are not made to wait for it. This is the
    /// ordering the old fire and forget path did not have.
    /// </summary>
    [Fact]
    public void RegistrationIsAwaitedBeforeTheFirstExternalNavigationOnly()
    {
        var engine = Text("Engine/TabEngine.cs");

        Assert.Contains("var gate = await AwaitExtensionGateAsync(wv, tab.Url, _shutdownCts.Token)", engine);
        Assert.Contains("if (!ServeBuiltInPage(tab))", engine);
        // ServeBuiltInPage claims the internal pages and the pending native sentinel
        // before the gate is reached, which is what keeps them immediate.
        Assert.Contains("case InternalPageKind.PendingNative:", engine);
        Assert.Contains("if (!IsExternalRequestUrl(url)) return ExtensionGateOutcome.Proceed;", engine);

        // Nothing in the engine registers a filter or waits on a fixed sleep to
        // declare the extension ready.
        Assert.DoesNotContain("Task.Delay", engine);
        Assert.DoesNotContain("TryInitializeExtensions", Text("Engine/WebViewFactory.cs"));
    }

    /// <summary>
    /// A cancelled wait is not permission to request the page anyway. The gate belongs
    /// to the engine rather than to the activation that started it, so a tab switch
    /// cannot turn an unproven registration into a first request, and a suppressed
    /// navigation drops its control instead of leaving a blank one behind.
    /// </summary>
    [Fact]
    public void ACancelledGateSuppressesTheNavigationRatherThanFailingOpen()
    {
        var engine = Text("Engine/TabEngine.cs");

        Assert.Contains("if (result.Status == ExtensionStartupStatus.Cancelled)", engine);
        Assert.Contains("return ExtensionGateOutcome.Suppress;", engine);
        Assert.Contains("if (gate != ExtensionGateOutcome.Proceed)", engine);
        Assert.Contains("AbandonControl(tab, wv);", engine);
        // The wait is not the activation token, which a tab switch cancels.
        Assert.DoesNotContain("AwaitExtensionGateAsync(wv, tab.Url, ct)", engine);
        Assert.Contains("_shutdownCts.Cancel()", engine);
    }

    /// <summary>
    /// A navigation that had to wait cannot land after a newer request already did,
    /// including a generated page, so a superseded url is dropped rather than written
    /// over the newer one.
    /// </summary>
    [Fact]
    public void ADeferredNavigationIsDroppedWhenANewerRequestOwnsTheTab()
    {
        var engine = Text("Engine/TabEngine.cs");

        Assert.Contains("IsLatestNavigationIntent(tab.Id, intent)", engine);
        Assert.Contains("TabEngine dropped a superseded deferred navigation", engine);
        Assert.Contains("!string.Equals(tab.Url, url, StringComparison.Ordinal)", engine);
        // Every document delivery takes a fresh intent: a typed url, a generated
        // page, and a first navigation.
        Assert.Equal(3, CountOccurrences(engine, "BeginNavigationIntent(tab.Id)"));

        // The creation path holds an intent from before the wait. It is claimed before
        // the control is reachable, so a request that lands while the control is being
        // built is strictly newer. Both places that deliver a document check it again
        // and drop rather than write over the newer one. The real behaviour of this path
        // is measured in the first navigation supersession runs, not only here.
        Assert.Contains("TabEngine first external navigation dropped as superseded", engine);
        Assert.Contains("TabEngine first internal page dropped as superseded", engine);
        Assert.DoesNotContain("first external navigation superseded for tab", CodeOnly(engine));
    }

    /// <summary>
    /// Nothing prepares extension files outside the coordinator's run. The eager
    /// startup call was a second writer against the same folder, and it could rewrite
    /// assets.json while the platform had it registered.
    /// </summary>
    [Fact]
    public void ExtensionFilesAreOnlyPreparedInsideTheCoordinatedRun()
    {
        // A wiring guard on a startup path that cannot be driven from a unit test. The
        // behavioural half of the same claim is measured on the real app: exactly one
        // preparation per cold start, inside the gate. Comments are stripped first, so
        // the explanation that replaced the call is not mistaken for the call.
        var startup = CodeOnly(Text("App.xaml.cs"));
        Assert.DoesNotContain("EnsureUBlockDownloadedAsync", startup);
        Assert.DoesNotContain("PrepareAsync", startup);

        var manager = Text("Services/ExtensionManager.cs");
        // PrepareOffThread is the single door to the disk work, and only the
        // reconciliation path calls it. The argument is what keeps the disk work off a
        // folder WebView2 is holding.
        Assert.Equal(1, CountOccurrences(manager, "await PrepareOffThread(mayWriteFolder: !registered)"));
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(token, at, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += token.Length;
        }
        return count;
    }

    /// <summary>
    /// Unhook is untouched and still injected on a document start, which is the whole
    /// point of keeping it: it is the YouTube feature that was asked to stay.
    /// </summary>
    [Fact]
    public void UnhookIsStillInjectedAndStillFollowsThePreference()
    {
        var unhook = new YouTubeUnhook();
        Assert.NotNull(new ContentScriptInjector(unhook));

        var withUnhook = new BrowserSettings();
        Assert.True(withUnhook.UnhookEnabled);
        var script = unhook.GetScript(withUnhook);
        Assert.NotEmpty(script);
        Assert.Contains("__STRIDE_UNHOOK", script);
        Assert.Contains("stride-unhook-hide-section", script);

        Assert.Equal("", unhook.GetScript(new BrowserSettings { UnhookEnabled = false }));

        // A preference change is pushed to live cores by this method, which calls the
        // injector's update and then executes the script in the page.
        var reinject = typeof(TabEngine).GetMethod("ReInjectUnhookAsync");
        Assert.NotNull(reinject);
        Assert.Contains("UpdateYouTubeScriptAsync", Text("Engine/ContentScriptInjector.cs"));
        Assert.Contains("_contentScriptInjector.UpdateYouTubeScriptAsync", Text("Engine/TabEngine.cs"));

        // The script itself stays where it was.
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "Resources", "Scripts", "youtube-unhook.js")));
    }
}