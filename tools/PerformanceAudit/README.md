# Performance audit probe

Run on Windows with an installed WebView2 runtime. The probe uses synthetic records in a new isolated profile and adds no packages. It refuses an existing output directory. It sets `STRIDE_DATA_DIR` before creating stores and blocks remote HTTP resources in its probe WebViews.

Build and run the final suite after all product edits and automated tests finish:

```powershell
dotnet build tools/PerformanceAudit/PerformanceAudit.csproj -c Release
dotnet run --no-build --project tools/PerformanceAudit/PerformanceAudit.csproj -c Release -- C:\absolute\path\new-audit-run --full
```

Use a new absolute output directory for every run. Do not run another build, test suite or browser timing probe at the same time. This is a development-machine measurement, with no universal latency thresholds. Failed assertions throw, make the process return 1 and appear in `results.json` with any earlier completed results. A passing run returns 0 and records `passed: true`.

The run command above shows the final suite, `--full`. Available modes are:

- `--stores-only` runs stores, malformed reader input and the fake suspension probe.
- `--pages-only` runs History, bounded Downloads, theme-color measurements and native suspension latency in a real offscreen WebView2.
- `--assets-only` decodes all 14 wallpapers and 14 thumbnails, checks dimensions and package bytes, and asserts that the 5,000-record Downloads shell navigates.
- `--engine-smoke` runs the real engine and internal-page handler checks described below.
- `--extension-gate` runs only the delayed-extension activation reproduction: one controlled reconcile stays unresolved while an external tab passes creation, and an internal tab must activate and render without waiting for it. This is the F05 acceptance case.
- `--ui-checks` adds keyboard, expand, dialog, focus and visibility checks to page measurements, then runs the engine smoke checks.
- `--full` runs all measurements, assets, the extension-gate reproduction and UI/engine checks. This is the final live suite.

Only one of the mode switches ending in `-only`, `--engine-smoke`, `--extension-gate` and `--full` may be supplied. `--ui-checks` can accompany `--pages-only` to skip store timings during a UI rerun.

## Store measurements

History starts with 100, 1,000 and 10,000 visits. It gains the same 28 visits used by the baseline, including three warmups. The largest history remains capped at 10,000. Download state changes use 100, 1,000 and 5,000 retained records. Saved-tab mutations use 1,000 and 10,000 tabs. Every mutation sample awaits a real flush before the next sample. Every store is flushed before its file is seeded for another scenario. Saved-tab fixture creation also awaits its asynchronous Save operation.

The results separate these costs:

- `*-ui-mutation` measures only the synchronous mutation and allocations on the WPF dispatcher.
- `*-awaited-background-flush` measures wall time until persistence completes. This includes the coalescing delay, background snapshot, JSON serialization and atomic replacement. It is not a measure of dispatcher blocking. It has no calling-thread allocation claim.
- `*-background-atomic-write` measures the writer callback after JSON serialization. It excludes snapshot and serialization costs. It includes warmup writes and any explicit fixture or final writes. The count and total UTF-8 bytes appear in a separate record. Ordinary writes on the dispatcher fail the probe.

The all-record download serializer remains as a diagnostic for comparison with the original audit. Its labels explicitly say `all-record-diagnostic`. It is no longer the product's normal page transfer. Icons are omitted from this synthetic serializer diagnostic. Download measurements have no browser view-model or toolbar subscribers. They isolate the store.

Reader malformed-input measurements keep the baseline cases of 100, 500, 1,000 and 2,000 unclosed script tags, with three warmups and five samples. The suspension probe still uses ten tabs and twenty repeated calls, with fake native cores. It now asserts nine initial suspend calls instead of the baseline's 180. That count probe uses fake cores and does not measure native suspension latency or memory savings.

## Native suspension latency

