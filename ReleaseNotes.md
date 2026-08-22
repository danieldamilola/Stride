# Release Note: Reader Mode & Link Preview

**Features**

* **Reader Mode**: Press `Ctrl+Shift+R` on any article to switch it to a clean, distraction-free reading view. Powered by Mozilla Readability, scripts are disabled while reading, and links clicked inside reader exit gracefully back to the live page. The reader mode icon is now a static toolbar button — auto-detection is disabled — and can be toggled on or off in Settings. The hotkey `Ctrl+Shift+R` still works anytime.
* **Link Preview**: Hold `Alt` and click (or hover) any link to peek at it in an isolated preview window without leaving your current page. Open it in a new tab or your current tab from the preview toolbar, or press `Esc` to dismiss.
* **Customizable Tab Dim**: New Performance settings let you dim sleeping and hibernated tabs, with independent opacity sliders so you can tune how faded background tabs look.
* **Pinned Tab Resource Controls**: Two new toggles allow pinned tabs to sleep and hibernate like normal tabs when you want maximum memory savings. Off by default, pinned tabs stay fully alive unless you opt in.
* **What's New Page**: Stride now shows a styled release-notes page once after each update, using the same design language as onboarding. It never interrupts first-run setup and remembers what you have seen.
* **Redesigned Settings**: Settings were reorganized into clear sections with a search bar, making it easy to find any option fast.
* **First-Run Onboarding**: A guided welcome flow covers address bar style, privacy defaults, built-in tools, search engine, theme, accent color, and import. It appears only on first launch, never again on every start.
* **Downloads page redesign**: Borderless cards with subtle backgrounds match the History page philosophy. White progress bars replace green, and neutral status dots are used. File type icons use Icons8 instead of plain text abbreviations like "ISO" or "HTML". Completed downloads show only the filename; the progress bar, size, time, and status text are all hidden once finished.
* **Popup dialog redesign**: All dialog windows now have rounded corners and neutral surface-colored buttons. The bright green accent OK button that clashed with the dark theme has been removed.
* **Dialog window positioning**: Dialogs no longer float above all applications. `Topmost="True"` has been removed so dialogs stay with Stride, and the offset drop shadow has been removed.

**Bug Fixes**

* **Tab dim flicker**: Stale suspend callbacks could mark a tab you just switched to as sleeping, re-dimming it during restoration or crash recovery. Suspension generations now invalidate outdated callbacks before they can set the sleeping flag.
* **Sleep setting race**: Disabling Tab Sleep now reliably clears the sleeping state instead of racing with in-flight suspends.
* **Reader extraction hang**: Extraction now times out instead of freezing the UI when a page is hibernated or unresponsive.
* **Preview safety**: Link previews validate window dimensions and suppress stray downloads triggered inside the peek window.
* **Update pipeline hardening**: Installer downloads are Ed25519 signature verified against the appcast before anything executes, so compromised uploads can never run on your machine.

**Under the Hood**

* Message handlers moved into the engine layer with a sealed router contract, and WebView2 environment plus IPC ownership extracted into dedicated classes for maintainability.
* Test suite grew to over 160 unit tests covering navigation policies, router behavior, reader sanitizing, link preview policy, and end-to-end update verification.