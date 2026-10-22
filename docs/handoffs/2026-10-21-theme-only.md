# Theme-only colors handoff

Date: 2026-10-21
Repository: C:\dev\Stride
Branch: feat/download-history
Previous pushed commit: faf29c0d

## Request and status

The user requested removing custom accent colors entirely. System, Light and Dark should own component colors, similar to Helium. This follow-up is implemented and verified. The user explicitly requested committing, pushing and creating a PR after reviewing the UI. This commit includes the UI follow-ups and preserves the separate performance work. The previous UI work is already pushed. No release or installer was published.

Orchestra could not launch DeepSeek 4.1 Flash or GLM 5.3 Flash workers. Worktree mode returned HTTP 400, personal project threads must use a personal workspace. Shared mode returned HTTP 409, personal project threads must reuse a personal workspace. The primary agent implemented and reviewed the changes directly.

## Implementation

- Removed accent pickers and related scripts from Settings and Get started.
- Removed BrowserSettings.AccentColor, its reset/setter/normalizer and MainWindow's startup and runtime resource overrides.
- Old JSON accentColor fields are ignored. Other preferences survive load/save, and the obsolete key disappears on the next save. The removed IPC setting route reports unsupported.
- Removed accent arguments and template substitutions from InternalPages, per-page renderers, host call sites, tests and the tracked internal-page verification tool.
- Removed Accent and AccentWash resources from both native themes. Selected tabs and loading gradients now use neutral theme values. Site-info loading and download progress use ProgressBrush.
- InternalPage.css owns theme roles for primary button background/text/hover/pressed, focus rings, selection, links, toggle track/knob, progress and danger. Secondary controls keep surface and hover colors. System mode follows the same palettes.
- Light primary buttons use charcoal with white text. Dark primary buttons use a pale neutral surface with dark text. Danger actions remain distinct. Wallpaper and download-history behavior are retained.
- Updated CHANGELOG.md under Unreleased. Versions stay at 0.1.0.

## Validation

- Debug application build passed with existing warnings and no errors.
- Final affected tests: 370 passed, zero failed, zero skipped. These cover themes, component contrast, settings migration, routing, page rendering, setup, updates and download templates. This was a focused run, not the full suite.
- Native WPF/WebView2 run after the Settings background correction: 35 checks passed in temporary profiles. Covered actual persisted theme controls, removed picker and IPC route, neutral native tabs/progress, page markup, primary button foreground/background, checked toggle track/knob and system mode.
- Corrected an accidental Settings .content-area background binding to --toggle-knob. It again uses --bg-dark. Confirmed original backgrounds of 111111 in dark mode and F9F9F9 in light mode.
- Inspected native captures of light Settings, corrected dark Settings and dark setup preferences.
- git diff --check passed apart from existing CRLF advisory warnings.

Artifacts: C:\Users\Daniel\.bb\thread-storage\thr_hkhatuaqyi\theme-only-verification

- background-build.log and background-runtime.log are the final background correction results.
- theme-background-results contains the final 35 observations and corrected screenshots.
- build.log and runtime.log
- theme-results/observations.json and light/dark page captures
- Program.cs and ThemeOnlyVerification.csproj for native checks

Focused test log: C:\Users\Daniel\.bb\thread-storage\thr_hkhatuaqyi\theme-only-tests-final.log
Preview: C:\dev\Stride\bin\ThemeOnlyPreview\Stride.exe. Close the running instance first.
Original pre-edit source backups: C:\Users\Daniel\.bb\thread-storage\thr_hkhatuaqyi\theme-only-backup

## Preserve separate work

The existing performance changes remain uncommitted in ContentScriptInjector, TabEngine, TabHibernationManager, WebViewIpcBridge, MainWindow.xaml.cs, theme-color.js and the untracked TabCreationCoordinator and performance tests. TabEngine and MainWindow.xaml.cs also contain this session's accent-removal edits. Review their diffs carefully before staging. Do not stage all changes together.

The prior full-worktree suite had a source-text assertion failure in YouTubeNetworkTamperingTests.RegistrationIsAwaitedBeforeTheFirstExternalNavigationOnly caused by the separate activation refactor. It was not changed in this session.

## Follow-up: consistent page headers and searches

The user requested moving search next to the header actions and using consistent header and search sizes across internal pages. These changes are included in the confirmed UI commit.

- Added explicit page-title, page-toolbar, page-actions and page-search classes in InternalPage.css. Page titles use 24px text with a 32px line height. Header search fields are 320px wide and 36px high, with 13px text and 16px search icons.
- Downloads places its search in the same action row as the clear icons. History uses the same header and search sizing.
- Saved tabs uses the same header and keeps search available in the empty state. Search markup now belongs to OneTab.html instead of being generated conditionally in OneTabPage.cs. Empty-state and no-results messages remain distinct.
- Settings search uses the same dimensions and aligns right within its content area. Heading sizes also match in setup, updates, diagnostics, error and Focus pages. The new-tab web search and dialog headings keep their own roles.
- At narrow widths, the search and actions share the row below the title. Existing page backgrounds and theme colors are unchanged.
- Focused page tests: 156 passed, zero failed, zero skipped.
- Native WPF/WebView2 checks: 82 passed across light/dark headers, preserved page backgrounds, search geometry, action alignment, actual filtering, empty saved tabs, and 600px/420px layouts. Inspected wide Downloads and History and narrow light Downloads captures.
- Native artifacts: C:\Users\Daniel\.bb\thread-storage\thr_hkhatuaqyi\page-header-verification. Final output is headers-final-results, runtime-final.log and tests.log. The first verifier run returned a label string instead of a boolean. Correcting the verifier produced the passing run; it was not a product failure.
- Updated preview: C:\dev\Stride\bin\PageHeaderPreview\Stride.exe. Close the existing instance first.

## Follow-up: search border and focus

The user rejected the permanent search border and oversized inner focus outline. InternalPage.css now gives .page-search a transparent idle border and a single theme-colored border on focus-within. Its input suppresses its own focus outline and shadow. The wrapper remains the visible focus indicator for mouse and keyboard users. Old light-theme search-bar border rules exclude page-search so they cannot bring the idle border back.

Native checks: 40 passed across Downloads, History, Saved tabs and Settings in dark and light mode. Real WebView2 clicks focused the fields. Checks covered idle, focused and blurred states, no duplicate input outline, and stable height. Inspected the focused dark History screenshot. No application logic changed, so the earlier focused page tests were not repeated.

Artifacts: C:\Users\Daniel\.bb\thread-storage\thr_hkhatuaqyi\search-focus-verification. Preview: C:\dev\Stride\bin\SearchFocusPreview\Stride.exe. The user authorized committing and pushing these changes and creating a PR.
