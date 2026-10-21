# Stride handoff: setup, updates and light theme

Date: 2026-10-21
Repository: C:\dev\Stride
Branch: feat/download-history
Latest pushed commit: faf29c0d, Redesign setup and updates pages and replace the light theme
Earlier pushed commit: 7921626c, Improve downloads with a recent panel and dated history
Upstream: origin/feat/download-history

Latest follow-up: the user then requested removing custom accents entirely. See docs/handoffs/2026-10-21-theme-only.md for the new uncommitted changes and current validation. The earlier status below describes faf29c0d.

## User requests and current status

The user confirmed the download popup and history redesign work and look clean. Those changes were committed and pushed to the new branch as requested. The earlier download handoff is docs/handoffs/2026-10-21-downloads.md.

The user then reported a washed-out wallpaper in light mode, requested a complete redesign of Get started and What's New, and requested a researched replacement for the whole light palette. These follow-up changes are implemented and verified. The user explicitly requested commit and push after receiving the preview and validation results. Committed and pushed those changes as faf29c0d on feat/download-history. HEAD and origin/feat/download-history matched at handoff. The separate performance work remains local and uncommitted. No PR was created for this branch in this session.

The user prefers neutral dark and light surfaces, and does not want every component colored with the accent. Their note about starting to use workspaces was not a request to move this thread or checkout.

## Light palette

Researched the official Radix Gray source and scale guidance, Fluent neutral-color guidance and W3C contrast guidance:

- https://www.radix-ui.com/colors/docs/palette-composition/understanding-the-scale
- https://github.com/radix-ui/colors/blob/main/src/light.ts
- https://fluent2.microsoft.design/color
- https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html

The palette starts from Radix Gray. Page background is F9F9F9, chrome F0F0F0, surfaces FFFFFF, hover E8E8E8, selected E0E0E0, decorative borders D9D9D9, and input outlines 8D8D8D. Primary text is 202020. Secondary and muted text are 5F5F5F, slightly darker than Radix Gray 11 so they also exceed 4.5:1 on the selected surface. Danger is B42318.

Themes/LightTheme.xaml and the light and system-light declarations in Resources/Pages/InternalPage.css use matching semantic colors. Page fallbacks in Settings, History, Downloads, Onboarding and ReleaseNotes were updated. Downloads now uses the shared semantic names rather than its previous private aliases.

Native changes:

- Theme dictionaries own the address field, tab card, hover, pressed, selection and scrollbar brushes. MainWindow's dark-only fallback brushes were removed.
- App.xaml uses dynamic theme brushes for menus, inputs and scrollbars so already-created controls follow a theme switch.
- Light selected tabs use a neutral fill. Dark normal tabs still follow the existing accent wash. Downloads stays neutral in both themes.
- MainWindow.ApplyAccentColor also updates AccentWashColor so the dynamic dark selected-tab brush follows a user accent change.
- The close-window icon follows button foreground and becomes white on its danger hover background.
- ToolbarTintAdapter uses the theme palette when no site tint applies. A tinted toolbar owns a coherent scoped surface and text palette, including a dark site on a light app. Leaving the tint removes the overrides.
- ToolbarTintResolver retains a light base when rejecting a near-white or strong-green site color. The old rule forced dark chrome even in light mode.

The dark palette is retained. Extra dark resource keys preserve previous control states. Tab favicon dimming still follows the user's sleeping and hibernation preferences.

## Wallpaper

Resources/Pages/InternalPage.css used a 55 percent overlay of the theme background. In light mode that became a nearly white wash. Explicit light and system-light now make .bg-overlay transparent. Dark keeps the previous scrim. The keyboard hints get a small readable light surface over a photo.

The final wallpaper check loaded a real bundled image before capture. Earlier harness output had no image because its test output lacked the wallpaper assets. That was a harness packaging issue, fixed by copying the existing assets into its output. No wallpaper file was edited.

## Get started

Resources/Pages/Onboarding.html was rebuilt as five steps: Welcome, Appearance, Search, Preferences and Ready. It has a step list, native radios and checkboxes, neutral buttons, a compact browser illustration, a summary and a deliberate default-browser action. It supports narrow windows and moves focus to the new heading when changing steps. Heading focus is announced without drawing a large focus rectangle. Interactive controls retain focus indicators.

Removed the inert import UI and unsupported feature claims. No import backend was added. The default-browser action only runs on a user click. Set up later and Start browsing use the existing real completion route.

Functional corrections:

- The hosted page uses the actual settings snapshot, with no stale localStorage overriding it.
- The HTTPS control maps the snapshot's httpsForce to the real forceHttps setting route.
- Host hydration accepts the existing token-prefixed onboarding-data message.
- Late initial snapshots do not overwrite a choice already made on the page.
- Real save failures restore the control and show an error. Restart-required changes show a persistent restart notice.
- OnboardingPage.SerializeSettings is shared by initial rendering and OnboardingMessageHandler.HandleReady.
- SettingsMessageHandler sends setting results to the active onboarding page as well as Settings pages.

Files: Resources/Pages/Onboarding.html, Services/Pages/OnboardingPage.cs, Engine/Handlers/OnboardingMessageHandler.cs, Engine/Handlers/SettingsMessageHandler.cs, and Services/Pages/PageAssets.cs. PageAssets caches the existing embedded app icon for both redesigned pages.

