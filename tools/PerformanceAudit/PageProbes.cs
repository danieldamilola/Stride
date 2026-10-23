using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.Helpers;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Pages;

internal static partial class Program
{
    /// <summary>Uses the shipped query and delta classes over real WebView messages, without a TabEngine.</summary>
    private sealed class DownloadAdapter : IDisposable
    {
        private readonly WebView2 _view;
        private readonly List<DownloadItem> _items;
        private readonly DownloadPageQuery _query = new();
        private readonly DownloadPageChanges _changes = new();
        public readonly List<string> Actions = [];
        public readonly List<object> Transfers = [];
        public int Requests { get; private set; }
        public int Payloads { get; private set; }
        public Exception? Failure { get; private set; }
        public bool Paused { get; set; }
        public DownloadPageChanges.Update? LastUpdate { get; private set; }
        public string CurrentRequest { get; private set; } = "";
        public DownloadAdapter(WebView2 view, List<DownloadItem> items)
        {
            _view = view; _items = items;
            view.CoreWebView2.WebMessageReceived += Message;
        }
        private void Message(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                var message = args.TryGetWebMessageAsString();
                if (!message.StartsWith("audit:download-sync:", StringComparison.Ordinal)) { Actions.Add(message); return; }
                Requests++;
                if (Paused) return;
                const string prefix = "audit:download-sync:";
                var request = JsonSerializer.Deserialize<DownloadRequest>(message[prefix.Length..], DownloadJson.Options)!;
                CurrentRequest = request.Request;
                var started = Stopwatch.GetTimestamp();
                var update = _changes.Create(request.Request, _query.Select(_items, request.Query, request.Page));
                var queryAndDeltaMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (update is null) return;
                LastUpdate = update;
                var json = JsonSerializer.Serialize(update, DownloadJson.Options);
                _view.CoreWebView2.PostWebMessageAsString("audit:downloads:" + json);
                Payloads++;
                Transfers.Add(new { query = request.Query, request.Page, changed = update.Changed.Length,
                    visible = update.Ids.Length, bytes = System.Text.Encoding.UTF8.GetByteCount(json), queryAndDeltaMs });
            }
            catch (Exception ex) { Failure = ex; }
        }
        public void Dispose() => _view.CoreWebView2.WebMessageReceived -= Message;
    }
    private sealed record DownloadRequest(string Query, int Page, string Request);

    private static async Task BoundedDownloads(WebView2 view, int retained)
    {
        var items = Downloads(retained);
        var query = new DownloadPageQuery();
        var changes = new DownloadPageChanges();
        var visible = query.Select(items, "", 0);
        Require(changes.Create("host-measurement", visible) is { Changed.Length: 50 }, "Initial host page must contain 50 rows");
        Measure("downloads-host-unchanged-visible-query-delta", retained, () =>
            Require(changes.Create("host-measurement", query.Select(items, "", 0)) is null,
                "Unchanged visible query produced a native payload"));
        items[0].State = DownloadState.InProgress;
        changes.Create("host-measurement", visible);
        Measure("downloads-host-single-progress-delta", retained, () =>
        {
            items[0].ReceivedBytes++;
            Require(changes.Create("host-measurement", query.Select(items, "", 0)) is { Changed.Length: 1 }, "Progress changed more than one host row");
        });
        items[0].State = DownloadState.Completed;
        items[0].ReceivedBytes = items[0].TotalBytes;

        using var adapter = new DownloadAdapter(view, items);
        var shell = new DownloadPage().Render(items, "audit");
        await Load(view, shell);
        await WaitFor(view, "itemMap.size === 50", "Downloads shell did not receive its first 50 rows");
        if (adapter.Failure is not null) throw new InvalidOperationException("Downloads adapter failed", adapter.Failure);
        Require(adapter.LastUpdate is { Changed.Length: 50 }, "Initial native delivery must contain 50 rows");
        Results.Add(new { name = "downloads-bounded-navigation", retainedRows = retained,
            utf8Bytes = System.Text.Encoding.UTF8.GetByteCount(shell), accepted = true, rows = 50,
            scope = "real WebView2, shipped HTML, shipped query/delta; probe-local native message adapter, no engine security router or file checks" });

        // Keep these raw rendering timings separate from native message transfer and host work.
        adapter.Paused = true;
        await view.ExecuteScriptAsync("clearTimeout(timer);window.__auditFull=" + JsonSerializer.Serialize(adapter.LastUpdate, DownloadJson.Options));
        await SamplePage(view, "downloads-full-changed-50-apply", retained,
            "sync(window.__auditFull)", ".item", "itemMap.clear();groupMap.clear();rowData.clear();list.replaceChildren()");
        await view.ExecuteScriptAsync("clearTimeout(timer);window.__auditButton=document.querySelector('.item .actions button');window.__auditButton.focus();window.__auditUntouched=itemMap.get('download1')");
        var emptyUpdate = adapter.LastUpdate! with { Changed = [] };
        await view.ExecuteScriptAsync("window.__auditUnchanged=" + JsonSerializer.Serialize(emptyUpdate, DownloadJson.Options));
        await SamplePage(view, "downloads-empty-changed-50-apply-diagnostic", retained, "sync(window.__auditUnchanged)", ".item");
        await CheckJs(view, "document.activeElement === window.__auditButton && itemMap.get('download1') === window.__auditUntouched", "Unchanged data replaced controls or focus");
        Results.Add(new { name = "downloads-unchanged-dom-check", retainedRows = retained, focusStable = true,
            note = "Direct empty delta is diagnostic. The host suppresses this payload in ordinary operation." });

        items[0].State = DownloadState.InProgress;
        var progress = new DownloadPageChanges();
        progress.Create(adapter.CurrentRequest, query.Select(items, "", 0));
        items[0].ReceivedBytes++;
        var delta = progress.Create(adapter.CurrentRequest, query.Select(items, "", 0))!;
        Require(delta.Changed.Length == 1, "A progress payload changed more than one row");
        await view.ExecuteScriptAsync("window.__auditProgress=" + JsonSerializer.Serialize(delta, DownloadJson.Options));
        // First apply changes action availability. Measure subsequent updates with stable controls.
        await view.ExecuteScriptAsync("sync(window.__auditProgress);clearTimeout(timer);window.__auditButton=document.querySelector('.item .actions button');window.__auditButton.focus()");
        await SamplePage(view, "downloads-single-row-progress-apply", retained,
            "window.__auditProgress.changed[0].receivedBytes++;sync(window.__auditProgress)", ".item");
        await CheckJs(view, "document.activeElement === window.__auditButton && itemMap.get('download1') === window.__auditUntouched", "Progress replaced controls or focus");
        await view.ExecuteScriptAsync("clearTimeout(timer)");
        items[0].State = DownloadState.Completed;
        adapter.Paused = false;

        // Search and real Next/Previous buttons travel through the adapter and native channel.
        await view.ExecuteScriptAsync("document.getElementById('search').value='';changePage(0)");
        await WaitFor(view, "page === 0 && downloads[0]?.id === 'download0' && downloads[0]?.state === 2", "Downloads first page did not reset");
        await view.ExecuteScriptAsync("document.getElementById('nextPage').click()");
        await WaitFor(view, "page === 1 && downloads[0]?.id === 'download50' && itemMap.size === 50", "Downloads Next did not change visible records");
        await view.ExecuteScriptAsync("document.getElementById('previousPage').click()");
        await WaitFor(view, "page === 0 && downloads[0]?.id === 'download0'", "Downloads Previous did not restore first page");
        await view.ExecuteScriptAsync("changePage(" + ((retained - 1) / 50) + ")");
        await WaitFor(view, "itemMap.has('download" + (retained - 1) + "') && itemMap.size <= 50", "Downloads pagination did not reach the oldest record");
        var search = "Example file " + (retained - 1) + ".zip";
        await view.ExecuteScriptAsync("document.getElementById('search').value=" + JsonSerializer.Serialize(search) + ";document.getElementById('search').dispatchEvent(new Event('input'))");
        await WaitFor(view, "itemMap.size === 1 && itemMap.has('download" + (retained - 1) + "')", "Downloads search did not reach the oldest record");
        await view.ExecuteScriptAsync("document.querySelector('.item .actions button[title=\"Copy download link\"]').click()");
        await Task.Delay(100);
        Require(adapter.Actions.Contains("audit:download-copy:download" + (retained - 1)), "Download action posted the wrong record ID");

        var requests = adapter.Requests; var payloads = adapter.Payloads;
        if (UiChecks)
        {
            // Observe the shipped five-second idle timer, including an unchanged request.
            await Task.Delay(5250);
        }
        else
        {
            await view.ExecuteScriptAsync("requestSync()");
            await Task.Delay(150);
        }
        Require(adapter.Requests > requests && adapter.Payloads == payloads, "An unchanged request delivered a full native payload");
        Results.Add(new { name = "downloads-idle-request", retainedRows = retained, requests = adapter.Requests - requests,
            nativePayloads = adapter.Payloads - payloads, timerObserved = UiChecks, observationMs = UiChecks ? 5250 : 150,
            actionIdCorrect = true, oldestSearchAndPage = true });
        if (UiChecks)
        {
            await DownloadUiChecks(view);
            var beforeHidden = adapter.Requests;
            // Override only the probe document. This tests the shipped visibility branch, not OS occlusion.
            await view.ExecuteScriptAsync("clearTimeout(timer);Object.defineProperty(document,'hidden',{configurable:true,get:()=>true});document.dispatchEvent(new Event('visibilitychange'));requestSync()");
            await Task.Delay(650);
            Require(adapter.Requests == beforeHidden, "Hidden Downloads document continued to request sync");
            await view.ExecuteScriptAsync("delete document.hidden;document.dispatchEvent(new Event('visibilitychange'))");
            await Task.Delay(150);
            Results.Add(new { name = "downloads-hidden-branch", retainedRows = retained, requestsWhileHidden = 0, syntheticDocumentHidden = true });
        }
        await view.ExecuteScriptAsync("clearTimeout(timer)");
        if (adapter.Failure is not null) throw new InvalidOperationException("Downloads adapter failed", adapter.Failure);
        Results.Add(new { name = "downloads-native-adapter-transfers", retainedRows = retained, adapter.Requests, adapter.Payloads, transfers = adapter.Transfers });
    }

    private static async Task DownloadUiChecks(WebView2 view)
    {
        await view.ExecuteScriptAsync("document.getElementById('clearBtn').focus();document.getElementById('clearBtn').click()");
        await WaitFor(view, "document.getElementById('confirmDialog').open && document.activeElement.id === 'confirmCancelBtn'", "Downloads confirmation did not focus Cancel");
        await PressKey(view, "Escape", "Escape", 27);
        await WaitFor(view, "!document.getElementById('confirmDialog').open && document.activeElement.id === 'clearBtn'", "Downloads dialog did not restore focus");
        Results.Add(new { name = "downloads-dialog-focus", opened = true, cancelFocused = true, focusRestored = true,
            limitation = "Programmatic click opens the dialog. Escape is sent through Chromium Input.dispatchKeyEvent. OS input and assistive technology are not exercised." });
    }

    private static async Task HistoryUiChecks(WebView2 view)
    {
        // The broad query creates one row per host. A narrow query creates a domain expander.
        await view.ExecuteScriptAsync("render('site0.example',0)");
        await WaitFor(view, "!!document.querySelector('.date-header[data-keyboard-ready]') && !!document.querySelector('.domain-header[data-keyboard-ready]')", "History controls were not enhanced for keyboard use");
        await view.ExecuteScriptAsync("window.__auditHeader=document.querySelector('.date-header');window.__auditHeader.focus()");
        await PressKey(view, "Enter", "Enter", 13);
        await WaitFor(view, "window.__auditHeader.getAttribute('aria-expanded') === 'true'", "History Enter did not expand the date group");
        await view.ExecuteScriptAsync("window.__auditDomain=document.querySelector('.domain-header');window.__auditDomain.focus()");
        await PressKey(view, " ", "Space", 32);
        await WaitFor(view, "window.__auditDomain.getAttribute('aria-expanded') === 'true'", "History Space did not expand the domain group");
        await CheckJs(view, "document.activeElement === window.__auditDomain", "History expansion lost keyboard focus");
        await view.ExecuteScriptAsync("document.getElementById('clearBtn').focus();document.getElementById('clearBtn').click()");
        await WaitFor(view, "document.querySelector('.dialog').getAttribute('aria-modal') === 'true' && document.querySelector('.dialog').contains(document.activeElement)", "History dialog did not receive focus");
        await CheckJs(view, "document.querySelector('header').inert", "History background was not inert during confirmation");
        await PressKey(view, "Tab", "Tab", 9);
        await CheckJs(view, "document.querySelector('.dialog').contains(document.activeElement)", "Tab escaped the History dialog");
        await PressKey(view, "Escape", "Escape", 27);
        await WaitFor(view, "!document.getElementById('confirmDialog').classList.contains('open') && document.activeElement.id === 'clearBtn' && !document.querySelector('header').inert", "History Escape did not close and restore focus");
        Results.Add(new { name = "history-keyboard-expand-dialog", enterExpandedDate = true, spaceExpandedDomain = true,
            ariaExpanded = true, dialogInert = true, tabStayedInDialog = true, escapeRestoredFocus = true,
            keyboard = "Chromium Input.dispatchKeyEvent; OS input and assistive technology excluded" });
    }

    private static async Task PressKey(WebView2 view, string key, string code, int virtualKey)
    {
        foreach (var type in new[] { "keyDown", "keyUp" })
            await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(new
            {
                type, key, code, windowsVirtualKeyCode = virtualKey, nativeVirtualKeyCode = virtualKey
            }));
    }

    private static async Task ThemeProbe(WebView2 view)
    {
        await Load(view, "<html><body style='background:rgb(30,40,50);height:4000px'>Static theme probe</body></html>");
        var messages = new List<string>();
        void Message(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try { var value = args.TryGetWebMessageAsString(); if (value.StartsWith("THEME_COLOR:")) messages.Add(value); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Theme probe ignored an unreadable message: {ex}"); }
        }
        view.CoreWebView2.WebMessageReceived += Message;
        try
        {
            await view.ExecuteScriptAsync(ResourceLoader.Load("Resources.Scripts.theme-color.js"));
            await Task.Delay(500); messages.Clear();
            for (var i = 0; i < 8; i++)
            {
                await view.ExecuteScriptAsync("window.dispatchEvent(new Event('scroll'))");
                await Task.Delay(300);
            }
            Results.Add(new { name = "unchanged-theme-scroll", events = 8, messages = messages.Count, distinct = messages.Distinct().ToArray() });
        }
        finally { view.CoreWebView2.WebMessageReceived -= Message; }
    }

    private static async Task ImageProbe(WebView2 view)
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "Resources", "Pages");
        var backgrounds = Path.Combine(assets, "Backgrounds");
        var main = Directory.GetFiles(backgrounds, "*.jpg");
        var thumbnails = Directory.GetFiles(Path.Combine(backgrounds, "Thumbnails"), "*.jpg");
        Require(main.Length == 14 && thumbnails.Length == 14, "Expected 14 wallpapers and 14 picker thumbnails");
        var files = main.Concat(thumbnails).ToArray();
        var totalBytes = files.Sum(file => new FileInfo(file).Length);
        Require(totalBytes < 10 * 1024 * 1024 && totalBytes < 80_661_686, "Wallpaper package did not shrink within its byte budget");
        view.CoreWebView2.SetVirtualHostNameToFolderMapping("audit.assets", assets, CoreWebView2HostResourceAccessKind.DenyCors);
        var names = files.Select(file => Path.GetRelativePath(assets, file).Replace('\\', '/')).ToArray();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Message(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            var value = args.TryGetWebMessageAsString();
            if (value.StartsWith("audit-images:", StringComparison.Ordinal)) completion.TrySetResult(value[13..]);
        }
        view.CoreWebView2.WebMessageReceived += Message;
        try
        {
            await Load(view, "<html><body>Local wallpaper decode probe</body></html>");
            await view.ExecuteScriptAsync("Promise.all(" + JsonSerializer.Serialize(names) + ".map(name=>new Promise(resolve=>{" +
                "const image=new Image();const t=performance.now();image.onload=()=>resolve({name,ok:true,width:image.naturalWidth,height:image.naturalHeight,ms:performance.now()-t});" +
                "image.onerror=()=>resolve({name,ok:false,ms:performance.now()-t});image.src='https://audit.assets/'+name.split('/').map(encodeURIComponent).join('/');" +
                "}))).then(rows=>window.chrome.webview.postMessage('audit-images:'+JSON.stringify(rows)))");
            var json = await completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
            var rows = JsonSerializer.Deserialize<JsonElement[]>(json)!;
            Results.Add(new { name = "wallpaper-decode", wallpapers = main.Length, thumbnails = thumbnails.Length,
                totalBytes, baselineBytes = 80_661_686, rows });
            foreach (var row in rows)
            {
                Require(row.GetProperty("ok").GetBoolean(), "Wallpaper did not decode: " + row.GetProperty("name").GetString());
                var thumbnail = row.GetProperty("name").GetString()!.Contains("/Thumbnails/", StringComparison.Ordinal);
                var width = row.GetProperty("width").GetInt32(); var height = row.GetProperty("height").GetInt32();
                Require(thumbnail ? width is >= 240 and <= 320 && height is >= 130 and <= 180 : width is >= 1920 and <= 2560 && height is >= 1080 and <= 1440,
                    "Wallpaper dimensions exceed the shipped image budget");
            }
            var largePage = new DownloadPage().Render(Downloads(5000), "audit");
            await Load(view, largePage);
            Results.Add(new { name = "downloads-large-navigation", retainedRows = 5000,
                utf8Bytes = System.Text.Encoding.UTF8.GetByteCount(largePage), accepted = true,
                scope = "shell navigation only; bounded data delivery is exercised by the Downloads page suite" });
            await Capture(view, "wallpaper-and-shell-navigation.png");
        }
        finally { view.CoreWebView2.WebMessageReceived -= Message; }
    }

    private static async Task Capture(WebView2 view, string filename)
    {
        await using var output = File.Create(Path.Combine(Output, filename));
        await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
    }
}
