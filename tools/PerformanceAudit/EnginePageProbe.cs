using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.Engine;
using StrideBrowser.Engine.Handlers;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.LinkPreview;

internal static partial class Program
{
    /// <summary>Real engine, registered internal documents, native messages and production handlers.</summary>
    private static async Task EnginePageSmoke()
    {
        foreach (var retained in new[] { 5000, 10000 })
        {
            var root = Path.Combine(Output, "engine-" + retained);
            Directory.CreateDirectory(root);
            var downloadPath = Path.Combine(root, "downloads.json");
            var items = Downloads(retained);
            var actualFile = Path.Combine(root, "present.txt");
            File.WriteAllText(actualFile, "local synthetic download");
            items[0].FilePath = actualFile;
            File.WriteAllText(downloadPath, JsonSerializer.Serialize(items, DownloadJson.Options));
            var downloads = new DownloadStore(downloadPath);
            var history = new HistoryStore(Path.Combine(root, "history.json"));
            var saved = new OneTabStore(Path.Combine(root, "saved.json"));
            saved.Save([
                new() { Id = "first", Name = "First group", Tabs = [new("https://duplicate.example/", "First duplicate", null, DateTime.UtcNow), new("https://duplicate.example/", "Second duplicate", null, DateTime.UtcNow), new("https://other.example/", "Other", null, DateTime.UtcNow)] },
                new() { Id = "second", Name = "Second group", Tabs = [new("https://last.example/", "Last", null, DateTime.UtcNow)] }
            ]);
            Require(await saved.FlushAsync(), "Saved-tab smoke fixture did not persist");
            var settings = new BrowserSettings { AppTheme = AppThemeMode.Dark, FaviconProxyEnabled = false, AdBlockEnabled = false };
            var settingsStore = new SettingsStore(Path.Combine(root, "settings.json"));
            var theme = new ThemeManager(settings);
            var unhook = new YouTubeUnhook();
            var focus = new FocusBlocklistService(settingsStore);
            var hibernation = new TabHibernationManager(downloads, settings);
            using var engine = new TabEngine(new EngineDependencies
            {
                Session = new BrowserSession(), ClosedTabStore = new ClosedTabStore(Path.Combine(root, "closed.json"), 50),
                Settings = settings, ThemeManager = theme, Pages = new InternalPages(theme), ExtensionManager = new ExtensionManager(),
                YouTubeUnhook = unhook, YouTubeEnhancer = new YouTubeEnhancer(), FaviconLoader = new FaviconLoader(settings),
                HistoryStore = history, OneTabStore = saved, DownloadStore = downloads,
                FocusBlocklistService = focus, ContentScriptInjector = new ContentScriptInjector(unhook),
                CustomDownloadManager = new CustomDownloadManager(), HibernationManager = hibernation,
                NavigationPolicyEngine = new NavigationPolicyEngine(focus), DownloadSuppressor = new LinkPreviewDownloadSuppressor()
            });
            var router = new WebMessageRouter([new DownloadMessageHandler(engine, downloads), new OneTabMessageHandler(engine, saved)]);
            var routed = new List<string>();
            Exception? routeFailure = null;
            engine.WebMessageReceived += async message =>
            {
                try { routed.Add(message); await router.RouteAsync(message); }
                catch (Exception ex) { routeFailure = ex; }
            };
            using var view = new WebView2();
            var window = new Window { Content = view, Width = 1100, Height = 800, Left = -12000,
                Top = -12000, ShowInTaskbar = false, ShowActivated = false };
            window.Show();
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(root, "webview"));
                await view.EnsureCoreWebView2Async(environment).WaitAsync(TimeSpan.FromSeconds(45));
                BlockRemoteResources(view);
                await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
                    window.__auditNativeUpdates=[];
                    window.chrome.webview.addEventListener('message',event=>{
                      if(typeof event.data !== 'string') return;
                      const marker=':downloads:', index=event.data.indexOf(marker);
                      if(index>=0) window.__auditNativeUpdates.push(JSON.parse(event.data.slice(index+marker.length)));
                    });
                    """);
                var tab = engine.CreateTab(InternalUrls.Downloads);
                var views = (Dictionary<Guid, dynamic>)typeof(TabEngine).GetField("_webViews", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
                views[tab.Id] = view;
                typeof(TabEngine).GetMethod("WireTabEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [view, tab]);
                await LoadEnginePage(view, () => engine.NavigateToDownloads(tab));
                await WaitFor(view, "itemMap.size === 50 && window.__auditNativeUpdates.length > 0", "Real engine did not deliver the first 50 Downloads rows");
                await CheckJs(view, "window.__auditNativeUpdates[0].ids.length === 50 && window.__auditNativeUpdates[0].changed.length === 50", "Real handler delivered an unbounded initial payload");
                await CheckJs(view, "!document.querySelector('.item[data-id=download0]').classList.contains('missing')", "Present local download was incorrectly marked missing");
                var beforeRoutes = routed.Count;
                await view.ExecuteScriptAsync("window.__auditNativeUpdates=[];requestSync()");
                await Task.Delay(250);
                Require(routed.Count > beforeRoutes, "Downloads request did not reach the registered engine IPC route");
                await CheckJs(view, "window.__auditNativeUpdates.length === 0", "Real unchanged handler sent a native payload");

                await view.ExecuteScriptAsync("document.getElementById('nextPage').click()");
                await WaitFor(view, "page === 1 && itemMap.has('download50') && itemMap.size === 50", "Real handler pagination failed");
                await view.ExecuteScriptAsync("document.getElementById('previousPage').click()");
                await WaitFor(view, "page === 0 && itemMap.has('download0')", "Real handler previous page failed");
                await view.ExecuteScriptAsync("changePage(" + ((retained - 1) / 50) + ")");
                await WaitFor(view, "itemMap.has('download" + (retained - 1) + "') && itemMap.size <= 50", "Real handler did not reach old records by page");
                await view.ExecuteScriptAsync("document.getElementById('search').value='Example file " + (retained - 1) + ".zip';document.getElementById('search').dispatchEvent(new Event('input'))");
                await WaitFor(view, "itemMap.size === 1 && itemMap.has('download" + (retained - 1) + "')", "Real handler search failed");
                await view.ExecuteScriptAsync("document.querySelector('.item .actions button[title=\"Remove from history\"]').click()");
                await WaitFor(view, "itemMap.size === 0", "Remove action did not update real Downloads page");
                Require(downloads.Get("download" + (retained - 1)) is null && routed.Contains("download-remove:download" + (retained - 1)), "Remove action used an incorrect ID");
                await view.ExecuteScriptAsync("document.getElementById('search').value='';changePage(0)");
                await WaitFor(view, "itemMap.has('download0') && itemMap.size === 50", "Downloads did not restore first page");

                // The product caches file details for five seconds. Exercise its actual refresh path.
                File.Delete(actualFile);
                await Task.Delay(5250);
                await view.ExecuteScriptAsync("requestSync()");
                await WaitFor(view, "document.querySelector('.item[data-id=download0]')?.classList.contains('missing') === true", "Real handler did not detect the deleted file after cache expiry");
                downloads.Items[1].State = DownloadState.InProgress;
                await view.ExecuteScriptAsync("requestSync()");
                await WaitFor(view, "rowData.get('download1')?.state === 0", "Real state change was not delivered");
                await view.ExecuteScriptAsync("clearTimeout(timer);window.__auditNativeUpdates=[];window.__auditProgressButton=itemMap.get('download1').querySelector('.actions button');window.__auditProgressButton.focus();window.__auditOther=itemMap.get('download2')");
                downloads.Items[1].ReceivedBytes++;
                await view.ExecuteScriptAsync("requestSync()");
                await WaitFor(view, "window.__auditNativeUpdates.length > 0", "Real progress delta did not arrive");
                await CheckJs(view, "window.__auditNativeUpdates[0].changed.length === 1 && window.__auditNativeUpdates[0].changed[0].id === 'download1' && document.activeElement === window.__auditProgressButton && itemMap.get('download2') === window.__auditOther", "Real progress changed other rows or lost control focus");
                await Capture(view, "downloads-engine-" + retained + ".png");
                Results.Add(new { name = "downloads-real-engine-ipc", retainedRows = retained, visibleRows = 50,
                    unchangedNativePayloads = 0, oldRecordSearch = true, pagination = true, removeIdCorrect = true,
                    deletedFileRefresh = true, progressChangedRows = 1, focusStable = true,
                    scope = "actual TabEngine, registered document security, router, DownloadMessageHandler, real store and file details; reflection attaches an already initialized WebView" });
                await view.ExecuteScriptAsync("clearTimeout(timer)");

                // Drive saved-tab controls and routes through the actual handler without reloading the page.
                tab.Url = InternalUrls.OneTab;
                await LoadEnginePage(view, () => engine.NavigateToOneTab(tab, saved.Load()));
                var navigations = 0;
                void Navigating(object? sender, CoreWebView2NavigationStartingEventArgs args) => navigations++;
                view.CoreWebView2.NavigationStarting += Navigating;
                try
                {
                    await view.ExecuteScriptAsync("window.__auditStar=document.querySelector('.tab-star');window.__auditStar.focus();window.__auditScroll=scrollY;window.__auditStar.click()");
                    await WaitFor(view, "window.__auditStar.classList.contains('starred')", "Saved-tab star did not update through the real handler");
                    await CheckJs(view, "document.activeElement === window.__auditStar && scrollY === window.__auditScroll", "Saved-tab star lost focus or scroll");
                    await view.ExecuteScriptAsync("window.chrome.webview.postMessage(" + JsonSerializer.Serialize(engine.IpcToken + ":onetab-reorder-tab:first:0:2") + ")");
                    await WaitFor(view, "document.querySelectorAll('.group[data-group-id=first] .tab')[2].querySelector('.tab-star') === window.__auditStar", "Saved-tab reorder replaced or misplaced the row");
                    await CheckJs(view, "document.activeElement === window.__auditStar && window.__auditStar.closest('.tab').dataset.tabIndex === '2'", "Saved-tab reorder lost focus or retained a stale index");
                    // This click must use the new index rather than the generated inline index.
                    await view.ExecuteScriptAsync("window.__auditStar.click()");
                    await WaitFor(view, "!window.__auditStar.classList.contains('starred')", "Saved-tab star used a stale index after reorder");
                    Require(!saved.Load().Single(group => group.Id == "first").Tabs[2].IsStarred, "Reordered star changed the wrong saved tab");
                    await view.ExecuteScriptAsync("document.querySelector('.group[data-group-id=first] .tab-delete').click()");
                    await WaitFor(view, "document.querySelectorAll('.group[data-group-id=first] .tab').length === 2", "Saved-tab removal did not update rows");
                    Require(saved.Load().Single(group => group.Id == "first").Tabs[0].Title == "Other", "Duplicate URL removal changed its first-match behavior");
                    await view.ExecuteScriptAsync("window.chrome.webview.postMessage(" + JsonSerializer.Serialize(engine.IpcToken + ":onetab-reorder-group:0:1") + ")");
                    await WaitFor(view, "document.querySelector('.group').dataset.groupId === 'second'", "Saved-tab group reorder failed");
                    await view.ExecuteScriptAsync("window.chrome.webview.postMessage(" + JsonSerializer.Serialize(engine.IpcToken + ":onetab-rename:first:Renamed in live smoke") + ")");
                    await WaitFor(view, "document.querySelector('.group[data-group-id=first] .group-name').textContent === 'Renamed in live smoke'", "Saved-tab rename did not update the existing group");
                    Require(navigations == 0, "Saved-tab mutation navigated the complete page");
                    await Capture(view, "saved-tabs-engine-" + retained + ".png");
                    Results.Add(new { name = "saved-tabs-real-engine-ipc", star = true, tabReorder = true, groupReorder = true,
                        duplicateUrlRemoval = true, rename = true, focusStable = true, mutationsNavigated = navigations,
                        limitation = "Small saved-tab DOM fixture; 10,000-record mutation and restart coverage comes from store tests. Drag routes are invoked through IPC, not OS pointer dragging." });
                }
                finally { view.CoreWebView2.NavigationStarting -= Navigating; }
                if (routeFailure is not null) throw new InvalidOperationException("Real handler failed", routeFailure);
            }
            finally
            {
                engine.Dispose();
                Require(await downloads.FlushAsync(), "Engine smoke download flush failed");
                Require(await saved.FlushAsync(), "Engine smoke saved-tab flush failed");
                Require(await history.FlushAsync(), "Engine smoke history flush failed");
                view.Dispose();
                window.Close();
            }
        }
    }

    private static async Task LoadEnginePage(WebView2 view, Action navigate)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded(object? sender, CoreWebView2NavigationCompletedEventArgs args) => completed.TrySetResult(args.IsSuccess);
        view.CoreWebView2.NavigationCompleted += Loaded;
        try
        {
            navigate();
            Require(await completed.Task.WaitAsync(TimeSpan.FromSeconds(45)), "Engine internal document failed to navigate");
            await Task.Delay(350);
        }
        finally { view.CoreWebView2.NavigationCompleted -= Loaded; }
    }
}
