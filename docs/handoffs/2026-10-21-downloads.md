# Stride download work handoff

For the later wallpaper, setup, update-page and light-theme work, read [the follow-up handoff](2026-10-21-setup-and-theme.md).

Date: 2026-10-21
Repository: C:\dev\Stride
Branch: feat/download-history
Base commit: 178e560c

## User direction and git status

The user requested the replacement download popup, fixes for the download flow, a compact Helium-style recent list, and a cleaner full history page. They explicitly requested a new branch, commit and push. After the final UI update they confirmed that it works and looks clean.

The download changes are committed separately from concurrent performance work on feat/download-history. Read git log and git status for the final commit and push state. Preserve the remaining worktree changes.

## Resulting UI

- The toolbar opens a compact recent-download panel in the source window. An inactive window gets a status badge.
- It shows the latest five records within 24 hours and retains older live transfers. Full history stays on its own page.
- Downloading rows have Pause and Cancel icons. Paused live transfers have Resume. Completed rows have Open file and Show in folder icons.
- The full page groups records by local download date, using Today, Yesterday and full dates. Search filters filenames and source URLs.
- Completed filenames are neutral underlined links. There is no Completed dot or redundant Completed label. Size and time remain visible.
- Row actions are icons with tooltips and accessibility labels. The filename opens the file. Folder, copy link, retry, pause, resume, cancel and remove actions appear as applicable.
- Removing a history record leaves the file on disk. Active transfers cannot be removed through that action.
- Existing executables use their embedded icon. Other types use their Windows file association, including missing records. A plain document is the fallback when lookup fails.
- Missing files use a muted struck-through name and File deleted label. Open and folder actions are unavailable.
- The page, progress ring, completion badge and selected Downloads tab use neutral colors. The Downloads tab overrides the general AccentWash selection rule. Other tabs retain their existing selection style.

## Download behavior

- Native WebView2 popup stays suppressed and the replacement panel keeps the browsing tab open.
- Toolbar progress observes collection and transfer changes. Unknown length uses indeterminate progress.
- Speed and ETA use monotonic samples. Metadata stays visible after completion.
- Pause and Resume invoke the live native operation. A restored record offers Restart and starts a fresh native transfer from zero. No cross-process byte-range resume is claimed.
- Hidden WebView2 controllers share the browser environment and profile for restarts. They deny prompts and close on terminal state.
- Retrying keeps the existing history row, coalesces duplicate requests and chooses a safe unique destination.
- Cancellation and clearing cancel pending retries so removed records cannot be resurrected.
- Downloads opens or reuses its existing tab. Escape closes the panel. Progress updates retain focused controls.

## Code map

- App.xaml contains the selected Downloads tab override in FaviconTabListStyle.
- Controls/DownloadsPanel.xaml and its codebehind define the compact WPF panel.
- ViewModels/DownloadsPanelViewModel.cs observes recent items and routes commands.
- Services/Downloads/RecentDownloadList.cs selects recent and live records.
- Services/UI/DownloadsPopupController.cs owns placement, focus, owner-window checks and subscriptions.
- Models/DownloadItem.cs contains shared action and display state. FileIcon, FileIconDataUri and IsMissing are runtime-only fields.
- Services/Downloads/DownloadFileInfoService.cs performs local Windows Shell lookup off the UI thread with at most three workers. It caches association and executable icons. File presence samples refresh after five seconds. Completion invalidates the earlier active-transfer sample so a newly completed file is not marked missing.
- Services/DownloadJson.cs SerializeForPage adds runtime icon and missing-file data to page snapshots. Disk persistence excludes that data.
- Services/Downloads/DownloadCoordinator.cs owns native and file actions. NativeDownloadRequest.cs owns retry controllers. DownloadDestination.cs chooses unique paths. DownloadProgressTracker.cs calculates transfer metrics.
- Engine/Handlers/TabDownloadHandler.cs tracks native state and readable failures. Engine/TabEngine.Downloads.cs starts requests and reuses the full page.
- Engine/Handlers/DownloadMessageHandler.cs routes page actions and coalesces async sync requests. Copy and remove routes are restricted to Downloads by IpcSecurityPolicy.
- MainWindow.xaml defines the toolbar indicator and panel. MainWindow.xaml.cs only wires the controller and full-page helper.
- ViewModels/BrowserViewModel.cs owns toolbar progress subscriptions.
- Services/DownloadStore.cs restores interrupted records and saves state, errors and paths.
- Resources/Pages/Downloads.html defines date groups, search, neutral styles, accessible icon actions, confirmation dialogs and stable DOM updates.
- Composition.cs and Engine/EngineDependencies.cs supply the shared services. Optional dependencies keep older test harnesses compatible.
- The legacy CustomDownloadManager remains registered for compatibility. The real route uses native downloads.

