using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.Helpers;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Pages;
using StrideBrowser.Services.Reader;
using StrideBrowser.Engine;

internal static partial class Program
{
    private static readonly List<object> Results = [];
    private static string Output = "";
    private static bool UiChecks;

    [STAThread]
    public static int Main(string[] args)
    {
        var valid = new[] { "--assets-only", "--stores-only", "--pages-only", "--engine-smoke", "--extension-gate", "--ui-checks", "--full" };
        if (args.Length < 1 || !Path.IsPathFullyQualified(args[0]) || args.Skip(1).Any(arg => !valid.Contains(arg)))
            throw new ArgumentException("Supply an absolute output directory and supported opt-in switches.");
        var flags = args.Skip(1).ToHashSet();
        Require(flags.Count(flag => flag.EndsWith("-only") || flag is "--engine-smoke" or "--extension-gate" or "--full") <= 1,
            "Choose only one run mode.");
        UiChecks = flags.Contains("--ui-checks") || flags.Contains("--full");
        Output = Path.GetFullPath(args[0]);
        if (Directory.Exists(Output)) throw new IOException("Output directory already exists. Choose a new run directory.");
        Directory.CreateDirectory(Output);
        // Set the override before creating a store. Never use the production profile.
        Environment.SetEnvironmentVariable("STRIDE_DATA_DIR", Path.Combine(Output, "data"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            var passed = false;
            try
            {
                AppPaths.EnsureDirectories();
                var assetsOnly = flags.Contains("--assets-only");
                var storesOnly = flags.Contains("--stores-only");
                var engineOnly = flags.Contains("--engine-smoke");
                var gateOnly = flags.Contains("--extension-gate");
                if (!assetsOnly && !flags.Contains("--pages-only") && !engineOnly && !gateOnly)
                {
                    await StoreBenchmarks();
                    SuspensionProbe();
                }
                if (!storesOnly && !engineOnly && !gateOnly) await PageBenchmarks(assetsOnly, assetsOnly || flags.Contains("--full"));
                if (gateOnly || flags.Contains("--full")) await EngineExtensionGateProbe();
                if (engineOnly || UiChecks) await EnginePageSmoke();
                passed = true;
                app.Shutdown(0);
            }
            catch (Exception ex)
            {
                Results.Add(new { name = "failed-assertion-or-probe", error = ex.ToString() });
                Console.Error.WriteLine(ex);
                app.Shutdown(1);
            }
            finally
            {
                File.WriteAllText(Path.Combine(Output, "results.json"), JsonSerializer.Serialize(new
                {
                    capturedUtc = DateTime.UtcNow, runtime = Environment.Version.ToString(),
                    os = Environment.OSVersion.ToString(), processors = Environment.ProcessorCount,
                    passed, switches = flags, mode = "Release recommended; synthetic isolated profile; no remote sites", results = Results
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("Results: " + Path.Combine(Output, "results.json"));
            }
        };
        return app.Run();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static List<HistoryEntry> History(int n) => Enumerable.Range(0, n).Select(i => new HistoryEntry
    {
        Url = $"https://site{i % 50}.example/docs/{i}", Title = $"Example documentation {i}",
        VisitedAt = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-i)
    }).ToList();

    private static List<DownloadItem> Downloads(int n) => Enumerable.Range(0, n).Select(i => new DownloadItem
    {
        Id = "download" + i, FileName = $"Example file {i}.zip", FilePath = $"C:\\synthetic-stride-audit\\file{i}.zip",
        Url = $"https://example.com/file{i}.zip", State = DownloadState.Completed,
        StartedAt = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-i),
        TotalBytes = 1048576, ReceivedBytes = 1048576
    }).ToList();

    private static void RecordSamples(string name, int rows, double[] times, long[]? allocations = null, string? scope = null)
    {
        Array.Sort(times);
        if (allocations is not null) Array.Sort(allocations);
        var result = new { name, rows, samples = times.Length, medianMs = times[times.Length / 2],
            p95Ms = times[(int)Math.Ceiling(times.Length * .95) - 1],
            medianAllocatedBytes = allocations is null ? (long?)null : allocations[allocations.Length / 2], scope };
        Results.Add(result); Console.WriteLine(JsonSerializer.Serialize(result));
    }

    private static void Measure(string name, int rows, Action action, int samples = 25)
    {
        for (var i = 0; i < 3; i++) action();
        var times = new double[samples]; var allocations = new long[samples];
        for (var i = 0; i < samples; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp(); action();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocations[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        RecordSamples(name, rows, times, allocations, "synchronous caller on WPF dispatcher");
    }

    private static async Task MeasureMutationAndFlush(string name, int rows, Action mutation, Func<Task<bool>> flush)
    {
        for (var i = 0; i < 3; i++) { mutation(); Require(await flush(), name + " warmup flush failed"); }
        var mutations = new double[25]; var allocations = new long[25]; var flushes = new double[25];
        for (var i = 0; i < mutations.Length; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp(); mutation();
            mutations[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocations[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            start = Stopwatch.GetTimestamp();
            Require(await flush(), name + " measured flush failed");
            flushes[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        RecordSamples(name + "-ui-mutation", rows, mutations, allocations, "mutation only; no durability awaited");
        RecordSamples(name + "-awaited-background-flush", rows, flushes, scope:
            "wall time includes coalescing delay, background snapshot, JSON serialization and atomic replacement; no caller allocation claim");
    }

    private sealed class WriteMeter
    {
        private readonly List<double> _times = [];
        private long _bytes;
        private readonly int _dispatcherThread = Environment.CurrentManagedThreadId;
        private bool _onDispatcher;
        public void Write(string path, string json)
        {
            var start = Stopwatch.GetTimestamp();
            AtomicFileWriter.WriteAllText(path, json);
            lock (_times)
            {
                _times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                _bytes += System.Text.Encoding.UTF8.GetByteCount(json);
                _onDispatcher |= Environment.CurrentManagedThreadId == _dispatcherThread;
            }
        }
        public void Report(string name, int rows)
        {
            lock (_times)
            {
                Require(!_onDispatcher, name + " performed an ordinary store write on the dispatcher");
                RecordSamples(name + "-background-atomic-write", rows, _times.ToArray(), scope:
                    "writer callback only; excludes snapshot and serialization; includes 3 warmups");
                Results.Add(new { name = name + "-write-count", rows, writes = _times.Count, totalUtf8Bytes = _bytes, wroteOnDispatcher = _onDispatcher });
            }
        }
    }

    private static async Task StoreBenchmarks()
    {
        foreach (var n in new[] { 100, 1000, 10000 })
        {
            File.WriteAllText(AppPaths.HistoryFile, JsonSerializer.Serialize(History(n)));
            var meter = new WriteMeter();
            var history = new HistoryStore(AppPaths.HistoryFile, meter.Write); history.Load();
            var settings = new BrowserSettings { SearchSuggestionsEnabled = false };
            using var http = new System.Net.Http.HttpClient();
            var suggestions = new SuggestionProvider(http, settings, history);
            await MeasureMutationAndFlush("history-add", n, () => history.Add("https://example.com/new", "Example new visit"), () => history.FlushAsync());
            Measure("history-search-all", n, () => history.Search("Example"));
            Measure("local-suggestions", n, () => suggestions.GetLocalSuggestions("Example"));
            Measure("frequent-sites", n, () => history.GetFrequentSites());
            Require(await history.FlushAsync(), "Final history flush failed before next fixture");
            meter.Report("history-add", n);
            Results.Add(new { name = "history-file", rows = n, bytes = new FileInfo(AppPaths.HistoryFile).Length });
        }
        foreach (var n in new[] { 100, 1000, 5000 })
        {
            var items = Downloads(n);
            // Retain the all-record serializer diagnostic for baseline comparison. Product sync is measured separately.
            Measure("downloads-json-all-record-diagnostic", n, () => DownloadJson.SerializeForPage(items));
            Results.Add(new { name = "downloads-all-record-diagnostic-payload", rows = n,
                bytes = System.Text.Encoding.UTF8.GetByteCount(DownloadJson.SerializeForPage(items)), icons = false, productTransfer = false });
            File.WriteAllText(AppPaths.DownloadsFile, JsonSerializer.Serialize(items, DownloadJson.Options));
            var meter = new WriteMeter();
            var store = new DownloadStore(AppPaths.DownloadsFile, meter.Write);
            await MeasureMutationAndFlush("download-state", n, () => store.Items[0].State =
                store.Items[0].State == DownloadState.Completed ? DownloadState.Failed : DownloadState.Completed, () => store.FlushAsync());
            Require(await store.FlushAsync(), "Final downloads flush failed before next fixture");
            meter.Report("download-state", n);
        }
        foreach (var n in new[] { 1000, 10000 })
        {
            var group = new OneTabGroup { Id = "group", Name = "Synthetic saved tabs", Tabs =
                History(n).Select(h => new OneTabEntry(h.Url, h.Title, null, h.VisitedAt)).ToList() };
            var meter = new WriteMeter();
            var store = new OneTabStore(AppPaths.OneTabFile, meter.Write); store.Save([group]);
            Require(await store.FlushAsync(), "Saved-tab fixture flush failed");
            await MeasureMutationAndFlush("saved-tab-toggle-star", n, () => store.ToggleStar("group", 0), () => store.FlushAsync());
            Require(await store.FlushAsync(), "Final saved-tab flush failed before next fixture");
            meter.Report("saved-tab-toggle-star", n);
        }
        var sanitizer = new ReaderSanitizer();
        foreach (var n in new[] { 100, 500, 1000, 2000 })
        {
            var malformed = string.Concat(Enumerable.Repeat("<script>", n));
            Measure("reader-unclosed-tags", n, () => sanitizer.Sanitize(malformed, "https://example.com/"), 5);
        }
    }

    private static void SuspensionProbe()
    {
        var tabs = Enumerable.Range(0, 10).Select(_ => new BrowserTab()).ToArray();
        tabs[0].IsActive = true;
        var views = tabs.ToDictionary(tab => tab.Id, _ => (dynamic)new AuditView());
        var manager = new TabHibernationManager(new DownloadStore(), new BrowserSettings { TabSleepEnabled = true });
        manager.Attach(() => tabs, () => views, _ => { }, 10);
        try
        {
            for (var i = 0; i < 20; i++) manager.SuspendBackgroundTabs(tabs[0]);
            var count = views.Values.Sum(view => (int)view.CoreWebView2.SuspendCount);
            Results.Add(new { name = "repeat-suspension", liveTabs = 10, invocations = 20, suspendCalls = count, expected = 9, baseline = 180, fakeCore = true });
            Require(count == 9, "Sleeping background tabs received repeated suspend requests");
        }
        finally { manager.Detach(); }
    }

    private static async Task PageBenchmarks(bool assetsOnly, bool includeAssets)
    {
        using var view = new WebView2();
        var window = new Window { Content = view, Width = 1100, Height = 800, Left = -12000,
            Top = -12000, ShowInTaskbar = false, ShowActivated = false };
        window.Show();
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Output, "webview"));
            await view.EnsureCoreWebView2Async(environment).WaitAsync(TimeSpan.FromSeconds(45));
            BlockRemoteResources(view);
            Results.Add(new { name = "webview-runtime", version = environment.BrowserVersionString });
            if (assetsOnly) { await ImageProbe(view); return; }
            await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
                window.__auditObserverTimes=[];
                const NativeObserver=window.MutationObserver;
                window.MutationObserver=class extends NativeObserver {
                  constructor(callback) { super((...args)=>{const t=performance.now();callback(...args);window.__auditObserverTimes.push(performance.now()-t);}); }
                };
                """);
            foreach (var n in new[] { 1000, 10000 })
            {
                await Load(view, new HistoryPage().Render(History(n), "audit", false));
                await SamplePage(view, "history-render", n, "render('Example',0)", ".entry");
                await CheckJs(view, "document.querySelectorAll('.entry').length <= 50", "History DOM exceeded 50 records");
                await CheckJs(view, $"(render('Example',{(n - 1) / 50}), [...document.querySelectorAll('.entry-title')].some(row=>row.dataset.url.endsWith('/{n - 1}')))", "History pagination did not reach the oldest record");
                if (UiChecks) await HistoryUiChecks(view);
            }
            foreach (var n in new[] { 1000, 5000, 10000 }) await BoundedDownloads(view, n);
            await ThemeProbe(view);
            await NativeSuspensionProbe(environment);
            if (includeAssets) await ImageProbe(view);
        }
        finally { view.Dispose(); window.Close(); }
    }

    private static void BlockRemoteResources(WebView2 view)
    {
        view.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        view.CoreWebView2.WebResourceRequested += (_, args) =>
        {
            if (Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host != "audit.assets")
                args.Response = view.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "No remote resources in audit", "Content-Length: 0");
        };
    }

    private static async Task Load(WebView2 view, string html)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Navigation(object? sender, CoreWebView2NavigationCompletedEventArgs args) => completed.TrySetResult(args.IsSuccess);
        view.CoreWebView2.NavigationCompleted += Navigation;
        try
        {
            view.NavigateToString(html);
            Require(await completed.Task.WaitAsync(TimeSpan.FromSeconds(45)), "Page navigation failed");
            await Task.Delay(350);
        }
        finally { view.CoreWebView2.NavigationCompleted -= Navigation; }
    }

    private static async Task CheckJs(WebView2 view, string expression, string message)
        => Require(await view.ExecuteScriptAsync("(" + expression + ")") == "true", message);

    private static async Task WaitFor(WebView2 view, string expression, string message)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await view.ExecuteScriptAsync(expression) == "true") return;
            await Task.Delay(50);
        }
        throw new TimeoutException(message);
    }

    /// <summary>
    /// Mirrors the hibernation manager's native calls on a real control: target level
    /// Low, then TrySuspendAsync, then Resume. This measures native API latency that
    /// the call-count probe deliberately does not. A fresh control is used because
    /// suspension can be refused with ERROR_INVALID_STATE on a control with heavy
    /// prior activity in the same process.
    /// </summary>
    private static async Task NativeSuspensionProbe(CoreWebView2Environment environment)
    {
        using var view = new WebView2();
        var window = new Window { Content = view, Width = 800, Height = 600, Left = -12000,
            Top = -12000, ShowInTaskbar = false, ShowActivated = false };
        window.Show();
        try
        {
            await view.EnsureCoreWebView2Async(environment).WaitAsync(TimeSpan.FromSeconds(45));
            await Load(view, "<html><body style='height:3000px'><p>Suspension latency probe page</p></body></html>");
            await Task.Delay(1000);
            var core = view.CoreWebView2;
            var suspends = new List<double>();
            var resumes = new List<double>();
            int refused = 0, failed = 0;
            // TrySuspend fails with ERROR_INVALID_STATE while the controller is
            // visible, which is the same condition the product's hibernation
            // manager meets: background controls are Collapsed before suspension.
            view.Visibility = Visibility.Hidden;
            for (var i = 0; i < 5; i++)
            {
                var start = Stopwatch.GetTimestamp();
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                bool success;
                try { success = await core.TrySuspendAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (Exception)
                {
                    failed++;
                    try { core.Resume(); core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal; } catch { }
                    await Task.Delay(500);
                    continue;
                }
                suspends.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                if (!success) { refused++; await Task.Delay(500); continue; }
                start = Stopwatch.GetTimestamp();
                core.Resume();
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                resumes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                await Task.Delay(300);
            }
            Require(failed == 0 || suspends.Count > 0, "Native TrySuspendAsync failed on the fresh probe control in every attempt");
            Results.Add(new
            {
                name = "native-suspension-latency",
                succeeded = suspends.Count, refused, exceptions = failed,
                medianSuspendMs = suspends.Count > 0 ? (double?)suspends.OrderBy(value => value).ElementAt(suspends.Count / 2) : null,
                maxSuspendMs = suspends.Count > 0 ? (double?)suspends.Max() : null,
                medianResumeMs = resumes.Count > 0 ? (double?)resumes.OrderBy(value => value).ElementAt(resumes.Count / 2) : null,
                maxResumeMs = resumes.Count > 0 ? (double?)resumes.Max() : null,
                scope = "fresh real CoreWebView2 control with IsVisible false during the calls; TrySuspendAsync and Resume with MemoryUsageTargetLevel Low",
                note = "latency on this machine; excludes garbage collection, renderer memory savings and OS power effects"
            });
            if (suspends.Count > 0)
                Console.WriteLine($"Native suspension measured: {suspends.Count} samples, median {suspends.OrderBy(value => value).ElementAt(suspends.Count / 2):0.##} ms");
            else Console.WriteLine("Native suspension not measurable in this host; refusals recorded");
        }
        finally { view.Dispose(); window.Close(); }
    }

    private static async Task SamplePage(WebView2 view, string name, int rows, string expression, string rowSelector, string? prepare = null)
    {
        var samples = new List<JsonElement>();
        for (var i = 0; i < 5; i++)
        {
            if (prepare is not null) await view.ExecuteScriptAsync(prepare);
            var result = await view.ExecuteScriptAsync("(()=>{window.__auditObserverTimes=[];const t=performance.now();" + expression +
                ";return {ms:performance.now()-t,nodes:document.querySelectorAll('*').length,rows:document.querySelectorAll(" + JsonSerializer.Serialize(rowSelector) + ").length}})()");
            await Task.Delay(150);
            var render = JsonSerializer.Deserialize<JsonElement>(result);
            Require(render.GetProperty("rows").GetInt32() <= 50, name + " exceeded the visible row bound");
            var observers = await view.ExecuteScriptAsync("window.__auditObserverTimes");
            samples.Add(JsonSerializer.SerializeToElement(new { render, observerMs = JsonSerializer.Deserialize<double[]>(observers) }));
        }
        Results.Add(new { name, retainedRows = rows, scope = "real WebView2 synchronous JS plus separate observer samples; excludes native message transfer and paint", samples });
        Console.WriteLine($"Page measured: {name}, retained rows={rows}");
    }
}

public sealed class AuditView
{
    public AuditCore CoreWebView2 { get; } = new();
}

public sealed class AuditCore
{
    public int SuspendCount { get; private set; }
    public CoreWebView2MemoryUsageTargetLevel MemoryUsageTargetLevel { get; set; }
    public Task<bool> TrySuspendAsync() { SuspendCount++; return Task.FromResult(true); }
    public void Resume() { }
}
