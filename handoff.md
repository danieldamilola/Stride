# Handoff / Release Notes Draft — Stride Browser

> Living document: append to the changelog sections as work lands. Review `ARCHITECTURE_AUDIT.md` for the proposed architecture changes (not yet applied).

**Last updated:** 2026-08-13

---

## Changelog (release-notes ready)

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
- Test project was silently broken: referenced a deleted `SpurBrowser.csproj` and targeted `net9.0-windows` while the app targets `net9.0-windows10.0.17763`. Fixed both — the test project now builds and runs.
- Build: 0 errors, 0 warnings. Tests: 29/29 passing.

**Architecture**
- Full architecture audit written to `ARCHITECTURE_AUDIT.md` (god classes `TabEngine`/`MainWindow`, dead code `TabDragDropHandler`, TCLens static-field bridge, Engine↔Services cycle, router contract, service-style inconsistency). **None of the proposals applied yet — pending review.**

---

## Scan status

- Anti-slop scan (slop-detector): real findings 44 → 34 after the health pass. All god functions eliminated except two intentional mechanical mapping tables (`KeyboardShortcutMap`, `SettingsPage`).
- Remaining 34 = one-line API delegates, legitimate DI constructor clones, ConvertBack convention, distinct test cases. Intentional.

---

## Open items / known caveats

- **Update installer integrity (not yet fixed):** `UpdateService.DownloadAndInstallUpdateAsync` downloads the installer via raw `HttpClient` without verifying the binary against the appcast `edSignature`. Only the appcast is cryptographically verified.
- **Live update flow unproven:** appcast enclosure points at `github.com/danieldamilola/Stride/releases/download/v1.1.3/Stride-win-Setup.exe` — the asset must actually exist, and `appcast.xml` must be committed to `main` for the raw.githubusercontent URL to resolve. No live check → badge → download → install run yet. Failures are only `Debug.WriteLine` (invisible in release).
- **Build caveat:** if Stride.exe is running, `dotnet build` compiles fine but the copy step fails (MSB3026/3027, file lock).
- **Architecture proposals** in `ARCHITECTURE_AUDIT.md` are not applied — roadmap order: quick wins (delete dead `TabDragDropHandler`, TCLens transfer service, ThemeManager → DI) → handler layer move → `TabEngine`/`MainWindow` decomposition.

---

## How to release (short version)

1. Bump `<Version>` in `Stride.csproj` and `sparkle:version` + `pubDate` in `appcast.xml`.
2. Host installer at `https://github.com/danieldamilola/Stride/releases/download/vX.Y.Z/Stride-win-Setup.exe`.
3. Re-sign appcast: `dotnet run --project tools/UpdateSigner -- sign appcast.xml`.
4. Commit appcast to `main`, update release notes (`.agents/skills/release-management/SKILL.md` has full detail).