## What's New

Resources/Pages/ReleaseNotes.html and Services/Pages/ReleaseNotesPage.cs were rebuilt. The newest published release has categories with grouped bullets. Older releases are expandable and reachable from a release list. Version, date, category and entry text are escaped. Release-list navigation scrolls without changing the verified document URL, so host commands keep working.

The current 0.1.0 development build has no published release sections. The page says so and does not invent history or claim the installation is up to date. Settings and Continue browsing use the existing host routes. No ReleaseNotes.md was recreated.

## Verification

- Debug application builds passed with existing warnings and no errors.
- Focused setup, routing and settings checks: 164 passed, zero failed, zero skipped.
- Focused palette and theme checks: 160 passed after correcting secondary text contrast on selected tabs. The initial 646464 value was slightly below 4.5:1 there; 5F5F5F passes.
- Isolated follow-up full suite: 1,603 passed, zero failed, eight skipped, total 1,611. This snapshot excludes the separate performance edits. Do not call this a clean full-worktree run.
- Final presentation checks after removing duplicate version copy and the heading focus rectangle: 83 passed, zero failed, zero skipped.
- Native setup checks covered the five-step progress, keyboard theme selection, native and persisted search/tab/HTTPS settings, resource settings, restart messages, a real locked-profile write failure and recovery, completion, skip, both themes and narrow windows.
- Native update checks covered the real development empty state, a controlled multi-release fixture, grouped categories, older-release expansion, document identity, Settings, Continue and narrow layout.
- Final native light checks: ten passed across chrome, address input, context menu, Downloads, History, Saved tabs, setup, updates and system light/dark.
- Wallpaper checks: five passed with a real loaded image, explicit light/dark and system light/dark.
- Final native run after presentation cleanup: 37 passed assertions and 0 failed assertions. See setup-final-runtime.log and setup-final-results/observations.json. The new welcome capture has no heading focus rectangle, and the development update page has one version label.

Native checks use temporary profiles under thread storage. The save-failure check holds a read-only FileStream on that temporary settings file, confirms the host setting stays unchanged, then releases it and verifies recovery. It never locks or changes the user's real profile. The default-browser button was not invoked because that opens Windows Settings. No installer was executed.

The native window image produced by WPF RenderTargetBitmap contains only chrome and leaves the WebView2 airspace blank. Inspect the separate WebView2 page captures for actual page content. This is a screenshot-method limitation.

## Validation artifacts

Root: C:\Users\Daniel\.bb\thread-storage\thr_hkhatuaqyi\download-fix-verification

- follow-up-suite.log is the isolated full-suite result.
- light-theme-tests-final.log and presentation-final-tests.log are the focused final results.
- final-preview-build.log is the app preview build.
- light-final-runtime.log and light-final-results/observations.json contain the ten final light checks.
- wallpaper-final-runtime.log and wallpaper-final-results contain the loaded-photo checks and captures.
- setup-results and setup-light-final-results contain setup and update captures and checks.
- setup-light-final-runtime.log has an earlier timeout caused by a harness predicate expecting the old OneTab title. The real page is titled Saved tabs. Correcting the predicate made the light-only check pass.
- setup-final-results contains the final setup, update and light-palette screenshots.
- Program.cs and DownloadFixVerification.csproj are the external native harness. Default item globs are disabled so the isolated source snapshot is not accidentally compiled into the harness.
- staged-download-history is the isolated validation copy. The initial name comes from the download commit; it was updated with the follow-up changes for the full suite. MainWindow.xaml.cs in that copy contains only the download base and the new AccentWashColor assignment.

Preview: C:\dev\Stride\bin\SetupPreview\Stride.exe. Close the existing Stride instance first so single-instance forwarding does not hide the new build. The redesigned pages are accessible from Settings, under About.

## Preserve separate performance work

These were already in progress and remain uncommitted. Do not discard or stage them with the UI work:

- Engine/ContentScriptInjector.cs
- Engine/TabCreationCoordinator.cs
- Engine/TabEngine.cs
- Engine/TabHibernationManager.cs
- Engine/WebViewIpcBridge.cs
- MainWindow.xaml.cs
- Resources/Scripts/theme-color.js
- StrideBrowser.Tests/TabActivationPerformanceTests.cs
- StrideBrowser.Tests/theme-color-performance.mjs

MainWindow.xaml.cs is shared. The single AccentWashColor assignment and its blank line are committed in faf29c0d. The adaptive-tint handler refactor and the BOM remain uncommitted from the performance work. Preserve them and review the diff before staging.

The earlier full-worktree suite had a source-text failure in YouTubeNetworkTamperingTests.RegistrationIsAwaitedBeforeTheFirstExternalNavigationOnly. It expects `if (!ServeBuiltInPage(tab))`, which the separate activation refactor removed. The isolated UI suite passes because it excludes that refactor.

## Next session

Read AGENTS.md and .opencode/skills/unslop/SKILL.md. Inspect branch and status. All requested download, wallpaper, setup, update-page and light-palette work is committed and pushed. This handoff refresh is a local documentation change after the push. No UI work remains pending from this session. Preserve the separate performance edits and wait for the next task. Do not commit or push further changes without user confirmation. Keep pending changelog entries under Unreleased and versions at 0.1.0. No release or installer was published.
