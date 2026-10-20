# Stride performance review

Date: 2026-10-20. Repository: C:\dev\Stride. Baseline commit: 6e83ddfd, with the current uncommitted UI and settings work present.

## Scope and evidence

Reviewed tab activation, WebView creation, extension readiness, tab suspension, history persistence and rendering, address suggestions, toolbar telemetry, resize layout and download updates. Measurements used a Release build, a temporary STRIDE_DATA_DIR and generated history. No production history or profile data was read by the benchmark.

The measurements cover isolated operations on this machine. They do not establish which path accounts for every reported slowdown. No specific slow interaction was supplied during the review. Startup, scrolling and memory across real sites still need scenario-based profiling. Source-only findings below have not been reproduced with a delayed real extension or a many-tab workload.

## 1. History search rebuilds thousands of rows on every keystroke

Priority: high. Reproduced in WebView2.

Resources/Pages/History.html:367 filters, sorts, groups and formats the matching visits, builds every row and replaces the list with innerHTML at line 454. The input handler at line 487 calls render for each input event. Collapsing a group hides rows after they have been created; it does not bound the DOM work.

With the query Example matching all generated records, five repetitions produced these medians:

- 1,000 visits: 340.6 ms for synchronous render, with 5,736 DOM elements.
- 10,000 visits: 4,777.4 ms for synchronous render, with 55,671 DOM elements.

These timings exclude final painting and the subsequent accessibility callback. They are elapsed JavaScript times in the shipped page, not estimates from source size.

Resources/Scripts/internal-page.js:6 scans the document for settings rows, interactive history headers and links. Its body-wide observer at line 73 runs that scan again after child-list or class changes. The measured median callback time after rebuilding the 10,000-visit list was another 164.3 ms. The 1,000-visit callback median was 14.2 ms.

Fix direction: bound the rendered rows with pagination or lazy groups, cache normalized search/date fields, and update accessibility only for added or changed controls. Preserve keyboard operation, dialog focus, accessible names and expanded state. Merely debouncing a four-second render leaves a four-second stall after typing stops.

Verification: exercise opening, searching, expanding and keyboard navigation with 1,000 and 10,000 records. Record render and accessibility timings separately. Assert the DOM remains bounded when a search matches all records.

## 2. Completing navigation rewrites the full history file on the UI thread

Priority: high. Storage cost reproduced; call path confirmed by source.

Engine/TabEngine.cs:1815 dispatches completion handling to the WPF dispatcher. UpdateTabFromWebView calls _historyStore.Add at line 2029. Services/HistoryStore.cs:30 holds its lock while appending and calling Persist. Persist serializes the complete list and calls AtomicFileWriter.WriteAllText at line 132. The writer performs synchronous file replacement.

For a 10,000-record synthetic history, the resulting file was 1,215,383 bytes. After five warm-up additions, 25 timed additions had a median of 28.49 ms and a p95 of 44.76 ms. Median allocation on the calling thread was 2,432,440 bytes per addition. This was a standalone store benchmark on the WPF thread, not a measured end-to-end navigation delay.

Fix direction: separate in-memory updates from ordered persistence and coalesce writes, with an explicit shutdown flush and failure behavior. Keep clear-history and clear-on-exit correct. A delayed older snapshot must never overwrite a newer snapshot or recreate history after a clear. Do not add a new storage package without approval.

Verification: ordered concurrent additions, clear while a write is pending, failed writes, shutdown flush and restart recovery. Compare UI-thread time and allocation with the same dataset.

## 3. The creation semaphore also covers extension startup waiting

Priority: high for affected cold-tab scenarios. Source-confirmed coupling, not a measured delay in this review.

Engine/TabEngine.cs:487 acquires the per-engine _activationGate. The gate remains held across CreateWebViewForTab and is released at line 531. CreateWebViewForTab awaits AwaitExtensionGateAsync at line 1209 before it returns. Services/ExtensionStartupCoordinator.cs:45 gives an extension waiter a 15-second budget.

A tab whose first external navigation waits for extension registration can therefore hold the creation gate while a newly selected tab waits to create its own control. That includes a new internal page, even though internal pages do not require their own extension gate. A tab switch cancels display activation, but intentionally does not cancel the shared registration.

