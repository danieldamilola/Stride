# Handoff / Release Notes Draft — Stride Browser

> Living document: append to the changelog sections as work lands. `ARCHITECTURE_AUDIT.md` proposals are being applied incrementally (see Open items below).

**Last updated:** 2026-08-17

---

## Changelog (release-notes ready)

### 2026-08-17 — Update pipeline rewrite (binary integrity + visible failures)

**Auto-update pipeline fixed end-to-end**
- `UpdateService` no longer downloads installers via raw `HttpClient`. The whole flow (check → verified download → install) now runs through NetSparkle:
  - `SecurityMode.Strict` required a `.signature` sidecar for the appcast that never existed, so the live check silently failed. Switched to `SecurityMode.OnlyVerifySoftwareDownloads`: the appcast rides the HTTPS channel (stops in-transit MITM), while **every installer binary is Ed25519-verified against the appcast's `edSignature` before anything executes** (stops compromised/rogue release uploads). No sidecar needed.
  - `InitAndBeginDownload` → `DownloadFinished` (signature already checked) → `InstallUpdate` (re-checks signature, then runs the installer) with `CustomInstallerArguments=/SILENT`, `UserInteractionMode=DownloadNoInstall`, `RelaunchAfterUpdate=false`.
  - New `AppExitRequested` event (instead of `Application.Current.Shutdown()` inside the service) — the view layer wires it to shutdown; this keeps the service testable.
  - `UpdateFailed` event + `Trace.WriteLine` on every failure path (check, download, signature, install) — the Trace listener is file-wired at startup, so failures are now observable.
  - Env-var overrides `STRIDE_APPCAST_URL` + `STRIDE_UPDATE_PUBLIC_KEY` exist for end-to-end testing only; the signature gate still applies to every download.
