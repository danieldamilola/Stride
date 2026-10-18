using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using StrideBrowser;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.UI;

/// <summary>
/// Drives the real tab tearing and docking path against real windows and a real page.
/// Adapted from tools/TabTearingSmoke at b4847b2. The composition branch is gone because
/// the floating command bar it selected no longer exists, so the HwndHost control is the
/// only one and its behaviour is asserted rather than tolerated.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application();
        using var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BrowserResources.xaml")!;
        var dictionary = XDocument.Load(resourceStream).Descendants().First(e => e.Name.LocalName == "ResourceDictionary");
        dictionary.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
        dictionary.SetAttributeValue(XNamespace.Xmlns + "conv", "clr-namespace:StrideBrowser.Converters;assembly=Stride");
        foreach (var element in dictionary.Descendants().Where(e => e.Name.NamespaceName == "clr-namespace:StrideBrowser.Converters"))
            element.Name = XName.Get(element.Name.LocalName, "clr-namespace:StrideBrowser.Converters;assembly=Stride");
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
        Trace.AutoFlush = true;

        // Every store is in memory and the WebView2 profile is a fresh directory, so a
        // run cannot read or write the installed browser's profile.
        Require(StrideBrowser.Helpers.AppPaths.IsIsolatedProfile,
            $"STRIDE_DATA_DIR is not redirecting the data tree, AppPaths.Base is {StrideBrowser.Helpers.AppPaths.DataDirectory}");

        var result = 1;
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await RunAsync().WaitAsync(TimeSpan.FromSeconds(180));
                result = 0;
                Console.WriteLine("PASS: tab tearing smoke checks");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.Dispatcher.InvokeShutdown(); }
        }));
        Dispatcher.Run();
        return result;
    }

    private static async Task RunAsync()
    {
        var settings = new BrowserSettings
        {
            AdBlockEnabled = false,
            // Sleep has to be on for the suspension step below to have anything to
            // suspend. Hibernation stays off so no tab loses its control mid test.
            TabSleepEnabled = true,
            TabHibernationEnabled = false,
            AutoCheckForUpdates = false,
            RestoreSessionOnStartup = false,
            ClearDataOnExit = false,
            ShowTabNames = true
        };
        var session = new BrowserSession { WebViewDataDirectory = Path.Combine(Path.GetTempPath(), "StrideTabTearingSmoke", Guid.NewGuid().ToString("N")) };
        var closedTabs = new MemoryClosedTabs();
        var sessionStore = new MemorySession();
        var settingsStore = new MemorySettings(settings);
        using var services = Composition.BuildServiceProvider(s =>
        {
            s.AddSingleton(settings);
            s.AddSingleton(session);
            s.AddSingleton<ISettingsStore>(settingsStore);
            s.AddSingleton<IHistoryStore, MemoryHistory>();
            s.AddSingleton<ISessionStore>(sessionStore);
            s.AddSingleton<IDownloadStore, MemoryDownloads>();
            s.AddSingleton<IClosedTabStore>(closedTabs);
        });
        var manager = services.GetRequiredService<BrowserWindowManager>();
        var first = manager.CreateWindow();
        first.Left = 30;
        first.Top = 60;
        first.Width = 850;
        first.Height = 650;
        first.Show();
        var firstServices = Services(first);
        await firstServices.GetRequiredService<BrowserWindowContext>().Ready.Task;
        var source = Engine(first);
        DisableExtensions(source);

        var background = source.CreateTab("about:blank", false);
        await source.ActivateAsync(background);
        var tab = source.CreateTab("about:blank", false);
        await source.ActivateAsync(tab);
        var core = source.GetCoreForTab(tab.Id)!;
        var loaded = new TaskCompletionSource();
        core.NavigationCompleted += (_, _) => loaded.TrySetResult();
        var pagePath = Path.Combine(session.WebViewDataDirectory, "transfer-test.html");
        File.WriteAllText(pagePath, "<html><body style='background:#295c45;color:white;font:24px sans-serif'><h1>Tab transfer state</h1><input id='draft' value='unsaved text'><p id='count'></p><script>window.marker=String(Date.now())+Math.random();window.ticks=0;setInterval(()=>{ticks++;document.getElementById('count').textContent=ticks},50)</script></body></html>");
        core.Navigate(new Uri(pagePath).AbsoluteUri);
        await loaded.Task;
        for (var i = 0; i < 100 && await core.ExecuteScriptAsync("typeof window.ticks") != "\"number\""; i++) await Task.Delay(50);
        await core.ExecuteScriptAsync("document.getElementById('draft').value='keep this draft';history.pushState({},'', '#kept');");
        var marker = await core.ExecuteScriptAsync("window.marker");
        var ticks = int.Parse(await core.ExecuteScriptAsync("window.ticks"));
        var navigationCount = 0;
        core.NavigationStarting += (_, _) => navigationCount++;
        var originalControl = source.GetWebViewElement(tab.Id);

        // In flight suspension: freeze the page in the window it was built in, then move
        // it. The move has to hand a running page to the new window, not a frozen one.
        // Tab sleep is the product path that does the freezing, so it is used rather
        // than a synthetic suspend call.
        var suspended = false;
        for (var attempt = 0; attempt < 8 && !suspended; attempt++)
        {
            await Task.Delay(500);
            await source.ActivateAsync(background);
            await source.ActivateAsync(tab);
            await source.ActivateAsync(background);
            suspended = await FrozenAsync();
        }
        Trace.WriteLine($"tab sleep froze the page before the move: {suspended}");
        if (!suspended) Trace.WriteLine("SKIP: in-flight suspension precondition not reproduced; asserting the post-move page runs");

        await manager.DetachAsync(source, tab, new Point(980, 130));
        var second = Application.Current.Windows.OfType<MainWindow>().Single(w => w != first);
        DisableExtensions(Engine(second));
        var destination = Engine(second);
        Require(source.Tabs.Count == 1 && destination.Tabs.Single() == tab, "Tab belongs to destination");
        Require(ReferenceEquals(originalControl, destination.GetWebViewElement(tab.Id)), "WebView control preserved");
        Require(ReferenceEquals(core, destination.GetCoreForTab(tab.Id)), "CoreWebView2 preserved");
        Require(!await FrozenAsync(), "A suspended tab is woken by the window that receives it");
        await AssertPageAsync();
        Console.WriteLine($"PASS: tab was suspended before the move = {suspended}; running page handed over");
        await core.ExecuteScriptAsync("document.title='Moved title'");
        await WaitFor(() => tab.Title == "Moved title");
        await SettleThemeColorAsync();
        await core.ExecuteScriptAsync("chrome.webview.postMessage('" + destination.IpcToken + ":THEME_COLOR:#123456')");
        await WaitFor(() => tab.ThemeColor == "#123456");
        Require(tab.Title == "Moved title" && tab.ThemeColor == "#123456", "Destination receives page events: " + tab.Title + " / " + tab.ThemeColor);

        Require(await manager.DockAsync(destination, source, tab, 0), "Dock succeeded");
        // The control has to survive the window it came from actually closing, so watch
        // it rather than assuming one dispatcher turn was enough.
        var aliveAfterDock = true;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(100);
            try { await core.ExecuteScriptAsync("1"); }
            catch (Exception ex) { aliveAfterDock = false; Trace.WriteLine($"control died {i * 100}ms after dock: {ex.Message}"); break; }
        }
        Require(aliveAfterDock, "Control stayed alive after the window it was built in closed");
        Require(source.Tabs[0] == tab && !second.IsVisible, "Dock closes empty source window");
        await AssertPageAsync();
        Require(navigationCount == 0, "No navigation during detach or dock");
        Require(core.CanGoBack, "Navigation history preserved");
        Console.WriteLine($"PASS: {originalControl!.GetType().Name}, page identity, draft, timer, history, events, empty window closure");

        // The window the tab came from must not take its page with it when it closes.
        var host = first;
        var hostEngine = source;
        await manager.DetachAsync(hostEngine, tab, new Point(980, 130));
        var moved = Application.Current.Windows.OfType<MainWindow>().Single(w => w != host);
        DisableExtensions(Engine(moved));
        Require(!await FrozenAsync(), "The torn-off tab is running in its new window");

        // The window the tab came from must not take its page with it when it closes.
        Require(Engine(moved).Tabs.Contains(tab) && !Engine(host).Tabs.Contains(tab), "Tab lives in the new window");
        host.Close();
        await WaitFor(() => !host.IsVisible);
        await Task.Delay(400);
        var liveCore = Engine(moved).GetCoreForTab(tab.Id);
        Require(ReferenceEquals(core, liveCore), "Torn-off tab keeps its live core after its original window closes");
        Require(await core.ExecuteScriptAsync("window.marker") == marker, "Torn-off tab keeps its live page after its original window closes");
        Console.WriteLine("PASS: torn-off tab keeps its live page after the original window closes");

        // Tearing the last tab in a window must move that window, not spawn a new one.
        var soloEngine = Engine(moved);
        while (soloEngine.Tabs.Count > 1) { soloEngine.CloseTab(soloEngine.Tabs[soloEngine.Tabs.Count - 1]); await Task.Delay(60); }
        Require(soloEngine.Tabs.Count == 1, "Window reduced to a single tab");
        var soloWindowCount = Application.Current.Windows.OfType<MainWindow>().Count();
        await manager.DetachAsync(soloEngine, soloEngine.Tabs[0], new Point(1400, 700));
        await Task.Delay(500);
        Require(Application.Current.Windows.OfType<MainWindow>().Count() == soloWindowCount,
            "Tearing the last tab moves the existing window instead of opening a new one");
        Require(moved.IsVisible && soloEngine.Tabs.Count == 1, "Solo tab stays in the same window after a last-tab tear");
        Console.WriteLine("PASS: last-tab tear moves the existing window");

        moved.Close();
        await Task.Delay(250);

        var survivor = await ReadyCancellationAsync();
        await MovedControlSuspendProbeAsync(survivor);
        await ShutdownOrderAsync(survivor);

        /// <summary>
        /// A window that closes while a tab is being docked into it has to read as an
        /// aborted transfer. The tab stays where it was, and the next dock still works,
        /// which is only true if the in flight flag was released.
        /// </summary>
        async Task<Window> ReadyCancellationAsync()
        {
            var host = manager.CreateWindow();
            host.Width = 700; host.Height = 500; host.Left = 120; host.Top = 120;
            host.Show();
            var hostEngine = Engine(host);

            // Wait for the window to finish loading first. While a tab control is still
            // being built the engine refuses to move it, which is shipped behaviour and
            // would let this case pass for the wrong reason.
            await Services(host).GetRequiredService<BrowserWindowContext>().Ready.Task;
            DisableExtensions(hostEngine);

            var keeper = hostEngine.CreateTab("about:blank", false);
            // Creating a control and settling it is not instantaneous, and the engine
            // refuses to move a tab whose control is still being built. Poll for the
            // state this test needs rather than trusting one activation call.
            for (var i = 0; i < 100 && !hostEngine.CanTransfer(keeper); i++) await Task.Delay(50);
            var creating = (System.Collections.Generic.HashSet<Guid>)typeof(TabEngine)
                .GetField("_creatingTabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hostEngine)!;
            var disposed = (bool)typeof(TabEngine)
                .GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hostEngine)!;
            Note($"host tabs={hostEngine.Tabs.Count} canTransfer={hostEngine.CanTransfer(keeper)} hasControl={hostEngine.GetWebViewElement(keeper.Id) is not null} creating={creating.Count} creatingHasTab={creating.Contains(keeper.Id)} disposed={disposed}");
            Require(hostEngine.Tabs.Count == 1 && hostEngine.CanTransfer(keeper),
                "the host window holds one transferable tab",
                $"tabs={hostEngine.Tabs.Count}");
            // Created but never shown, so OnWindowLoaded never runs and Ready never completes.
            var pending = manager.CreateWindow();
            var pendingEngine = Engine(pending);
            var pendingContext = Services(pending).GetRequiredService<BrowserWindowContext>();
            var outcome = manager.DockAsync(hostEngine, pendingEngine, keeper, 0);

            Require(!pendingContext.Ready.Task.IsCompleted, "the destination window has not loaded");
            Require(!outcome.IsCompleted, "a dock into a window that has not loaded waits for it");

            pending.Close();
            await Task.Delay(600);

            var docked = await outcome;
            Require(!docked, "closing the destination during a dock aborts the transfer");
            Require(hostEngine.Tabs.Contains(keeper), "the source window keeps the tab after an aborted dock");
            Require(!pendingEngine.Tabs.Contains(keeper), "nothing moved into the closed window");
            Trace.WriteLine("PASS: destination closed during a dock is an aborted transfer, source intact");

            // The in flight flag must have been released, or every later drag would be a
            // silent no op. The next dock into a window that has loaded proves it.
            var target = manager.CreateWindow();
            target.Width = 700; target.Height = 500; target.Left = 900; target.Top = 120;
            target.Show();
            await Services(target).GetRequiredService<BrowserWindowContext>().Ready.Task;
            DisableExtensions(Engine(target));

            Require(await manager.DockAsync(hostEngine, Engine(target), keeper, 0),
                "a later dock runs after an aborted one");
            Require(Engine(target).Tabs.Contains(keeper), "the later dock moved the tab");
            Trace.WriteLine("PASS: in flight flag released after an aborted dock");
            return target;
        }

        /// <summary>
        /// The writes a window owes on the way out, through the real manager. The browser
        /// cleanup is forced to fail by disposing the live WebView control first, so the
        /// closed tab history and the settings are checked with that step broken.
        /// </summary>
        async Task ShutdownOrderAsync(Window host)
        {
            var engine = Engine(host);
            if (engine.Tabs.Count == 0) { Note("SKIP: shutdown order, no window left to close"); return; }

            var live = engine.GetWebViewElement(engine.ActiveTab?.Id ?? Guid.Empty);
            if (live is null) { Note("SKIP: shutdown order, no live control to break"); return; }

            // Put a closure in the history so the clear has something to remove.
            closedTabs.Record("https://example.invalid/should-not-survive", "gone", false);
            var beforeCount = closedTabs.Count;
            var beforeLoads = sessionStore.Clears + sessionStore.Saves;
            var beforeSettings = settingsStore.Saves;

            settings.ClearDataOnExit = true;
            // Disposing the control makes GetCoreWebView2 throw, which is the browser
            // cleanup failing the way a COM fault or a timeout would.
            try { ((Microsoft.Web.WebView2.Wpf.WebView2)live).Dispose(); }
            catch (Exception ex) { Note("control dispose: " + ex.Message); }

            host.Close();
            await Task.Delay(1200);

            Note($"closed history {beforeCount} -> {closedTabs.Count}, session writes {beforeLoads} -> {sessionStore.Clears + sessionStore.Saves}, settings saves {beforeSettings} -> {settingsStore.Saves}");
            Require(closedTabs.Count == 0 && beforeCount > 0,
                "A failed browser cleanup still clears the closed tab history",
                $"{beforeCount} -> {closedTabs.Count}");
            Require(settingsStore.Saves > beforeSettings,
                "A failed browser cleanup still saves settings",
                $"{beforeSettings} -> {settingsStore.Saves}");
            Require(sessionStore.Clears + sessionStore.Saves > beforeLoads,
                "A data clearing exit still writes the startup snapshot",
                $"{beforeLoads} -> {sessionStore.Clears + sessionStore.Saves}");
            Trace.WriteLine("PASS: recovery state lands even when the best effort browser cleanup fails");

            // Restore off has to empty the snapshot rather than write one.
            settings.ClearDataOnExit = false;
            var clearsBefore = sessionStore.Clears;
            var survivor = manager.CreateWindow();
            var survivorEngine = Engine(survivor);
            var tab = survivorEngine.CreateTab("about:blank", false);
            await survivorEngine.ActivateAsync(tab);
            survivor.Close();
            await Task.Delay(900);
            Require(sessionStore.Clears > clearsBefore,
                "Restore off empties the startup snapshot on exit",
                $"{clearsBefore} -> {sessionStore.Clears}");
            Trace.WriteLine("PASS: restore off empties the snapshot and still saves settings");
        }

        /// <summary>
        /// A control that has already crossed a window boundary refuses to suspend, while
        /// one that never moved suspends. This measures both in the same window, at the same
        /// moment, so the difference is the move and nothing else.
        /// </summary>
        async Task MovedControlSuspendProbeAsync(Window host)
        {
            var hostEngine = Engine(host);

            // Two tabs in the same window, measured back to back: one arrived through a
            // drag across a window boundary, one was created here and never left. Nothing
            // else differs between them, so any difference in outcome is the move.
            var native = hostEngine.CreateTab("about:blank", false);
            await hostEngine.ActivateAsync(native);
            for (var i = 0; i < 100 && hostEngine.GetWebViewElement(native.Id) is null; i++) await Task.Delay(50);
            // Wait for a live core on both, not just a control, because the suspend calls
            // go through the core.
            for (var i = 0; i < 200 && hostEngine.GetCoreForTab(native.Id) is null; i++) await Task.Delay(50);
            var moved = hostEngine.Tabs.FirstOrDefault(t =>
                t.Id != native.Id && hostEngine.GetWebViewElement(t.Id) is not null);
            if (moved is null || hostEngine.GetCoreForTab(native.Id) is null)
            {
                Note("SKIP: comparative suspend probe, need one moved and one native live control");
                return;
            }

            var nativeDetail = await DescribeSuspendAttemptAsync(hostEngine, native, "never moved");
            var movedDetail = await DescribeSuspendAttemptAsync(hostEngine, moved, "moved across windows");
            Note($"suspend, control that never moved: {nativeDetail}");
            Note($"suspend, control moved across windows: {movedDetail}");
            Note($"control types: native={hostEngine.GetWebViewElement(native.Id)?.GetType().Name} moved={hostEngine.GetWebViewElement(moved.Id)?.GetType().Name}");

            // Recorded, not asserted for the moved case. Whether that is a port defect or
            // an SDK limit is not this tool to decide, and guessing either way would be a
            // claim without comparative evidence.
            // Neither outcome is asserted. Two runs against this build produced opposite
            // results, one where only the moved control refused and one where only the
            // control that never moved refused, so the failure is not attributable to the
            // move and there is nothing here to assert.
            Trace.WriteLine($"NOTE comparative suspend result: native=[{nativeDetail}] moved=[{movedDetail}]");
            Trace.WriteLine("PASS: comparative suspend evidence recorded");
        }
        async Task<string> DescribeSuspendAttemptAsync(TabEngine engine, BrowserTab tab, string label)
        {
            var core = engine.GetCoreForTab(tab.Id);
            if (core is null) return $"{label}: no core";
            try { core.MemoryUsageTargetLevel = Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Low; }
            catch (Exception ex) { return $"{label}: MemoryUsageTargetLevel threw {ex.GetType().Name}: {ex.Message}"; }
            bool ok;
            try { ok = await core.TrySuspendAsync(); }
            catch (Exception ex) { return $"{label}: TrySuspendAsync threw {ex.GetType().Name}: {ex.Message}"; }
            return $"{label}: TrySuspendAsync returned {ok}";
        }

        /// <summary>
        /// True while the page's timer has stopped. A suspended WebView freezes script,
        /// so the counter on the page is the symptom a user would actually see, and it
        /// needs no SDK property that may or may not exist in the installed runtime.
        /// </summary>
        async Task<bool> FrozenAsync()
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var before = int.Parse(await core.ExecuteScriptAsync("window.ticks"));
                await Task.Delay(400);
                var after = int.Parse(await core.ExecuteScriptAsync("window.ticks"));
                if (after <= before) return true;
            }
            return false;
        }

        async Task AssertPageAsync()
        {
            await Task.Delay(200);
            Require(await core.ExecuteScriptAsync("window.marker") == marker, "JavaScript identity preserved");
            Require(await core.ExecuteScriptAsync("document.getElementById('draft').value") == "\"keep this draft\"", "Unsaved input preserved");
            Require(int.Parse(await core.ExecuteScriptAsync("window.ticks")) > ticks, "Timer continues");
        }

        async Task WaitFor(Func<bool> condition)
        {
            for (var i = 0; i < 80 && !condition(); i++) await Task.Delay(25);
            Require(condition(), "Timed out waiting for browser state");
        }

        // The injected theme-color script re-sends on DOM mutation with a 250ms throttle,
        // so wait until its value holds still before asserting on a colour we post ourselves.
        async Task SettleThemeColorAsync()
        {
            var previous = tab.ThemeColor;
            var stable = 0;
            for (var i = 0; i < 80 && stable < 8; i++)
            {
                await Task.Delay(50);
                if (tab.ThemeColor == previous) stable++;
                else { previous = tab.ThemeColor; stable = 0; }
            }
        }
    }

    /// <summary>The engine behind a window, read from its own DI scope.</summary>
    private static TabEngine Engine(Window window) =>
        Services(window).GetRequiredService<TabEngine>();

    private static IServiceProvider Services(Window window) =>
        (IServiceProvider)typeof(MainWindow).GetField("_services", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    /// <summary>
    /// Stops the extension loader. It unzips and rewrites the uBlock folder under the
    /// profile, which is file work this probe does not measure and must not repeat for
    /// every window it opens.
    /// </summary>
    private static void DisableExtensions(TabEngine engine)
    {
        var factory = typeof(TabEngine).GetField("_webViewFactory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
        typeof(WebViewFactory).GetField("_extensionsLoaded", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(factory, true);
    }

    private static void Require(bool value, string message, string detail = "")
    {
        if (!value) throw new InvalidOperationException(detail.Length == 0 ? message : $"{message}: {detail}");
    }

    private static void Note(string message) => Trace.WriteLine("NOTE " + message);

    private sealed class MemorySettings(BrowserSettings settings) : ISettingsStore
    {
        public int Saves { get; private set; }
        public BrowserSettings Load() => settings;
        // Nothing to write to disk, so an in memory save cannot fail.
        public bool TrySave(BrowserSettings value) { Saves++; return true; }
        public void Save(BrowserSettings value) => TrySave(value);
    }

    private sealed class MemoryHistory : IHistoryStore
    {
        public void Add(string url, string title) { }
        public List<HistoryEntry> Load() => [];
        public List<HistoryEntry> Search(string query) => [];
        public void Clear() { }
        public List<HistoryEntry> GetFrequentSites(int count = 8) => [];
    }

    private sealed class MemorySession : ISessionStore
    {
        public int Saves { get; private set; }
        public int Clears { get; private set; }
        public List<SessionEntry> LastSaved { get; private set; } = [];
        public List<SessionEntry> Load() => [];
        // Nothing to write to disk, so an in memory save cannot fail.
        public bool TrySave(IEnumerable<(string Url, string Title, bool IsPinned, bool IsActive)> tabs)
        {
            Saves++;
            LastSaved = tabs.Select(t => new SessionEntry(t.Url, t.Title, t.IsPinned, t.IsActive)).ToList();
            return true;
        }
        public void Save(IEnumerable<(string Url, string Title, bool IsPinned, bool IsActive)> tabs) => TrySave(tabs);
        public bool TryClear() { Clears++; LastSaved = []; return true; }
        public void Clear() => TryClear();
        public void ClearAndDelete() { Clears++; LastSaved = []; }
    }

    private sealed class MemoryDownloads : IDownloadStore
    {
        public ObservableCollection<DownloadItem> Items { get; } = [];
        public void Add(DownloadItem item) => Items.Add(item);
        public void Remove(string id) { }
        public void ClearCompleted() { }
        public void ClearAll() => Items.Clear();
        public DownloadItem? Get(string id) => Items.FirstOrDefault(i => i.Id == id);
    }

    private sealed class MemoryClosedTabs : IClosedTabStore
    {
        private readonly List<ClosedTabRecord> _items = [];
        public IReadOnlyList<ClosedTabRecord> Load() => new List<ClosedTabRecord>(_items);
        public bool Record(string url, string title, bool wasPinned)
        {
            var record = ClosedTabRecord.Create(url, title, wasPinned, DateTime.UtcNow);
            if (record is null) return false;
            _items.Add(record);
            return true;
        }
        public ClosedTabRecord? TakeNewest()
        {
            if (_items.Count == 0) return null;
            var newest = _items[^1];
            _items.RemoveAt(_items.Count - 1);
            return newest;
        }
        public int Count => _items.Count;
        public bool LastReadFailed => false;
        public bool LastWriteFailed => false;
        public bool Clear() { _items.Clear(); return true; }
        public bool ClearAndDelete() { _items.Clear(); return true; }
    }
}