The page modes also mirror the hibernation manager's real native calls on a fresh offscreen control: MemoryUsageTargetLevel Low, then TrySuspendAsync, then Resume, five sampled rounds. WebView2 refuses suspension with ERROR_INVALID_STATE while the controller is visible, so the probe hides the control first, which matches the product behavior of collapsing background controls before suspension. The probe records suspend and resume latency only. It excludes garbage collection, renderer memory savings, OS power effects and any claim about how many suspensions a real session issues.

## Extension-gate reproduction

`--extension-gate` shares the profile coordinator with a deliberately unresolved reconcile request. An external tab is created through the real activation path while that run is held, then an internal tab activates. The probe asserts that the external tab's creation released the shared gate, that the internal tab activated and rendered while the external tab still waited, and that the held run resolves as an honest failure after release. One WebView2 control with a 404 filter covers all remote requests, so nothing leaves the machine.

The internal control fills a Grid host. Its Downloads element and request function must exist and its rendered area must be nonzero before a PNG capture with a 15-second timeout. Control creation has a 45-second deadline. The fixture releases its held reconcile during cleanup even on failure.

## Live renderer measurements

History uses `render('Example', 0)` at 1,000 and 10,000 retained visits. Each page must keep at most 50 entry rows and pagination must reach the oldest visit. Five samples record synchronous JavaScript and DOM element counts. Observer callback time is recorded separately. Native transfer, painting and input-to-paint latency are excluded.

Downloads uses the shipped shell and production `DownloadPageQuery` and `DownloadPageChanges`, with 1,000, 5,000 and 10,000 retained records. A probe-local native WebMessage adapter implements the current request and delta framing. This isolates renderer timings without claiming that the adapter is the product's security router or file service.

The request is `download-sync:{query,page,request}`. The reply is `downloads:{request,page,total,ids,changed,active}`. Both use the internal-page token prefix. The first reply has 50 rows. An unchanged visible query must produce no reply payload. Host measurements separate the cached unchanged query and one-row progress delta. Renderer samples separate a full 50-row apply and a one-row progress apply. A direct empty delta is retained only as a renderer diagnostic. The real host suppresses that message.

The adapter suite checks bounded DOM, Next/Previous, pagination and search reaching old records, correct action IDs and stable focused controls. UI mode checks the hidden-document branch by overriding the probe document's `hidden` property. That check tests the shipped branch and does not claim actual OS occlusion or visibility behavior. Dialog and History keyboard checks use Chromium `Input.dispatchKeyEvent`; no OS keyboard, screen reader or pointer drag is simulated.

## Real engine smoke

The engine smoke creates the real `TabEngine`, production stores, a registered internal document, `WebMessageRouter`, `DownloadMessageHandler`, `OneTabMessageHandler` and real native messages. A probe-only reflection seam attaches an already initialized WPF WebView2 and calls the engine's existing event wiring method. It follows the repository's InternalPagesVerification pattern. It does not initialize the full main window, extension startup or toolbar view models.
At both 5,000 and 10,000 download records it asserts 50-row delivery, no unchanged native payload, old-record search and pagination, correct remove IDs, a deleted-file refresh after the five-second cache expires, a single-row progress update, and stable controls and focus. File checks use a disposable file under the output directory. No shell Open, clipboard action or remote download is invoked.

Saved-tab checks drive the real star control, reorder routes, duplicate URL removal, group order and rename through the actual registered IPC path. They assert zero page navigations and stable focus. Reorder tests invoke the route directly, so they do not claim native pointer drag coverage. Saved-tab DOM fixtures are small. The 10,000-record store scenarios and regression tests cover large mutation and restart behavior. They do not establish that a 10,000-tab initial saved-page document is bounded.

The full run writes PNG previews for the engine Downloads pages, saved-tab pages and asset/shell check. Inspect these artifacts after the run. The probe does not launch the application's main window, exercise window tear-off, profile native memory or power, or test real sites, paid APIs and extensions. Use the separate TabTearingSmoke artifact after the final product build for activation and transfer coverage.
