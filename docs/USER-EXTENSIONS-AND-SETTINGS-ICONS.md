# User extensions and settings icon consistency

Two separate pieces of work, written down before any code. Nothing here is implemented.

## Part 1: user-installed extensions

### What exists today

`ExtensionManager` already loads extensions, and it is hardcoded to uBlock Origin.

- `LoadUnpackedAsync` (`Services/ExtensionManager.cs:261`) calls `Profile.AddBrowserExtensionAsync` on an unpacked folder. This is the entire mechanism a user-installed extension would use.
- `GetExtensionsAsync` (`:274`), `ToggleExtensionAsync` (`:287`), `RemoveExtensionAsync` (`:300`) already exist and work on any extension.
- `AreBrowserExtensionsEnabled = true` is already set in `Engine/WebViewFactory.cs:49`.
- Extensions persist into the WebView2 profile on their own, so a user-added extension survives restarts with no extra work.

So the capability is there and unused. What is missing is UI and sourcing.

### Why there will never be a store

WebView2 does not support installing extensions from the Edge Add-ons store. This is a Microsoft position, not something we can code around. From the WebView2 feedback tracker:

> WebView2 does not plan to support installing extensions in WebView2 through Edge/Edge Store. Edge and WebView2 are different entities and an extension being in Edge Store does not mean it is licensed to be used in WebView2. What WebView2 does support is a BYO extension mechanism where developers source their own extensions and are compliant with the extension's license.

A store we build would be us republishing other people's extensions, which puts the licensing question back on us per extension. That conversation belongs with each author, not with us by default.

### Decision: load unpacked first

Build this in order. Stop after each step and decide whether the next is worth it.

**Step 1. Load unpacked, no store**

Add a settings section, probably under the existing "Installed Extensions & Core Libraries" area.

- A "Load unpacked extension" row that opens a folder picker.
- Validate the chosen folder has a `manifest.json` at its top level before calling `LoadUnpackedAsync`.
- Copy the folder into `%LocalAppData%\StrideBrowser\extensions\<name>` rather than registering it in place. The API docs state that changing an extension's content after installation causes it to be removed from the profile. Registering from a location the user might edit or delete is asking for that failure.
- List what is currently installed with a toggle and a remove button, calling the existing methods.

`AddBrowserExtensionAsync` error values worth handling explicitly, from the docs:

| Error | Meaning |
|---|---|
| `ERROR_NOT_SUPPORTED` | Extensions disabled in the environment |
| `ERROR_FILE_NOT_FOUND` | No valid `manifest.json` |
| `E_ACCESSDENIED` | Path component starts with `_`, reserved by the system |
| `E_FAIL` | Unknown install failure |

`LoadUnpackedAsync` currently swallows every exception into `Trace.WriteLine` and returns void, so a user picking a bad folder would see nothing happen. It needs to return a result the UI can show.

**Step 2. Curated catalogue, if wanted after step 1**

Not a public store. A pinned list we ship, each entry with a version and a SHA-256, exactly as uBlock already does in `EnsureUBlockDownloadedAsync` and `VerifyTofuHashAsync`.

Reuse the existing download, hash verify, extract, and `FindManifestDirectory` code rather than writing a second path.

Benefit beyond convenience: no unknown extension can inject scripts into the browser. The YouTube scroll work is the argument for this. One bad CSS selector caused days of breakage, and a third-party extension has vastly more reach than anything in this repo.

Cost: we take on vetting and updating every entry.

**Step 3. CRX fetching from the Edge CDN**

Works. People do it. Skipping it unless asked.

The endpoint returns a `.crx` that needs a header strip and unzip before `AddBrowserExtensionAsync`. It is a licensing question per extension and buys us nothing over a curated list.

### Prerequisite work before any of this

`ExtensionManager.InitializeAsync` assumes uBlock is the only extension that exists. Read it before adding a second one.

- `CleanupOldVersions` enumerates directories under `ExtensionsDir` and deletes anything not matching `uBlock0_{version}`. If user extensions land in that same directory, this will delete them on every launch. They need their own directory or the cleanup needs to learn to skip them.
- The version-cycling block (`:82` to `:92`) removes and re-adds by folder path when it detects a uBlock version change. It finds uBlock by name, so it should not catch a second extension, but that needs confirming rather than assuming.
- `EnsureUBlockDownloadedAsync` clears a target directory that exists without a manifest. Harmless today. Worth a look once the directory holds more than one thing.

