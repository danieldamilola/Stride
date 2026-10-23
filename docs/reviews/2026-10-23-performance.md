# Project performance audit

Date: 2026-10-23. Baseline commit: 83a3c12902d865ecafca07745bd534a236123f73. The working tree was clean before this audit.

## Assessment

The largest reproduced stalls are in internal pages and synchronous persistence. History search takes seconds at the store's supported size. Downloads repeatedly processes unchanged data and eventually produces a document WebView2 rejects. Reader sanitization has poor malformed-input scaling. These deserve attention before small allocation or rendering cleanups.

This audit identifies six high-priority findings, five medium-priority findings and one low-priority finding. Priority reflects impact and reach. Each finding states whether the evidence is a live WebView2 check, an isolated measurement or a source observation.

At the time of the audit run, no product fixes had been applied. The audit added this report, raw synthetic results, a repeatable diagnostic tool and a changelog entry. The follow-up section [Applied fixes](#applied-fixes) records the fixes that now exist on this branch, their post-fix measurements and their acceptance evidence. The numbered findings stay as written, so their references and numbers describe the baseline commit.

## Applied fixes

The fixes below are implemented and tested on this branch. Their numbers are ranges across two complete post-fix `--full` runs of the [PerformanceAudit tool](../../tools/PerformanceAudit/README.md) in the same environment.

| Probe | Baseline number in this report | After fixes | Evidence |
|---|---|---|---|
| History render at 10,000 rows, synchronous page work | 4,773.4 ms median, 52,870 elements | median 2.7 to 9.5 ms across the two post-fix runs, 339 elements, 50 visible rows | `history-render`, live WebView2 |
| A history addition at 10,000 rows | 27.7 ms median store write on the dispatcher | 0.02 to 0.04 ms median mutation; the atomic write moves to the worker at 3.4 to 8.4 ms median | `history-add-ui-mutation`, `history-add-background-atomic-write` |
| An unchanged 1,000-row Downloads sync | 357.9 ms in the renderer plus 60.7 ms host serialization | the unchanged visible query sends no payload; host query cost 0.05 to 0.08 ms median | `downloads-host-unchanged-visible-query-delta`, unchanged native payload assertions in the engine smoke |
| A 5,000-record generated Downloads document | 3.58 MB rejected with ArgumentException | the real handler opens and pages 5,000 and 10,000 records | `downloads-real-engine-ipc` at 5,000 and 10,000 |
| A OneTab star change at 10,000 tabs | 64.7 ms, one full reread, rewrite and page reload | 0.02 to 0.03 ms mutation, zero page navigations through the real handler | `saved-tab-toggle-star-ui-mutation`, `saved-tabs-real-engine-ipc` |
| 2,000 unclosed script tags | 627.1 ms median | 0.26 to 0.67 ms median | `reader-unclosed-tags` |
| Twenty repeated suspension passes over ten tabs | 180 TrySuspendAsync calls | 9 calls | `repeat-suspension` |
| Download state change at 5,000 records | 36.3 ms median full write | 0.02 to 0.03 ms median mutation, background write 4.0 to 12.9 ms median | `download-state-ui-mutation`, `download-state-background-atomic-write` |
| Wallpapers | 76.9 MiB, four files failed to decode | main set 7,156,900 bytes decoded, 7,266,359 bytes with the 14 picker thumbnails, 80,661,686 bytes baseline | asset probe in the post-fix run |
| Theme color | eight identical messages from eight scrolls | one message, later identical values suppressed | theme probe and `theme-color-runtime.mjs` |
| Favicon storage | no retention budget | 2,000 entries, 64 MiB, 30 days, bounded response reads | `FaviconDiskCache.cs`, `FaviconBudgetTests` |

Acceptance evidence beyond the numbers:

- The F05 reproduction runs as the tool's `--extension-gate` mode. A held reconcile shares the profile coordinator, an external tab creation completes and releases the shared gate, and an internal Downloads tab activates in 145 and 263 ms across the two closing runs while the external tab still waits. The external tab's creation took 1,340 ms in the closing run and no remote request left the held tab. After the release, the held run resolves as an honest failure and the external navigation proceeds. The earlier pre-fix code held the creation semaphore through the extension wait, so an internal tab waited behind it.
- Native suspension cost is now sampled: the tool records TrySuspendAsync and Resume latency on a fresh real control with the controller hidden, five samples with no refusals in both runs: suspend median 0.36 to 0.68 ms and max 8.4 ms, resume median below 0.05 ms. See the reproduction section for the run location. Memory savings and power effects remain out of scope.

Change details per finding:

- F01 History: 50-row pages, cached date and search metadata, rows created only for the visible page, and an accessibility pass limited to added controls and expanded state changes. `Resources/Pages/History.html`, `Resources/Scripts/internal-page.js`.
- F02 Downloads: page queries with an invalidated index, changed-row deltas, no periodic full sync, no snapshot delivery, and a plain shell first document. `Services/Pages/DownloadPageQuery.cs`, `Services/Pages/DownloadPageChanges.cs`, `Engine/Handlers/DownloadMessageHandler.cs`, `Resources/Pages/Downloads.html`.
- F03 Ordered persistence: one background writer per store, coalesced mutations, worker-owned atomic replacement, snapshot invalidation and an awaited shutdown flush. `Services/OrderedStorePersistence.cs` with `HistoryStore`, `DownloadStore` and `OneTabStore` integrations.
- F04 Reader: scanning sanitizer with tag allow-list, 2,000,000 character ceiling, 500 ms execution budget and timeouts that fail closed. Article content is prepared off the dispatcher. `Services/Reader/ReaderSanitizer.cs`, `Services/Reader/ReaderService.cs`.
- F05 Activation: `CreateThenNavigateAsync` holds the shared gate for runtime creation only and releases it before per-tab first navigation, whose extension wait belongs to engine lifetime. `Engine/TabEngine.cs`.
- F06 Download notifications: `DownloadCollectionObserver` uses collection change arguments instead of linear Contains passes, `DownloadActivityTracker` keeps toolbar progress to active transfers, and bulk clears unsubscribe through the same events. `ViewModels/BrowserViewModel.cs`, `ViewModels/DownloadsPanelViewModel.cs`.
- F07 Wallpapers: the damaged encodings were recovered from the original Unsplash files, all primaries are screen-sized, and the picker uses lazy thumbnails. `Resources/Pages/Backgrounds`, `Resources/Pages/NewTab.html`, `tools/PerformanceAudit/OptimizeWallpapers.ps1`.
- F08 Saved-tabs: star, reorder, rename and group changes update rows in place and persist in order. `Services/OneTabStore.cs`, `Engine/Handlers/OneTabMessageHandler.cs`.
- F09 Suspension: confirmed and pending suspension tracking skips already sleeping controls and keeps stale-generation undo behavior. `Engine/TabHibernationManager.cs`.
- F10 Theme color: sender dedupe with a persistent bootstrap toggle, receiver dedupe in the IPC bridge, and extraction disabled while adaptive tint is off. `Resources/Scripts/theme-color.js`, `Engine/ContentScriptInjector.cs`, `Engine/WebViewIpcBridge.cs`.
- F11 Download ownership and documentation: native transfers protect their owning tab. Independent hidden download controllers do not exempt the origin tab. Unknown ownership remains conservative. The README describes the soft per-window control budget and its exemptions.
- F12 Favicons: `Services/FaviconDiskCache.cs` applies an entry, byte and age budget after each write and on schedule, and icon responses are bounded by size before decode.

## Scope and method

Reviewed application startup and composition, engine activation and teardown, session restore, hibernation, window transfer, WPF layout, downloads, persistence, suggestions, favicons, internal pages, injected scripts, reader mode, link preview, Focus, extension startup and inventory, T&C Lens integration, PDF.js packaging, updater and release configuration. Existing tests and verification tools were also inspected. Vendored rendering engines received integration and packaging review, not an internal algorithm audit.

Measurements used the Release assembly, real WPF and WebView2 controls and synthetic records in a separate STRIDE_DATA_DIR. The final timing run started after tests and builds completed. This is a development machine, not a dedicated benchmark host. Treat timings as scenario results, not universal latency predictions.

- Windows build 26200, .NET runtime 9.0.20, eight logical processors and WebView2 154.0.4258.62.
- Store probes use three warmup operations and 25 timed samples. They run on the WPF dispatcher. Allocations cover the calling thread.
- Page probes use five samples after initial navigation. Reported page times cover synchronous JavaScript. Observer times are measured separately. Painting, input latency and native message transfer are excluded.
- Download payloads omit icons. Their measured size is a lower bound for equivalent histories with icons.
- The suspension probe uses fake cores to count native API requests. It does not measure native suspension latency or savings.
- Asset probes load the actual copied wallpapers through a local virtual host in WebView2.

The earlier review in [2026-10-20-performance.md](2026-10-20-performance.md) remains useful history. Its numbers were not reused as this audit's measurements.

## High-priority findings

### F01. History rebuilds an unbounded document for each search input

Evidence: live WebView2 measurement and source review.

Resources/Pages/History.html lines 357, 444 and 488 filter and sort all matches, format dates and domains, build every row and replace innerHTML on each input event. Collapsed groups still contain their rows. Resources/Scripts/internal-page.js line 73 then runs its document-wide accessibility scan after the replacement.

- At 1,000 records, the median render took 268.8 ms and created a document with 5,740 elements. The observer took another 11.0 ms.
- At 10,000 records, the median render took 4,773.4 ms and created 52,870 elements. Samples ranged from 3,934.9 to 5,955.3 ms. The observer took another 135.2 ms.

These measurements exclude paint. This reproduces a renderer stall without any remote website or network request.

Change direction: paginate results or create groups and rows only when needed. Cache normalized search and date fields. Update accessibility for added controls and changed expanded state. Preserve keyboard access, focus and dialog behavior. Debouncing alone leaves the final multi-second render intact.

Acceptance: with 10,000 records and a query matching every record, opening and searching keep the DOM bounded. Use a first target of under 50 ms of synchronous work per input after warmup, then measure input-to-paint latency separately.

### F02. Downloads polls and processes the complete history, then exceeds document delivery capacity

Evidence: live WebView2 checks, isolated serialization measurements and source review.

Resources/Pages/Downloads.html line 227 requests a sync every 500 ms. Engine/Handlers/DownloadMessageHandler.cs lines 26 through 43 refresh file details for the entire store, serialize every row and post the full snapshot. Services/DownloadJson.cs builds a JsonNode tree for each item. The page's sync at line 175 filters, sorts and groups all visible rows, then updates every row even when nothing changed. The existing item map preserves controls and focus, but does not bound the work.

- At 1,000 records, one host serialization took a median 60.7 ms, allocated 8,545,768 bytes and produced 704,561 UTF-8 bytes without icons.
- At 5,000 records, serialization took 243.5 ms, allocated 42,844,584 bytes and produced 3,540,561 bytes without icons.
- Applying an unchanged 100-row snapshot in the renderer took 49.8 ms. At 1,000 rows it took 357.9 ms, with another 13.8 ms in the observer and 24,043 DOM elements.
- The actual 5,000-row generated Downloads document was 3,576,209 UTF-8 bytes. NavigateToString rejected it with ArgumentException on the tested runtime. Engine/TabEngine.cs lines 815 through 840 catches delivery errors and traces them, leaving no usable generated document from that attempt.

The store has no history count limit. At two requested syncs per second, unchanged completed records continue to drive host allocation, transfer and renderer work. The probe did not measure the end-to-end sum, so the separate stage medians should not be added as a measured user delay.

Change direction: deliver a small initial document and paginated data, maintain a bounded visible list, and send changes for active or modified rows. Check missing files on demand or at a slower cadence. Stop periodic work when there is no changing data and avoid requesting syncs from hidden documents. Preserve current controls, focus, restart actions and deleted-file detection.

Acceptance: the page opens with 5,000 and 10,000 records. An unchanged snapshot causes no full-list DOM update or repeated full JSON transfer. Bound initial document size and rendered rows. Measure active progress separately from completed history.

### F03. Navigation and download state changes write full JSON files on the dispatcher

Evidence: isolated measurements on the dispatcher and source-confirmed product call paths.

Engine/TabEngine.cs line 2054 records history from its navigation-completion path. Services/HistoryStore.cs lines 30 through 48 holds the store lock while adding and persisting. Line 132 serializes every entry and calls Helpers/AtomicFileWriter.cs, which writes and replaces files synchronously.

At the 10,000-record cap, one history addition took a median 27.7 ms and a p95 of 67.0 ms. Median calling-thread allocation was 2,292,712 bytes. This is store cost, not end-to-end navigation latency.

Services/DownloadStore.cs lines 41 through 59 similarly serializes all records on state, error and path changes. A state change at 5,000 records took 36.3 ms at the median, 49.9 ms at p95 and allocated 11,123,304 bytes. This excludes toolbar and panel subscribers. Progress bytes alone do not trigger persistence, which is already a useful safeguard.

Change direction: apply in-memory mutations immediately and use one ordered background persistence owner with coalescing. Define failures and an awaited shutdown flush. Clear and clear-on-exit must invalidate pending older snapshots so a delayed write cannot recreate deleted history. Bulk cancellation should avoid a full write per cancelled item.

Acceptance: navigation callbacks no longer serialize or write complete stores on the dispatcher. Test write ordering, concurrent changes, clear during a pending write, locked files, shutdown flush and restart recovery. Preserve atomic replacement.

### F04. Reader sanitization scales poorly on malformed HTML

Evidence: isolated execution of the shipped sanitizer and source review.

Services/Reader/ReaderSanitizer.cs lines 24 through 40 uses backtracking regular expressions without a match timeout or input ceiling. The drop-entirely expression tries to pair each opening tag with a closing tag. Repeated unclosed script tags cause repeated unsuccessful scanning. ReaderService.EnterReaderAsync and RefreshAsync call the sanitizer synchronously after extraction returns to the caller's context.

- 500 opening tags, 4,000 characters, took a median 39.9 ms.
- 1,000 opening tags, 8,000 characters, took 212.7 ms.
- 2,000 opening tags, 16,000 characters, took 627.1 ms, with a maximum sample of 977.0 ms.

The extraction timeout does not bound this later synchronous stage. The measured strings were diagnostic inputs passed directly to the sanitizer. This does not establish that Readability emits this exact string for an ordinary website.

Change direction: give sanitization an input and execution budget, use a scan with bounded work for malformed input, and move expensive pure processing off the dispatcher. A timeout must fail closed. Do not fall back to unsanitized HTML. Discuss any parser package before adding it.

Acceptance: regression cases include missing closing tags, repeated tag prefixes, malformed attributes and large normal articles. Bound CPU time and verify that cancellation or timeouts leave the original page usable.

### F05. One tab's extension wait holds the control-creation gate for other tabs

Evidence: source-confirmed dependency, without a delayed real-extension reproduction in this audit.

Engine/TabEngine.cs line 497 acquires _activationGate and holds it across CreateWebViewForTab until its finally block releases the gate. Creation awaits AwaitExtensionGateAsync at line 1234 before returning. Services/ExtensionStartupCoordinator.cs line 45 gives each waiter a default 15-second budget.

A first external tab waiting for extension preparation or registration can therefore block creation of another selected tab, including an internal page that needs no extension registration. Switching tabs cancels display activation, but the engine-owned readiness work correctly continues. This is an asynchronous dependency stall, not synchronous dispatcher blocking. The timeout bounds one waiter, not every startup operation.

Change direction: keep control creation serialized but release its gate before per-tab first-navigation readiness. Preserve shared readiness per tab, the extension barrier, COM thread affinity and navigation-intent checks.

Acceptance: delay external tab A's reconciliation and open internal tab B. B must render before A's gate completes. Also exercise A/B/A switching, close during creation, popup adoption, hibernation wake and transfers between windows.

### F06. Download collection notifications repeatedly rescan all retained records

Evidence: source review. No full toolbar interaction timing was measured.

ViewModels/BrowserViewModel.cs lines 174 through 205 and ViewModels/DownloadsPanelViewModel.cs lines 21 through 40 reconcile their subscriptions on every collection change. Each reconciliation checks every observed item with ObservableCollection.Contains, which performs a linear search. One reconciliation therefore becomes quadratic as retained history grows. Every open window has its own subscribers.

Services/DownloadStore.cs ClearAll and ClearCompleted remove records individually. Each removal triggers another reconciliation. DownloadCoordinator.ClearAll also cancels live items before clearing, which can trigger repeated store writes and panel refreshes. Ordinary progress callbacks scan all records to aggregate active downloads even when almost every record is completed.

Change direction: use NotifyCollectionChangedEventArgs to subscribe and unsubscribe only affected items. Maintain an active-download set and batch bulk removals and persistence. Keep the panel's existing limited recent list and unsubscribe behavior.

Acceptance: measure adding one record and clearing 1,000 and 10,000 records with one and several windows. Instrument collection events, reconciliation passes, dispatcher time and persistence writes. Verify active transfers are cancelled once and every removed item loses its subscriptions.

## Medium-priority findings

### F07. Wallpapers add 76.9 MiB, and four bundled images fail to decode

Evidence: file inventory and live WebView2 image checks.

The 14 files under Resources/Pages/Backgrounds total 80,661,686 bytes. Stride.csproj copies all of them to output. NewTab.html line 7 preloads the chosen full image. Its picker at lines 491 through 503 creates a full-image img for every wallpaper with no separate thumbnails or lazy-loading instruction.

WebView2 could not decode these four copied files:

- alexander-slattery-LI748t0BK8w-unsplash.jpg
- casey-horner-O0R5XZfKUGQ-unsplash.jpg
- hrvoje_photography-pdRsf77OBoo-unsplash.jpg
- john-towner-JgOeRuGD_Y4-unsplash.jpg

The john-towner file begins with a UTF-8 BOM and replacement-character bytes instead of a JPEG signature. These four files total 40,031,496 bytes, about half the wallpaper payload, yet fail the actual browser image check.

The largest valid image is 7,568 by 5,048 pixels. One full-resolution four-byte pixel buffer would be about 145.7 MiB. This is a size calculation, not measured renderer memory. WebView2 may downsample, share or evict decoded images.

Change direction: recover valid source images, create screen-sized variants and small picker thumbnails, and keep the originals out of the runtime package. Confirm visual quality at intended display sizes. This reduces every full update package as well as install and picker work.

Acceptance: every shipped image decodes. Measure package bytes, picker latency and renderer private bytes before and after resizing. No blanket recompression was applied during this audit.

### F08. Saved-tab mutations reread, rewrite and reload the whole page

Evidence: isolated store measurements and source review.

Services/OneTabStore.cs lines 78 through 91 reread and deserialize the complete file to change one star, then serialize and write the complete file at lines 170 through 182. The handler calls RefreshOneTabPages, which reads the store again and navigates every saved-tab page in that window to regenerated HTML.

At 10,000 saved tabs, toggling one star took a median 64.7 ms and allocated 15,708,528 bytes before the subsequent read, template generation and page reload. The store has no count limit. At 1,000 tabs the store-only median was 7.6 ms.

Change direction: maintain an authoritative in-memory collection, persist ordered changes off the dispatcher and update affected groups or rows through messages. Preserve drag ordering, duplicate URLs, stars and save-failure behavior.

Acceptance: star and reorder operations preserve scroll and focus without page navigation. Test rapid mutations, failed writes and restart recovery with 10,000 saved tabs.

### F09. Activation asks already sleeping tabs to suspend again

Evidence: counted calls through the real manager with fake native cores, plus source review.

Engine/TabHibernationManager.cs lines 70 through 111 iterates every background control without skipping IsSleeping or coalescing a pending suspension. Twenty calls with ten live tabs and the same active tab produced 180 TrySuspendAsync calls. Only the initial nine were needed to establish the simulated sleeping state. Native WebView2 may handle redundant calls cheaply; its cost was not measured here.

Change direction: track confirmed and pending suspension per control. Keep the current generation, settings, ownership and active-tab checks that undo stale completions. Do not weaken the recently added pinned-tab preferences.

Acceptance: unchanged sleeping tabs do not receive new calls. Delayed completion still resumes a tab selected, transferred or exempted while a request is pending. Measure switching with 10 and 50 live tabs.

### F10. Unchanged theme colors repeatedly refresh the host

Evidence: live WebView2 messages and source review.

Resources/Scripts/theme-color.js throttles extraction to 250 ms and skips hidden documents, but does not suppress unchanged values. Eight synthetic scroll events spaced 300 ms apart on a static page produced eight identical THEME_COLOR:#1e2832 messages. ContentScriptInjector registers the script regardless of AdaptiveToolbarTint. WebViewIpcBridge.cs lines 231 through 245 schedules TabStateChanged for each valid message. MainWindow.xaml.cs line 323 handles it through the general active-tab refresh path.

Change direction: deduplicate in the sender and receiver, and disable extraction while adaptive tint is off. Use a narrow notification for a real color change. Preserve reset-to-empty behavior and live setting changes.

Acceptance: repeated identical scrolls cause no repeated host refresh. Changed colors, navigation, dark/light changes and clearing the color still update the correct window.

### F11. The live-control limit is a preference subject to broad exemptions

Evidence: source review. No many-site memory profile was captured.

TabHibernationManager.cs lines 195 through 214 can evict only safe candidates. Its predicate at lines 309 through 332 excludes pinned tabs by default, internal tabs, audio-playing tabs and preview origins. Any in-progress download disables hibernation for every tab in that window. Each window owns a separate manager. The configured count therefore does not provide a process-wide memory ceiling.

These exemptions protect user activity. They also mean the README claim that memory stays strictly bounded regardless of open tabs is stronger than the implementation supports. The new startup behavior already hibernates restored background tabs, including pinned tabs, which reduces restore cost.

Change direction: describe the limit accurately and measure exempt-tab workloads. If memory pressure requires tighter behavior, track download ownership and coordinate a process-wide budget while preserving protected state and explicit sleep preferences.

Acceptance: record the Stride process and its complete WebView2 process tree with several windows, pinned tabs, internal pages, audio and active downloads. Verify tab drag, docking, tear-off and wake behavior before changing eviction policy.

## Low-priority finding

### F12. Favicon disk storage has no retention budget

Evidence: source review. No long-duration disk-growth or renderer-memory leak was reproduced.

Services/FaviconLoader.cs caps its in-memory LRU at 200 decoded images and decodes at 32 pixels wide. Those are useful limits. SaveToDiskAsync writes one PNG per host, but there is no age, count or byte-budget cleanup. History caps visits at 10,000 while the favicon directory can retain hosts that no longer appear in history. The opt-in proxy path also buffers the full response with GetByteArrayAsync before decode.

Change direction: add a bounded background disk-retention pass and an explicit maximum icon response size. Preserve local-only list lookups and the opt-in network policy. Keep retention off tab activation and typing paths.

Acceptance: seed more hosts and bytes than the chosen budget, run cleanup and verify that recent cache hits, concurrent writes and profile isolation remain correct.

## Other reviewed areas and profiling limits

- Startup reads settings and download records synchronously during service resolution. Large downloads amplify initial construction and subscription reconciliation. No main-window startup, first-paint or restored-session latency was measured.
- Layout forces UpdateLayout during interactive resize in WindowChromeManager.cs line 71. TabShrinkPanel measures all children. This remains a frame-time profiling target. Existing drag and resize behavior should not be rewritten from source inspection alone.
- UpdateService streams package bytes to disk and enforces archive limits. It awaits a UI progress notification per 81,920-byte chunk. Coalescing progress is a possible improvement if traces show dispatcher overhead. Updater install time, recovery time and signed installer execution were not measured. The release script was inspected and not run.
- PDF.js contains 372 tracked files totaling about 16.7 MiB. No PDF rendering benchmark was run. Avoid removing worker, font or character-map files without representative document tests.
- T&C Lens is invoked on demand. Its history is capped at 100 entries, requests have a timeout and retry path, and provider input is truncated. Its full history uses JSON storage. No paid API request, provider latency benchmark or extension-host runtime test was run.
- Focus loads blocklists on the thread pool and uses a compiled set for lookup. Custom domains are reparsed per IsBlocked call. This is lower priority than measured internal-page stalls. No large-list memory benchmark was run.
- Extension preparation and inventory move file work to the thread pool, and profile registration is coordinated. Cold external navigation still shares the activation gate identified in F05. Extension-action popups poll size while open. No real extension startup, popup or toolbar load profile was captured.
- Reader availability clones and parses the document, then extraction can parse it again. Resource text is reread per invocation. This is a profiling target for large articles. Reader article data remains cached until tab removal. No retained-heap attribution was performed.
- Link preview is on demand and has explicit dismissal and cleanup paths. No repeated-preview heap profile was captured.
- Local suggestions at 10,000 history records took a median 1.55 ms and allocated 165,968 bytes. Search still scans and materializes matches before selecting eight rows, but this is lower priority than multi-second page work. Online suggestions debounce and cancel. No network latency benchmark was run.
- GetFrequentSites took a median 18.1 ms at 10,000 entries, but no product caller was found. Do not prioritize that dormant method from the benchmark alone.
- Engine teardown unsubscribes settings and WebView events and stops hibernation. Window scopes and view models have cleanup paths. This audit does not establish a general closed-window, GPU or renderer memory leak.

## Validation

- Debug application build passed with 20 compiler warnings and no errors on the initial full build. The warnings concern unawaited calls and nullable references. They are not performance measurements.
- The final Debug application build after the audit changelog entry passed with ten warnings and no errors.
- Release application and diagnostic-tool builds passed. The final incremental tool build had no warnings or errors.
- Release updater build passed with no warnings or errors.
- Final full test run: 1,794 passed, one failed and eight skipped, out of 1,803. The failing test is ReleaseNotesProviderTests.DevelopmentBuild_HasNoInventedReleaseHistory at line 88. It expects an empty release list, while the existing changelog contains the dated 0.1.0 release. No test or release history was changed to force a pass.
- Initial test restore failed under sandboxed NuGet TLS credentials. Host restore succeeded. The first full run then had 42 missing-uBlock-fixture failures plus the release-notes failure. The existing archive was copied from the main checkout after its SHA-256 matched the product's pinned hash. The definitive full run used host access with that fixture present. An intermediate sandboxed rerun was interrupted after TLS failures.
- The first sandboxed probe did not complete WebView2 initialization and was interrupted. Its preliminary timings were discarded. The reported page and store timings come from the completed host run.
- Live checks used generated internal pages and copied assets in real WebView2. They did not exercise manual input, main-window interaction, drag/tear-off, real sites, startup, power use or long-session memory.

### Validation after the fixes

All validation below happened after the fixes were applied. Test builds use Debug and live diagnostic tools use Release.

- A fresh isolated Debug application and test build passed. The Release application and tab-transfer tool build passed with 20 application compiler warnings and no errors. Incremental tool builds can report zero warnings because they reuse the compiled application. The full test build also reports existing analyzer warnings.
- Full C# test run: 1,893 passed, 0 failed, 8 skipped. The count includes the new unit tests listed under Applied fixes.
- The four Node page probes pass: `bounded-downloads-page.mjs`, `bounded-history-page.mjs`, `internal-page-accessibility.mjs` and `theme-color-runtime.mjs`. Eleven checks in total across the four files.
- A complete post-fix `--full` run of the diagnostic tool passed, including the delayed-extension reproduction, native suspension latency samples, assets, engine smoke and UI checks. Its results are copied to [applied-fixes results](2026-10-23-performance-fixes-results.json).
- The F05 reproduction also ran standalone through `--extension-gate` and passed.
- The strengthened F05 rerun passed with the Downloads control sized and its DOM checked before capture. Internal activation took 321 ms while the external activation remained pending. [Closing extension-gate results](2026-10-23-performance-extension-gate-results.json) contain this focused run. The preceding full repeat was interrupted when its new screenshot attempted to capture a zero-height StackPanel child. That partial run is discarded. A Grid host and a 15-second capture timeout corrected the probe. The earlier complete full runs remain the performance evidence.
- A fresh full C# rerun passed 1,893 tests, skipped eight and failed none. The eight skips are existing reader extraction, service and view-model integration cases. Evidence is `performance-finish-full.trx` in the thread-storage directory.
- The real-window TabTearingSmoke run passed all nine internal pages, sleeping-tab tear-off, docking, page identity, draft, timer, history and event preservation, original-window closure, last-tab tear, aborted docking and shutdown recovery. It invokes the production transfer methods rather than OS pointer dragging. Evidence is `performance-finish-transfer6.log` in thread storage. The shutdown case deliberately disposes a control to verify recovery writes despite browser cleanup failure. Its comparative suspension diagnostic observed a disposed never-moved control and is not evidence of relative suspension cost.

The tab-transfer tool initially failed before transfer because it navigated a native core directly while startup navigation was still pending. It now uses `TabEngine.Navigate`, a local virtual-host fixture, explicit navigation failure and timeout checks, current internal-page titles and the supported theme-message format. The extension-gate probe now requires a real Downloads element and script function, bounds its control-creation wait and captures the rendered document. Its earlier null/type assertion could accept a missing element.

Ordinary store mutations now return before durability. They coalesce for 75 ms and persist on a worker. The awaited-flush measurements include that delay, serialization and atomic replacement. Background atomic-write numbers exclude serialization. Explicit history clear and saved-tab SaveAll still wait for durable writes. An abrupt process termination can lose mutations that have not reached disk. Normal final-window shutdown awaits the pending flushes.

## Recommended order

1. Bound History and Downloads documents and eliminate unchanged full download syncs. Fix Downloads opening at larger retained histories.
2. Remove full-store writes and download reconciliation from dispatcher callbacks. Keep persistence ordering and shutdown semantics explicit.
3. Bound reader sanitizer work and decouple extension waits from control creation.
4. Recover and resize wallpapers, then remove saved-tab page reloads and redundant suspension and tint work.
5. Profile resize, startup and the complete browser process tree before changing layout, eviction or Chromium flags.

The latency targets above are proposed acceptance criteria, not promises or measured improvements. No performance improvement has been claimed from this audit alone.

## Reproduction and evidence

[PerformanceAudit tool](../../tools/PerformanceAudit/README.md) contains the run command and scope. It has no new package dependencies. Use a new absolute output directory for each run.

[Timing samples](2026-10-23-performance-results.json) and [asset and document checks](2026-10-23-performance-assets.json) contain synthetic data and runtime details. The raw timing run is also under C:\Users\Daniel\.bb\thread-storage\thr_6etv2jrrh6\performance-audit-20261023-final. The final full test evidence is performance-audit-host.trx in that thread-storage directory. That folder holds the pre-fix baseline. It stays as the record the numbered findings quote.

Post-fix evidence:

- Applied-fix numbers in this report come from the complete post-fix `--full` run copied to [2026-10-23-performance-fixes-results.json](2026-10-23-performance-fixes-results.json). The earlier post-fix live run is C:\Users\Daniel\.bb\thread-storage\thr_6etv2jrrh6\performance-fixes-live-final and the closing repeat is C:\Users\Daniel\.bb\thread-storage\thr_6etv2jrrh6\perf-closing-full2.
- The F05 delayed-extension reproduction runs with the tool's `--extension-gate` mode. In the closing run the inner Downloads tab activated in 145 ms and rendered while the external tab was still waiting on the held registration, and the creation gate stayed free during that wait. The raw folder is C:\Users\Daniel\.bb\thread-storage\thr_6etv2jrrh6\perf-closing-extensiongate.
- Native suspension latency is measured by the page modes on a fresh real control with IsVisible false: five samples with no refusals in both post-fix runs, suspend median 0.36 to 0.68 ms and max 8.4 ms, resume median below 0.05 ms. The API refuses suspension with ERROR_INVALID_STATE while a controller is visible, which matches the product behavior of collapsing background controls first. Memory savings and power effects remain unmeasured.
- Wallpaper package sizes after the fixes appear in the asset results inside the applied-fixes results file. The repair evidence is C:\Users\Daniel\.bb\thread-storage\thr_6etv2jrrh6\wallpaper-optimized-final.
