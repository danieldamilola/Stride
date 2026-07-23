# Third-Party Notices

Stride is licensed under the MIT License (see `LICENSE`). It bundles or
downloads the following third-party components at runtime.

## Dark Reader

- **License:** MIT
- **Source:** https://github.com/darkreader/darkreader
- **Usage:** `Resources/Scripts/darkreader.min.js` is embedded directly in the
  Stride binary and injected into pages when Force Dark Mode is enabled
  (`ContentScriptInjector`). MIT is compatible with Stride's MIT license;
  this notice satisfies Dark Reader's attribution requirement.

## uBlock Origin

- **License:** GPL-3.0
- **Source:** https://github.com/gorhill/uBlock
- **Usage:** uBlock Origin is **not** bundled with or distributed by Stride.
  On first launch, `ExtensionManager` downloads the official release archive
  directly from GitHub, verifies it against a locally pinned SHA-256 hash
  (Trust-On-First-Use), and loads it into the WebView2 extension host as an
  unpacked, unmodified extension. Because Stride never redistributes uBlock
  Origin's source or binary — it fetches the unmodified upstream release at
  runtime, on the user's machine, from the copyright holder's own repository —
  this does not trigger GPL-3.0's distribution/copyleft obligations for
  Stride itself. If this download behavior changes (e.g. Stride starts
  bundling the extension in its own installer), this section must be revisited
  and Stride's own licensing re-evaluated for GPL compatibility.

## Microsoft.Web.WebView2 / CommunityToolkit.Mvvm / Microsoft.Extensions.DependencyInjection

- **License:** MIT
- **Source:** NuGet packages, referenced in `SpurBrowser.csproj`.
