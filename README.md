# Stride

A Windows desktop web browser built on WPF and WebView2 (Chromium).

## Features

- Tab hibernation and LRU eviction to keep memory usage bounded with many tabs open
- Built-in ad blocking (network-level filters + bundled uBlock Origin)
- Dark mode forcing via Dark Reader
- YouTube enhancer (default quality/speed/loop) and Unhook (distraction removal)
- OneTab-style tab consolidation, history, downloads, and a Focus Mode with
  domain blocklists
- Session restore, custom keyboard shortcuts, accent theming

## Requirements

- Windows 10 1809+ (x64)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Evergreen WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually already present on Windows 11 / recent Windows 10)

## Building

```
dotnet build SpurBrowser.csproj -c Release
```

## Running tests

```
dotnet test SpurBrowser.Tests/SpurBrowser.Tests.csproj
```

## Publishing / Installer

```
dotnet publish SpurBrowser.csproj -c Release -r win-x64 --self-contained false -o publish
```

Then build `stride-setup.iss` with [Inno Setup 6+](https://jrsoftware.org/isinfo.php)
to produce `installer/Stride-Setup.exe`.

## Architecture

```
App.xaml.cs         Startup, crash handling, DI bootstrap
Composition.cs       DI container registration (composition root)
MainWindow.xaml(.cs) Window chrome, toolbar, tab strip, command bar — view layer
ViewModels/          Thin UI state (BrowserViewModel)
Engine/              TabEngine owns all WebView2 instances and tab lifecycle;
                     AdBlockFilter and ContentScriptInjector are extracted
                     single-purpose helpers used by TabEngine
Services/            Persistence (settings/history/session/downloads/OneTab),
                     navigation resolution, extension management, focus mode
Services/Pages/      Server-side (C#) HTML generation for internal pages
Resources/Pages/     Static HTML templates for internal pages
Resources/Scripts/   Injected JS (YouTube enhancer/unhook/adnuke, Dark Reader)
Models/              Plain data + observable models
Helpers/             Small, focused utilities (atomic file writes, JS escaping,
                     window chrome/native interop, app data paths)
```

Data (settings, history, session, favicons, extensions, Focus Mode cache) is
stored under `%LocalAppData%\StrideBrowser`. Logs (`stride.log`, `crash.log`)
live in the same directory — never in the working directory or the repo.

## Testing

`SpurBrowser.Tests` covers the pure-logic pieces that don't require a live
WebView2/WPF window: URL resolution (`NavigationService`), keyboard shortcut
parsing (`KeyboardShortcutMap`), and Focus Mode domain matching
(`FocusDomainMatcher`). Most of `TabEngine` and `MainWindow` are integration
surfaces against WebView2 and the WPF window and are not currently covered by
automated tests — manual verification is required for tab lifecycle,
hibernation, and rendering changes.

## License

MIT — see [LICENSE](LICENSE). Third-party components (Dark Reader, uBlock
Origin) are documented in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
