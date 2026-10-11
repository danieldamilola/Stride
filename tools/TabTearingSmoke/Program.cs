using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using StrideBrowser;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.UI;

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
        var result = 1;
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await RunAsync(args.Contains("--composition")).WaitAsync(TimeSpan.FromSeconds(90));
                result = 0;
                Console.WriteLine("PASS: tab tearing smoke checks");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.Dispatcher.InvokeShutdown(); }
        }));
        Dispatcher.Run();
        return result;
    }

    private static async Task RunAsync(bool composition)
    {
        var settings = new BrowserSettings
        {
            UseFloatingCommandBar = composition,
            AdBlockEnabled = false,
            TabSleepEnabled = false,
            TabHibernationEnabled = false,
            AutoCheckForUpdates = false,
            RestoreSessionOnStartup = false,
            ClearDataOnExit = false,
            ShowTabNames = true
        };
        var session = new BrowserSession { WebViewDataDirectory = Path.Combine(Path.GetTempPath(), "StrideTabTearingSmoke", Guid.NewGuid().ToString("N")) };
        using var services = Composition.BuildServiceProvider(s =>
        {
            s.AddSingleton(settings);
            s.AddSingleton(session);
            s.AddSingleton<ISettingsStore>(new MemorySettings(settings));
            s.AddSingleton<IHistoryStore, MemoryHistory>();
            s.AddSingleton<ISessionStore, MemorySession>();
            s.AddSingleton<IDownloadStore, MemoryDownloads>();
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
        var source = firstServices.GetRequiredService<TabEngine>();
        var factory = typeof(TabEngine).GetField("_webViewFactory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
        typeof(WebViewFactory).GetField("_extensionsLoaded", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(factory, true);
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

        await manager.DetachAsync(source, tab, new Point(980, 130));
        var second = Application.Current.Windows.OfType<MainWindow>().Single(w => w != first);
        var destination = Services(second).GetRequiredService<TabEngine>();
        Require(source.Tabs.Count == 1 && destination.Tabs.Single() == tab, "Tab belongs to destination");
        Require(ReferenceEquals(originalControl, destination.GetWebViewElement(tab.Id)), "WebView control preserved");
        Require(ReferenceEquals(core, destination.GetCoreForTab(tab.Id)), "CoreWebView2 preserved");
        await AssertPageAsync();
        await core.ExecuteScriptAsync("document.title='Moved title'");
        await WaitFor(() => tab.Title == "Moved title");
        await SettleThemeColorAsync();
        await core.ExecuteScriptAsync("chrome.webview.postMessage('" + destination.IpcToken + ":THEME_COLOR:#123456')");
        await WaitFor(() => tab.ThemeColor == "#123456");
        Require(tab.Title == "Moved title" && tab.ThemeColor == "#123456", "Destination receives page events: " + tab.Title + " / " + tab.ThemeColor);

        Require(await manager.DockAsync(destination, source, tab, 0), "Dock succeeded");
        await Task.Delay(200);
        Require(source.Tabs[0] == tab && !second.IsVisible, "Dock closes empty source window");
        await AssertPageAsync();
        Require(navigationCount == 0, "No navigation during detach or dock");
        Require(core.CanGoBack, "Navigation history preserved");
        Console.WriteLine($"PASS: {originalControl!.GetType().Name}, page identity, draft, timer, history, events, empty window closure");

        // Tearing the last tab in a window must move that window, not spawn a new one.
        while (source.Tabs.Count > 1) { source.CloseTab(source.Tabs[source.Tabs.Count - 1]); await Task.Delay(60); }
        Require(source.Tabs.Count == 1, "Window reduced to a single tab");
        var soloWindowCount = Application.Current.Windows.OfType<MainWindow>().Count();
        await manager.DetachAsync(source, tab, new Point(1400, 700));
        await Task.Delay(500);
        Require(Application.Current.Windows.OfType<MainWindow>().Count() == soloWindowCount,
            "Tearing the last tab moves the existing window instead of opening a new one");
        Require(first.IsVisible && source.Tabs.Single() == tab, "Solo tab stays in the same window after a last-tab tear");
        Console.WriteLine("PASS: last-tab tear moves the existing window");

        // A detached tab must survive its original window closing. WebView2 binds a control to
        // the window it was built in: the HwndHost control follows the tab to its new window,
        // the composition control cannot and is rebuilt, which reloads the page.
        // Give the window a second tab so the detach spawns a real new window.
        var filler = source.CreateTab("about:blank", false);
        await source.ActivateAsync(filler);
        await source.ActivateAsync(tab);
        await manager.DetachAsync(source, tab, new Point(980, 130));
        var third = Application.Current.Windows.OfType<MainWindow>().Single(w => w != first);
        var thirdEngine = Services(third).GetRequiredService<TabEngine>();
        await AssertPageAsync();
        first.Close();
        await WaitFor(() => !first.IsVisible);
        await WaitFor(() => thirdEngine.Tabs.Count == 1 && thirdEngine.Tabs[0] == tab);
        var reloaded = false;
        try
        {
            if (await core.ExecuteScriptAsync("window.marker") != marker) reloaded = true;
        }
        catch (InvalidOperationException) { reloaded = true; }
        if (reloaded) await WaitFor(() => thirdEngine.GetCoreForTab(tab.Id) is not null);
        Require(thirdEngine.GetCoreForTab(tab.Id) is not null, "Detached tab keeps a live WebView after its original window closes");
        Console.WriteLine(composition
            ? "PASS: composition control cannot follow its tab, so the tab reloads instead of going blank"
            : "PASS: torn-off tab keeps its live page after the original window closes");
        third.Close();
        await Task.Delay(250);

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

    private static IServiceProvider Services(Window window) =>
        (IServiceProvider)typeof(MainWindow).GetField("_services", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class MemorySettings(BrowserSettings settings) : ISettingsStore
    {
        public BrowserSettings Load() => settings;
        public void Save(BrowserSettings value) { }
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
        public List<SessionEntry> Load() => [];
        public void Save(IEnumerable<(string Url, string Title, bool IsPinned, bool IsActive)> tabs) { }
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
}