## Verification

- Focused history, download, template and IPC run: 133 passed, zero failed, zero skipped before the tab-color refinement.
- Final history and WPF template run: 18 passed, zero failed, zero skipped, including the new selected-tab tests in both themes with a magenta accent.
- Native history run: 33 passing assertions, no failed assertions. Real local 8 MB transfers covered pause, resume, restart, duplicate retry coalescing and destination safety. Page checks covered date groups, Windows icons, neutral links despite a magenta accent, icon controls, search, missing files, record removal without deleting the file, both themes and narrow layout.
- Exact staged snapshot: Debug build passed. Full suite passed with 1,590 tests, zero failed and eight skipped, total 1,598. The first snapshot run lacked the existing ignored uBlock ZIP and had fixture failures. Copying the original 1.73.0 ZIP into the snapshot, verifying its hash and rebuilding resolved those failures. No extension source changed.
- Final native run: 34 passing live assertions, including the selected Downloads tab with a magenta accent. Screenshot: history-final-results/downloads-tab-neutral.png. The final run retained the redesigned page checks and real transfers.
- The automated check did not open installers or launch a downloaded file. It verified Open file and Show in folder controls and their command bindings.
- Earlier compact-popup verification had intermittent waits for the full page's periodic sync. The final diagnostic compact run used an explicit sync request. The redesigned page now requests sync on initial load, focus, visibility changes and after actions, as well as its timer. Do not claim the old timing issue has a separately established root cause.
- The restricted sandbox caused WebView2 native GPU/browser failures and TLS problems for network fixtures. Native checks use an isolated host process with a temporary profile and local test files.

Validation files: C:\Users\Daniel\.bb\thread-storage\thr_hkhatuaqyi\download-fix-verification.

- staged-history-build.log and staged-history-suite-final.log contain the exact staged-source checks.
- history-final-runtime.log and history-final-results/observations.json contain final native checks.
- history-tests.log
- history-neutral-tab-tests.log
- history-runtime.log and history-results/observations.json
- history-results/page-dark-completed.png shows the date groups and actual icons.
- history-results/page-light.png and page-narrow.png show theme and width checks.
- DownloadFixVerification.csproj and Program.cs run the real WPF/WebView2 harness.
- staged-download-history contains an export of the exact index for independent build and test checks.

Preview: C:\dev\Stride\bin\DownloadFixPreview\Stride.exe. Close an existing Stride instance before opening it so single-instance forwarding does not hide the new build.

## Preserve separate performance work

Concurrent performance changes remain outside the download commit. They touch:

- Engine/ContentScriptInjector.cs
- Engine/TabCreationCoordinator.cs
- Engine/TabEngine.cs
- Engine/TabHibernationManager.cs
- Engine/WebViewIpcBridge.cs
- MainWindow.xaml.cs
- Resources/Scripts/theme-color.js
- StrideBrowser.Tests/TabActivationPerformanceTests.cs
- StrideBrowser.Tests/theme-color-performance.mjs

Three files were staged with only download edits reconstructed from HEAD: Engine/TabEngine.cs, Engine/WebViewIpcBridge.cs and MainWindow.xaml.cs. Their remaining diffs belong to the separate work. Do not stage them wholesale or discard them.

The earlier full worktree suite had one source-text failure in YouTubeNetworkTamperingTests.RegistrationIsAwaitedBeforeTheFirstExternalNavigationOnly at line 237. It expects `if (!ServeBuiltInPage(tab))`, removed by the separate activation refactor. This is separate from the staged download changes.

## Next session

1. Read AGENTS.md and .opencode/skills/unslop/SKILL.md. Inspect status and the branch before editing.
2. Confirm the pushed branch and commit. The user has already authorized and confirmed the download UI.
3. Preserve the performance edits. Review and validate them separately.
4. Do not recreate ReleaseNotes.md. Versions remain 0.1.0 and changes stay under Unreleased.
5. No installer or update package was requested or published in this session.

## Tooling notes

- rg is unavailable. Use focused git grep or Python searches.
- Read source as UTF-8. Use Unicode escapes or set PowerShell OutputEncoding before piping non-ASCII Python scripts.
- Avoid overwriting the user's running Debug build. Use isolated output paths.
- Do not stop the user's Stride process or modify their normal browser profile during tests.