- `tools/UpdateSigner` now signs **installer file bytes** (it previously signed the enclosure URL bytes, which can never match what NetSparkle verifies — downloads would have been rejected as corrupt). New CLI: `sign <appcast.xml> <installer-file> <private-key.key>` and `verify <appcast.xml> <public-key-base64> <installer-file>`.
- Removed `MainWindow.CheckForUpdatesInBackgroundAsync` (GitHub-API release check duplicating the NetSparkle badge check).
- **E2E dry run** (`StrideBrowser.Tests/UpdateServiceE2ETests.cs`, 3 tests, real HTTP + real Ed25519 + throwaway key):
  1. appcast check works with **no sidecar**;
  2. a **tampered installer is rejected** before any install (no exit requested);
  3. a correctly **signed installer reaches the install step** (`AppExitRequested`, dummy harmless WinExe runs via NetSparkle's batch).
  - 32/32 tests passing; 0 build errors.

### 2026-08-17 — R3b handlers decoupled from ViewModel + sealed router contract (F5/F6)

- **Dropped `BrowserViewModel` from the 3 handlers that took it** (Core/Settings/Shortcut): `CoreMessageHandler` now uses `NavigationService.Resolve` (the real logic behind `ResolveInput`) and no longer carries the unused `UpdateService`; Settings/Shortcut handlers inject `BrowserSettings` directly (already registered as singleton) instead of the whole VM.
- **New `IAddressEmitter`** (`Engine\Handlers`) — the one `_vm.AddressText = url` UI-sync each navigator did is now a narrow `AddressChanged` event on the router; `MainWindow.OnRouterAddressChanged` sets the VM text.
- **Sealed router contract**: `IWebMessageHandler` now exposes `IEnumerable<MessageRoute>`; `MessageRoute(Key, IsExact, Handler)` record with `Exact(...)`/`Prefix(...)` factories replaces the two leaked `Func` dictionaries. Router keeps exact-first semantics; also forwards `ISettingEmitter` + `IAddressEmitter`.
- **Router unit tests added** (`StrideBrowser.Tests/WebMessageRouterTests.cs`, 7 tests — first router coverage): exact-over-prefix, prefix payload slicing, multi-handler merge, unknown message, exception swallowing, setting/address forwarding.
- 39/39 tests passing; 0 build errors.

### 2026-08-17 — R3a message handlers → Engine layer

- Moved all 9 message-handler files from `Services\MessageHandlers\` to `Engine\Handlers\` (namespace `StrideBrowser.Engine.Handlers`): `IWebMessageHandler`, `ISettingEmitter`, `CoreMessageHandler`, `SettingsMessageHandler`, `OneTabMessageHandler`, `HistoryMessageHandler`, `ShortcutMessageHandler`, `DownloadMessageHandler`, `TCLensMessageHandler`.
- `WebMessageRouter` + `Composition` updated; handler registrations shortened (no more fully-qualified type names).
- Handlers now carry explicit `StrideBrowser.Services` / `StrideBrowser.ViewModels` usings — R3b removes them (drop `BrowserViewModel`/router deps).
- 32/32 tests passing; 0 build errors.

### 2026-08-17 — R2 cosmetic cleanup (F11)

- Test project renamed `SpurBrowser.Tests` → `StrideBrowser.Tests` (folder + csproj, matches `StrideBrowser.Tests` namespaces). All references updated (`Stride.csproj` excludes, `README.md`, `CONTRIBUTING.md`, `THIRD-PARTY-NOTICES.md`).
- Deleted `UpdateZoomIndicator()` stub (no visible zoom indicator) + the `ShortcutActions.UpdateZoomIndicator` delegate and its call sites in `KeyboardShortcutMap`.
- `App.xaml.cs` duplicate `using Microsoft.Extensions.DependencyInjection;` — already gone (audit stale).
- `ARCHITECTURE_AUDIT.md` F11 marked done.

### 2026-08-13 — Code health, update security, architecture audit

**Auto-update security hardening**
- Update verification switched from `SecurityMode.Unsafe` to `SecurityMode.Strict` with an embedded Ed25519 public key (`Services/UpdateService.cs`).
- New `tools/UpdateSigner` CLI (generate / sign / verify) for producing signed appcasts; private key lives in `tools/signing/` (gitignored, never commit).
- `appcast.xml` (v1.1.3) signed and verified; end-to-end Strict-mode verification test passed.
- `tools/**` excluded from main project globs (prevents duplicate-assemblyinfo build break).
- Release workflow documented in `.agents/skills/release-management/SKILL.md`.

**Code health pass (anti-pattern cleanup, behavior preserved)**
- `CustomDownloadManager` — split one god-function (`ResumeDownloadAsync`) into `SendDownloadRequestAsync` + `CopyDownloadStreamAsync`.
- `TabEngine` — added `GetActiveCore()` helper; split `CreateWebViewForTab`, `WireNavigationEvents`, `WireMessageAndWindowEvents` into focused private methods; removed 3 debug writes to `ipc_log.txt` from the message pipeline.
- `TabDialogHandler` — empty catch blocks replaced with proper `Uri.TryCreate` guards (2 criticals → 0); `Wire` split into `ShowScriptDialog` / `ShowPermissionDialog` / `SuppressSpamDialog`.
- `ContentScriptInjector` — inline script strings moved to embedded resources `Resources/Scripts/theme-color.js` + `wallhaven-token.js` (token/host placeholders substituted at runtime).
- `StressTestRunner` — `RunAsync` split into 5 phase methods.
- `DefaultBrowserRegistrar` — `Register` split into 5 registry-block methods.
- Pages — new shared `Services/Pages/TemplateRenderer.cs`; `DownloadPage`/`HistoryPage` deduplicated to delegates over it.
- `MainWindow` — shared `HandleAddressTextChanged` + `NavigateToSuggestion`; 4 duplicate address-bar handlers collapsed.
- `NavigationServiceTests` — 3 identical test methods consolidated into one `[Theory]` (3 cases).

**Fixes**
- Test project was silently broken: referenced a deleted `SpurBrowser.csproj` (later renamed to `StrideBrowser.Tests.csproj`) and targeted `net9.0-windows` while the app targets `net9.0-windows10.0.17763`. Fixed both — the test project now builds and runs.
- Build: 0 errors, 0 warnings. Tests: 29/29 passing.

**Architecture**
- Full architecture audit written to `ARCHITECTURE_AUDIT.md` (god classes `TabEngine`/`MainWindow`, dead code `TabDragDropHandler`, TCLens static-field bridge, Engine↔Services cycle, router contract, service-style inconsistency). **None of the proposals applied yet — pending review.**

---

## Scan status

- Anti-slop scan (slop-detector): real findings 44 → 34 after the health pass. All god functions eliminated except two intentional mechanical mapping tables (`KeyboardShortcutMap`, `SettingsPage`).
- Remaining 34 = one-line API delegates, legitimate DI constructor clones, ConvertBack convention, distinct test cases. Intentional.

---

## Open items / known caveats

- **Update installer integrity:** FIXED (2026-08-17) — see changelog. Installer binaries are Ed25519-verified before install; failures are traced to the log file and surfaced via `UpdateFailed`.
- **Live update flow:** the local E2E dry run (appcast → download → signature → install step) passes, but the *real* release asset has not been exercised: `appcast.xml` currently carries a signature made by the OLD signer (over the enclosure URL), which NetSparkle rejects. **REQUIRED BEFORE NEXT RELEASE:** re-sign `appcast.xml` with the corrected signer against the real installer — `dotnet run --project tools/UpdateSigner -- sign appcast.xml Releases/Stride-win-Setup.exe tools/signing/ed25519_private.key` — verify it, commit, and confirm `ReleaseNotes.md` resolves at the `releaseNotesLink`.
- **Build caveat:** if Stride.exe is running, `dotnet build` compiles fine but the copy step fails (MSB3026/3027, file lock).
- **Architecture roadmap** (from `ARCHITECTURE_AUDIT.md`): R0 baseline ✅ · R1 update pipeline ✅ · R2 docs & test-project naming ✅ · R3a move message handlers to `Engine\Handlers` ✅ · R3b narrow handler dependencies (drop `BrowserViewModel`/router) ✅ · R4 `TabEngine` decomposition (WebViewFactory, WebViewIpcBridge) · R5 `MainWindow` decomposition (TabStripController, WindowLifecycleController, TCLensLauncher) · R6 navigation predicates + test coverage + dead-code removal · R7 release actions (re-sign appcast, ReleaseNotes.md, release a build).

---

## How to release (short version)

1. Bump `<Version>` in `Stride.csproj` and `sparkle:version` + `pubDate` in `appcast.xml`.
2. Host installer at `https://github.com/danieldamilola/Stride/releases/download/vX.Y.Z/Stride-win-Setup.exe`.
3. Re-sign appcast against the installer: `dotnet run --project tools/UpdateSigner -- sign appcast.xml Releases/Stride-win-Setup.exe tools/signing/ed25519_private.key`.
4. Commit appcast to `main`, update release notes (`.agents/skills/release-management/SKILL.md` has full detail).
