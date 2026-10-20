using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.Engine;
using StrideBrowser.Engine.Handlers;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.LinkPreview;

internal static class Verification
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try { await Run(args[0]); app.Shutdown(0); }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        };
        Environment.ExitCode = app.Run();
    }

    private static async Task Run(string output)
    {
        Directory.CreateDirectory(output);
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Path.Combine(output, "native.log")));
        System.Diagnostics.Trace.AutoFlush = true;
        var settings = new BrowserSettings { AppTheme = AppThemeMode.Dark, FaviconProxyEnabled = false };
        var store = new SettingsStore(Path.Combine(output, "settings.json"));
        var theme = new ThemeManager(settings);
        var pages = new InternalPages(theme);
        var downloads = new MemoryDownloads();
        var unhook = new YouTubeUnhook();
        var focus = new FocusBlocklistService(store);
        var hibernation = new TabHibernationManager(downloads, settings);
        var engine = new TabEngine(new EngineDependencies {
            Session=new BrowserSession(), ClosedTabStore=new ClosedTabStore(Path.Combine(output, "closed.json"), 50),
            Settings=settings, ThemeManager=theme, Pages=pages, ExtensionManager=new ExtensionManager(),
            YouTubeUnhook=unhook, YouTubeEnhancer=new YouTubeEnhancer(), FaviconLoader=new FaviconLoader(settings),
            HistoryStore=new MemoryHistory(), OneTabStore=new MemoryTabs(), DownloadStore=downloads,
            FocusBlocklistService=focus, ContentScriptInjector=new ContentScriptInjector(unhook),
            CustomDownloadManager=new CustomDownloadManager(), HibernationManager=hibernation,
            NavigationPolicyEngine=new NavigationPolicyEngine(focus), DownloadSuppressor=new LinkPreviewDownloadSuppressor()
        });
        var handler = new SettingsMessageHandler(engine, settings, store, new UpdateService(new System.Net.Http.HttpClient()), hibernation);
        var router = new WebMessageRouter(new[] { handler });
        engine.WebMessageReceived += async message => await router.RouteAsync(message);
        var webview = new WebView2();
        var window = new Window { Content=webview, Width=1100, Height=800, Left=-12000, Top=-12000, ShowInTaskbar=false, ShowActivated=false };
        window.Show();
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(output, "profile"));
        await webview.EnsureCoreWebView2Async(environment);
        var tab = engine.CreateTab(InternalUrls.Settings);
        var views = (Dictionary<Guid, dynamic>)typeof(TabEngine).GetField("_webViews", BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(engine)!;
        views[tab.Id] = webview;
        var bridge = (WebViewIpcBridge)typeof(TabEngine).GetField("_ipcBridge", BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(engine)!;
        typeof(TabEngine).GetMethod("WireTabEvents", BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(engine, new object[] { webview, tab });
        await Load(() => engine.NavigateToSettings(tab, settings), webview.CoreWebView2);
        await WaitFor(async () => await JsBool(webview, "!!document.querySelector('[data-setting-key]')"));
        var keys = JsonSerializer.Deserialize<string[]>(await webview.ExecuteScriptAsync("Array.from(document.querySelectorAll('input[type=checkbox][data-setting-key]'), el => el.dataset.settingKey)"))!;
        var verified = 0;
        foreach (var key in keys.Where(k => k != "adBlock"))
        {
            foreach (var value in new[] { false, true })
            {
                var literal = JsonSerializer.Serialize(key);
                await webview.ExecuteScriptAsync($"(() => {{ const el=document.querySelector('[data-setting-key='+{literal}+']'); el.checked={value.ToString().ToLowerInvariant()}; el.dispatchEvent(new Event('change',{{bubbles:true}})); }})()");
                try { await WaitFor(async () => await JsBool(webview, "document.getElementById('settingsStatus').textContent.startsWith('Saved')")); }
                catch { Console.WriteLine("Failed key=" + key + " status=" + await webview.ExecuteScriptAsync("document.getElementById('settingsStatus').textContent")); throw; }
                if (!File.Exists(Path.Combine(output, "settings.json"))) throw new Exception("Settings did not persist.");
                var persisted = store.Load();
                var comparison = JsonSerializer.Serialize(settings);
                if (JsonSerializer.Serialize(persisted) != comparison) throw new Exception("Saved values differ for " + key);
                verified++;
            }
        }
        await Load(() => engine.NavigateToSettings(tab, settings), webview.CoreWebView2);
        await WaitFor(async () => await JsBool(webview, "document.querySelectorAll('[data-setting-key]').length > 0"));
        await webview.ExecuteScriptAsync("document.querySelector('[data-setting-key=autoCheckForUpdates]').click()");
        await WaitFor(async () => await JsBool(webview, "document.getElementById('settingsStatus').textContent === 'Saved.'"));
        verified++;
        var before = store.Load().AutoCheckForUpdates;
        await Load(() => webview.CoreWebView2.NavigateToString("<html><body>Unregistered document</body></html>"), webview.CoreWebView2);
        await webview.ExecuteScriptAsync("window.chrome.webview.postMessage(" + JsonSerializer.Serialize(engine.IpcToken + ":setting:autoCheckForUpdates:" + (!before).ToString().ToLowerInvariant()) + ")");
        await Task.Delay(200);
        if (store.Load().AutoCheckForUpdates != before) throw new Exception("Unregistered document was accepted.");
        await Load(() => engine.NavigateToSettings(tab, settings), webview.CoreWebView2);
        await webview.ExecuteScriptAsync("document.querySelector('[data-setting-key=autoCheckForUpdates]').click()");
        await WaitFor(async () => await JsBool(webview, "document.getElementById('settingsStatus').textContent === 'Saved.'"));
        verified++;

        var history = new List<HistoryEntry> { new() { Url="https://example.com/docs", Title="Example documentation" } };
        var saved = new List<OneTabGroup> { new() { Name="Research", Tabs=[new("https://example.com/", "Example page", null, DateTime.UtcNow)] } };
        foreach (var mode in new[] { AppThemeMode.Dark, AppThemeMode.Light })
        {
            settings.AppTheme=mode;
            var html = new Dictionary<string,string> {
                ["settings"]=pages.SettingsPage(settings,"verification"),
                ["newtab"]=pages.NewTabPage([],settings.AccentColor,InternalPages.HexToRgb(settings.AccentColor),"verification",100,"",false),
                ["history"]=pages.HistoryPage(history,settings.AccentColor,InternalPages.HexToRgb(settings.AccentColor),"verification",false),
                ["downloads"]=pages.DownloadsPage([],settings.AccentColor,InternalPages.HexToRgb(settings.AccentColor),"verification"),
                ["saved-tabs"]=pages.OneTabPage(saved,settings.AccentColor,InternalPages.HexToRgb(settings.AccentColor),"verification"),
                ["onboarding"]=pages.OnboardingPage(settings,settings.AccentColor,InternalPages.HexToRgb(settings.AccentColor),"verification"),
                ["release-notes"]=pages.ReleaseNotesPage("1.2.2",ReleaseNotesProvider.GetAllReleases(),settings.AccentColor,InternalPages.HexToRgb(settings.AccentColor),"verification"),
                ["focus"]=pages.FocusPage("blocked.example", "verification"),
                ["health"]=pages.HealthPage("1.2.2",new ExtensionManager.ExtensionHealth(true,true,true,false,"test"),"verification"),
                ["error"]=pages.ErrorPage("https://offline.example","The connection could not be completed.",settings.AccentColor,InternalPages.HexToRgb(settings.AccentColor))
            };
            foreach (var (name, content) in html)
                File.WriteAllText(Path.Combine(output,name+"-"+mode.ToString().ToLowerInvariant()+".html"),content);
        }
        hibernation.Detach();
        webview.Dispose(); window.Close();
        Console.WriteLine($"PASS: {verified} live setting changes, reopen, unregistered document rejection, registered document recovery. Exported 20 rendered pages.");
    }

    private static async Task Load(Action navigate, CoreWebView2 core)
    {
        var completion = new TaskCompletionSource<bool>();
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) { completion.TrySetResult(e.IsSuccess); }
        core.NavigationCompleted += Completed;
        try { navigate(); if (!await completion.Task.WaitAsync(TimeSpan.FromSeconds(15))) throw new Exception("Navigation failed."); }
        finally { core.NavigationCompleted -= Completed; }
    }
    private static async Task<bool> JsBool(WebView2 webview,string expression) => await webview.ExecuteScriptAsync(expression) == "true";
    private static async Task WaitFor(Func<Task<bool>> predicate)
    {
        for(var i=0;i<100;i++) { if(await predicate()) return; await Task.Delay(50); }
        throw new Exception("Timed out waiting for the browser to confirm the setting.");
    }

    private sealed class MemoryDownloads : IDownloadStore
    {
        public ObservableCollection<DownloadItem> Items {get;}=[];
        public void Add(DownloadItem item)=>Items.Add(item);
        public void Remove(string id) { }
        public void ClearCompleted() { }
        public void ClearAll()=>Items.Clear();
        public DownloadItem? Get(string id)=>null;
    }
    private sealed class MemoryHistory : IHistoryStore
    {
        public void Add(string url,string title) { }
        public List<HistoryEntry> Load()=>[];
        public List<HistoryEntry> Search(string query)=>[];
        public bool Clear() => true;
        public List<HistoryEntry> GetFrequentSites(int count=8)=>[];
    }
    private sealed class MemoryTabs : IOneTabStore
    {
        public List<OneTabGroup> Load()=>[];
        public void Save(List<OneTabGroup> groups) { }
        public void AddGroup(OneTabGroup group) { }
        public void RemoveGroup(string groupId) { }
        public void RemoveTab(string groupId,string url) { }
        public void ToggleStar(string groupId,int tabIndex) { }
        public void ReorderGroup(int oldIndex,int newIndex) { }
        public void ReorderTab(string groupId,int oldIndex,int newIndex) { }
    }
}