Extensions must not be re-registered on every launch. WebView2 persists them, so registration should happen once at install and then be left alone. If registration runs every startup, a user extension gets reinstalled every launch, and reinstalling from the same path is not free.

### Open questions

- Should a user extension be able to see Stride's own injected scripts? A content script and our injected content script run in the same world, so an extension could conflict with the theme colour, link preview, or YouTube scripts. Probably worth a warning in the docs, or isolating our scripts into a separate world if the API allows it.
- Do we want an extension to be able to toggle `AdBlockEnabled`? That setting drives both uBlock and the parked adnuke script. An extension enabling uBlock behind the user's back is confusing.

## Part 2: settings icon consistency

### What was reported

The settings nav icons do not match the rest of the software. Reference screenshot is Obsidian's settings dialog.

### What I could actually confirm

I have read the nav rail but have not compared it against the app's own iconography in a running build, so the rest of this is a read of the code and not a diagnosis.

What the code shows:

- The rail is `Resources/Pages/Settings.html:745` to `:778`. Eight items, each an inline SVG.
- The icons are Feather icons on a 24x24 grid, `stroke-width="2"`, `fill="none"`, `currentColor`. Consistent with each other.
- Sources: gear (general), palette (appearance), shield (privacy), lightning bolt (performance), play triangle (youtube), keyboard (shortcuts), concentric circles (focus mode), chip (system).

Real problems visible from the code:

1. **YouTube is a filled play triangle.** It is the only filled icon in the rail. Every other icon is an outline. This reads as inconsistent next to the rest on its own, and it is also a duplicate of YouTube's own brand mark, which is exactly the kind of thing that looks borrowed rather than part of the app.
2. **Focus mode uses concentric circles.** That is a radar or target symbol. The feature is about blocking sites. A shield with a slash, or an eye with a bar, says the thing more plainly.
3. **Eight rail items against more section headers.** The rail has eight entries while the page has more than eleven `section-header` blocks (general, theme, layout, toolbar buttons, privacy, performance, tab appearance, youtube enhancer, youtube unhook, focus, installed extensions, system). Some are nested inside others, which is fine, but the mapping is not obvious from the markup. Worth confirming every header is reachable through the rail or its scroll position, and that nothing is orphaned.
4. **Two YouTube sections, one rail item.** YouTube Enhancer and YouTube Unhook are separate sections sharing one rail entry. Obsidian groups its rail into labelled groups, which is the pattern in the reference screenshot and would split these cleanly.
5. **Rail item labels are longer than the reference.** "Privacy & Security" and "Keyboard Shortcuts" next to short items like "General" makes the rail ragged. Either shorten or group, not both.

### Approach if we do this

Follow the reference screenshot's structure rather than copying its icons:

- Group the rail with small uppercase group labels, matching the existing `.section-header` treatment at `Settings.html:111` so the rail and the page read as one system.
- One icon style across all items, all outline, same stroke width and grid.
- Drop the filled play triangle for an outline mark.
- Replace the focus mode circles with something that reads as a restriction.
- Keep every icon inline SVG. There is no icon font or sprite sheet in the project and adding one for eight icons is not worth it.

Decide whether to keep eight entries or match the reference's grouping. Grouping is the better answer given the YouTube split, but it is a larger change.

### Not doing without asking

- Replacing the icon set wholesale. The existing icons are internally consistent, they are just not distinctive to Stride and one is visually off. A full swap is a design change, not a consistency fix.
- Any change to the settings page layout beyond the rail.

## Order of work

1. Read `ExtensionManager.InitializeAsync` end to end and fix `CleanupOldVersions` so it cannot delete user extensions. Prerequisite, and it is a real bug the moment step 1 ships.
2. `LoadUnpackedAsync` returns a result instead of swallowing. Settings row with a folder picker, install into its own directory, list with toggle and remove.
3. Decide on a curated catalogue based on whether step 2 gets used.
4. Icons: fix the filled triangle and the focus mode mark. Cheap, low risk, fixes the actual complaint.
5. Rail grouping. Larger, separate.