Fix direction: separate serialized control creation from per-tab first-navigation readiness. Preserve one shared creation operation per tab, UI-thread COM access, navigation-intent checks, the extension barrier and engine-owned cancellation. Do not fix this by bypassing extension checks or cancelling registration whenever the selected tab changes.

Verification: hold tab A's extension reconciliation with a controlled test and open internal tab B. B must become usable without waiting for A's gate. Also check rapid A/B/A selection, closing during creation, popup adoption, hibernation wake and transfer between windows.

## 4. Scrolling sends unchanged theme colors through the full tab refresh path

Priority: medium. Duplicate messages reproduced; downstream refresh confirmed by source.

Resources/Scripts/theme-color.js:112 already throttles extraction to 250 ms and ignores hidden documents. It does not suppress an unchanged color. Its scroll listener is registered at line 132. On a static synthetic page, eight scroll events spaced 300 ms apart produced eight native messages carrying exactly the same color.

Engine/ContentScriptInjector.cs:96 registers this script without checking AdaptiveToolbarTint. Engine/WebViewIpcBridge.cs:224 schedules TabStateChanged for each authenticated theme message, even when the color matches. MainWindow.xaml.cs:305 then enters the general active-tab refresh path. ToolbarTintAdapter's own cache prevents its repeated animation, but that cache runs after the earlier refresh work.

Fix direction: suppress unchanged values at the sender and defensively in the receiver. Avoid extraction and messages while adaptive tint is disabled, with a correct live enable/disable path. Preserve the empty-color reset, site changes, dark/light changes and switching between windows.

Verification: identical scroll events should not repeatedly refresh the host; changed colors and a transition to no color must still update it. Confirm behavior with adaptive tint disabled and then enabled without reloading.

## 5. Tab switching repeats suspension work for already sleeping tabs

Priority: medium profiling target. Source observation only.

Engine/TabHibernationManager.cs:70 iterates all live controls and calls TrySuspendAsync for every background control. It does not skip a confirmed sleeping tab or coalesce an in-flight suspension. Several ActivateAsync paths call this method. MainWindow.xaml.cs also has explicit activation after SwitchTo in keyboard cycling, while the tab-state handler activates newly selected tabs.

Measure suspension calls and dispatcher time during repeated switching with 10 and 50 live tabs. If redundant calls are confirmed, coalesce them without losing the existing active-tab, transferred-tab and disposal checks. Preserve audio, downloads, previews and the user's sleep preference.

## Other profiling targets

Services/WindowChromeManager.cs:63 forces UpdateLayout during native resizing. Services/UI/TabShrinkPanel.cs:124 measures every tab, and TabStripController also forces layout when revealing a selected tab. Profile resize frame times with many tabs before changing these paths. The current resize and drag behavior has regression tests and should not be rewritten on the strength of source inspection alone.

This review did not establish a renderer memory leak or a GPU configuration problem. Existing pending SettingsMessageHandler changes already release updater event subscriptions and should be preserved.

## UI changes completed alongside this review

- Routine successful saves leave no Saved text or empty status gap. Errors and restart notices remain visible.
- The site-information popup has explicit custom placement. It opens rightward below the button and clamps its horizontal position to the owning window. It can open above the button if screen space requires it.

Validation passed for the current working tree:

- Debug compilation completed with existing compiler warnings.
- All 88 selected tests passed, including placement math and existing settings tests. The full suite was not run.
- The real WPF window passed runtime checks for rightward placement and containment, plus the existing site-information checks.
- Live WebView2 message checks confirmed hidden ordinary success, visible save failure, and a visible restart notice without Saved text.

The initial benchmark restore failed with sandboxed NuGet TLS credentials. Restoring with normal host credentials succeeded and the Release runs completed. No dependencies were added. The Windows input helper was unavailable, so native interaction validation used a programmatic WPF harness and rendered controls rather than manual mouse input.

## Reproduction artifacts

The isolated benchmark project and raw measurements are in:

C:\Users\Daniel\.bb\thread-storage\thr_hkhatuaqyi\performance-review

Run its PerformanceReview.csproj in Release with an output directory argument to repeat the history and status checks. Supply telemetry as the second program argument to run the static-page telemetry probe instead. The results folder contains results.json and telemetry-results.json. The datasets and harnesses are synthetic and disposable.

The orchestration handoff is in 2026-10-20-t3-performance-prompt.md beside this report. The performance findings have not been implemented in the product during this review.
