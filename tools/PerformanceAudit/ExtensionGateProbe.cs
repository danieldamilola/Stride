using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.LinkPreview;

internal static partial class Program
{
    /// <summary>
    /// Reproduces the F05 acceptance case with the real engine. One controlled
    /// reconcile stays unresolved while an external tab passes creation, and an
    /// internal tab activates and renders without waiting for it.
    /// </summary>
    private static async Task EngineExtensionGateProbe()
    {
        var root = Path.Combine(Output, "extension-gate");
        Directory.CreateDirectory(root);
        var downloads = new DownloadStore(Path.Combine(root, "downloads.json"));
        var history = new HistoryStore(Path.Combine(root, "history.json"));
        var saved = new OneTabStore(Path.Combine(root, "saved.json"));
        var settingsStore = new SettingsStore(Path.Combine(root, "settings.json"));
        var settings = new BrowserSettings { AppTheme = AppThemeMode.Dark, FaviconProxyEnabled = false, AdBlockEnabled = true };
        var theme = new ThemeManager(settings);
        var unhook = new YouTubeUnhook();
        var focus = new FocusBlocklistService(settingsStore);
        var hibernation = new TabHibernationManager(downloads, settings);
        var session = new BrowserSession { WebViewDataDirectory = Path.Combine(root, "profile") };
        var coordinator = session.ExtensionStartup;
        var profileKey = ExtensionStartupCoordinator.ProfileKeyFor(session.WebViewDataDirectory);

        // A held registration run. Every waiter shares it, so the external tab's
        // readiness stays unresolved until this release fires.
        var release = new TaskCompletionSource<ExtensionNativeState>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = coordinator.EnsureReadyAsync(new ExtensionStartupRequest(
            profileKey, true, "audit-held-run",
            (desiredEnabled, cancellationToken) => release.Task, null));
        Require(coordinator.AttemptCount(profileKey) > 0, "Held extension reconcile did not start");

        using var engine = new TabEngine(new EngineDependencies
        {
            Session = session,
            ClosedTabStore = new ClosedTabStore(Path.Combine(root, "closed.json"), 50),
            Settings = settings, ThemeManager = theme, Pages = new InternalPages(theme),
            ExtensionManager = new ExtensionManager(),
            YouTubeUnhook = unhook, YouTubeEnhancer = new YouTubeEnhancer(),
            FaviconLoader = new FaviconLoader(settings),
            HistoryStore = history, OneTabStore = saved, DownloadStore = downloads,
            FocusBlocklistService = focus, ContentScriptInjector = new ContentScriptInjector(unhook),
            CustomDownloadManager = new CustomDownloadManager(), HibernationManager = hibernation,
            NavigationPolicyEngine = new NavigationPolicyEngine(focus),
            DownloadSuppressor = new LinkPreviewDownloadSuppressor()
        });
        var host = new System.Windows.Controls.Grid();
        var window = new Window { Content = host, Width = 1100, Height = 800, Left = -12000,
            Top = -12000, ShowInTaskbar = false, ShowActivated = false };
        window.Show();
        try
        {
            engine.AttachHost(host);
            await engine.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(45));

            var viewField = typeof(TabEngine).GetField("_webViews", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var gateField = typeof(TabEngine).GetField("_activationGate", BindingFlags.Instance | BindingFlags.NonPublic)!;

            var external = engine.CreateTab("https://audit.assets/held.html", false, true);
            var externalCreationStarted = Stopwatch.StartNew();
            var externalActivation = engine.ActivateAsync(external);

            // Creation finished when this tab's control carries a live core, the shared
            // creation gate is back to one and the outer activation still runs.
            for (var settled = false; !settled;)
            {
                Require(externalCreationStarted.Elapsed < TimeSpan.FromSeconds(45),
                    "External control creation did not finish within 45 seconds");
                Require(!externalActivation.IsCompleted, "External activation ended while the held registration was unresolved");
                var gateCheck = (SemaphoreSlim)gateField.GetValue(engine)!;
                if (gateCheck.CurrentCount == 1
                    && ((System.Collections.Generic.Dictionary<Guid, dynamic>)viewField.GetValue(engine)!).TryGetValue(external.Id, out var liveView)
                    && (object)liveView.CoreWebView2 is not null)
                {
                    settled = true;
                }
                await Task.Delay(50);
            }
            var externalCreationMs = externalCreationStarted.ElapsedMilliseconds;

            // No remote request from this tab starts while it waits for the extension.
            var blocked = 0;
            var externalView = (WebView2)(object)((System.Collections.Generic.Dictionary<Guid, dynamic>)viewField.GetValue(engine)!)[external.Id];
            externalView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            externalView.CoreWebView2.WebResourceRequested += (_, args) =>
            {
                blocked++;
                args.Response = externalView.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "No remote resources in audit", "Content-Length: 0");
            };

            // The internal tab now activates. Pre-fix code kept the creation semaphore
            // through the external tab's extension wait, so this activation waited too.
            var creationGate = (SemaphoreSlim)gateField.GetValue(engine)!;
            Require(creationGate.CurrentCount == 1, "Creation gate stayed occupied after the external tab's creation finished");
            var inner = engine.CreateTab(InternalUrls.Downloads, false, true);
            var innerStarted = Stopwatch.StartNew();
            await engine.ActivateAsync(inner).WaitAsync(TimeSpan.FromSeconds(45));
            var innerMs = innerStarted.ElapsedMilliseconds;
            var innerView = (WebView2)(object)((System.Collections.Generic.Dictionary<Guid, dynamic>)viewField.GetValue(engine)!)[inner.Id];
            await WaitFor(innerView, "document.getElementById('nextPage') !== null && typeof requestSync === 'function'",
                "Internal Downloads page did not render while the external tab waited");
            Require(!externalActivation.IsCompleted,
                "External activation finished before the controlled release; the delay was not held");
            Require(innerView.ActualWidth > 0 && innerView.ActualHeight > 0,
                "Internal Downloads control has no rendered area");
            await Capture(innerView, "extension-gate-internal-downloads.png").WaitAsync(TimeSpan.FromSeconds(15));
            var blockedBeforeRelease = blocked;
            Require(blockedBeforeRelease == 0, "External requests started before extension readiness was released");

            // The held run fails honestly: the state never satisfies the preference, so
            // the waiter proceeds without claiming readiness and completes its navigation.
            var releaseStarted = Stopwatch.StartNew();
            release.TrySetResult(ExtensionNativeState.None);
            await externalActivation.WaitAsync(TimeSpan.FromSeconds(60));
            var releasedThenCompletedMs = releaseStarted.ElapsedMilliseconds;
            Results.Add(new
            {
                name = "extension-gate-internal-activation",
                externalCreationMs,
                innerTabActivationMs = innerMs,
                innerRendered = true,
                externalStillPendingBeforeRelease = true,
                controlledReconcile = "one held TaskCompletionSource shared by the profile coordinator",
                creationGateFreeDuringPendingExternalWait = true,
                blockedRemoteRequestsFromHeldTab = blockedBeforeRelease,
                releaseOutcome = "failed attempt; navigation proceeds without a ready state",
                externalCompletedAfterReleaseMs = releasedThenCompletedMs,
                scope = "real TabEngine activation path with a real WebView2 control and a deliberately delayed shared reconcile",
                preFixBaseline = "pre-fix code kept the creation semaphore through the extension wait, so a new internal tab waited behind it"
            });
            Console.WriteLine($"Extension gate measured: inner activation {innerMs} ms; external still pending before release");
        }
        finally
        {
            release.TrySetResult(ExtensionNativeState.None);
            engine.Dispose();
            Require(await downloads.FlushAsync(), "Extension-gate download flush failed");
            Require(await saved.FlushAsync(), "Extension-gate saved-tab flush failed");
            Require(await history.FlushAsync(), "Extension-gate history flush failed");
            window.Close();
        }
    }
}
