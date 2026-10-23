Work on Umber in the repository root. Improve the measured performance problems described in docs/reviews/2026-10-20-performance.md. Use T3's orchestration tools, not shell-launched agent processes.

Read AGENTS.md and .opencode/skills/unslop/SKILL.md first. Record the current git status and preserve every existing staged, unstaged and untracked change. The working tree contains pending theme, site-information, popup-placement, save-status, settings lifetime and shortcut work. Do not reset, stash, clean, overwrite it, commit or push. Keep tab drag, drop, tear-off, privacy controls and extension startup protection intact. Ask before adding NuGet dependencies.

First call orchestrator_capabilities. Find the exact advertised IDs for these OpenCode models:
- DeepSeek 4.1 Flash
- GLM 3.5 Flash
- MiniMax M3.1 Flash
Also find GPT 6.1 Sol for independent review, preferably through Codex.
These are the model names I requested. Do not invent IDs or silently substitute another model. If an exact model cannot be matched, show the available choices and ask me before implementation.

Establish reproducible baselines in Release with an isolated UMBER_DATA_DIR and synthetic data. Never benchmark against my real history or modify my live browser profile. The prior local sample found a 28.49 ms median full-history write, a 4,777.4 ms median render of 10,000 history rows, another 164.3 ms accessibility pass, and eight identical theme messages for eight synthetic scroll events. These are measurements from one machine, not universal targets. Recheck them. The cold-tab semaphore delay and repeated suspension work are source findings that still need controlled reproduction.

Then use delegate_task to run three implementation workers asynchronously in parallel. Give each worker a complete brief, its exact provider/model ID, tests to add and exclusive file ownership. Save the returned task IDs. Let completion notifications return their results rather than polling continuously.

1. DeepSeek owns tab activation and suspension. Focus on Engine/TabEngine.cs and Engine/TabHibernationManager.cs. The creation semaphore remains held while CreateWebViewForTab awaits extension readiness, whose waiter budget is 15 seconds. Let unrelated internal tabs become usable during another tab's slow extension setup. Preserve single creation per tab, engine-owned cancellation, navigation-intent checks and the extension barrier. Investigate duplicate suspension and activation work, and fix it only if reproduced. Add controlled delayed-gate and rapid-switch regression tests.

2. GLM owns history persistence and the History page, including Services/HistoryStore.cs, IHistoryStore.cs if needed, Resources/Pages/History.html, Services/Pages/HistoryPage.cs and Resources/Scripts/internal-page.js. Remove full synchronous history-file rewrites from the UI path using ordered, bounded persistence with shutdown and failure handling. Prevent stale writes from recreating cleared history. Bound the History DOM and avoid sorting, formatting and replacing thousands of rows per keystroke. Make accessibility enhancement incremental while preserving keyboard and dialog behavior. Add persistence and large-history tests.

3. MiniMax owns theme-color telemetry, including Resources/Scripts/theme-color.js, Engine/ContentScriptInjector.cs, Engine/WebViewIpcBridge.cs and narrowly required MainWindow wiring. Suppress duplicate color extraction/messages and host refreshes, including while adaptive tint is disabled. Preserve empty-color resets, live toggles and theme changes. Profile resize/layout only if evidence points there; do not rewrite custom chrome or tab layout speculatively. Add telemetry regression tests.

Workers must not edit shared project files, CHANGELOG.md, the same test files or another worker's owned files. Request cross-module edits through the coordinator. All delegated workers share this checkout, so do not run builds, tests or performance measurements concurrently with other workers' writes. The coordinator performs serialized validation after workers stop editing. Consolidate the changelog once.

After integration, delegate a fresh read-only code review to GPT 6.1 Sol. Supply the original requirements, baseline dirty-tree record, workers' diffs, performance evidence and test results. Have it check correctness, cancellation, races, stale writes after clear, event lifetime, WebView2 thread affinity, accessibility, privacy and tab-transfer regressions. It must report concrete file and line references and distinguish measured results from inference. Return its findings to the responsible worker and allow up to two repair/review rounds. Do not mark an unresolved finding fixed.

Build Umber, run relevant regression tests, then run the full suite once after all edits stop. Exercise the actual browser with a fresh profile and repeat the same benchmarks without concurrent workloads. Use separate output folders if my running app locks Debug output. Compare before/after timings, allocations, rendered node counts and message counts. Preserve errors and restart notices in Settings and the newly fixed rightward site card.

Finish with the exact changes, before/after measurements, tests actually run, reviewer findings and unresolved limits. Do not claim a performance improvement from compilation or source inspection alone. Stop for my confirmation that it works before committing or pushing.
