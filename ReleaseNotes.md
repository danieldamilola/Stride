# Release Note: v1.2.3

**Features**
* **Taskbar Icon Sharpness**: Rebuilt stride.ico and stride-light.ico from the 1024 masters with the full Windows size set 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 so the taskbar finds a native 24px entry at 100 percent DPI instead of scaling the nearest size. Both ICOs are now embedded as resources.
* **No Dots Truncation**: Removed ellipsis trimming across tab titles, URL label, suggestion lists, link preview, drag ghost, and context menu preview so clipped text ends clean with no dots. The URL label now uses MaxWidth 200 instead of a fixed 80 width.
* **Tab Drag Reorder**: Tabs can now be reorganised by dragging. A solid pill with the tab favicon and title follows the cursor, edge zones auto scroll overflowing strips, and the drop commits the new order. Cancelling restores the start slot.
* **Full Names Shrink To Fit**: Full Names mode now shares the strip width across every tab, capped at full card width. Opening more tabs shrinks every card so all tabs stay visible with no scrolling. Titles collapse to favicons on tiny cards, and the close button moves from the card end to the favicon overlay as cards shrink. Compact mode is unchanged.
* **Adaptive Toolbar Color**: New Layout toggle. On keeps today's behavior, tinting the toolbar with the active site theme. Off keeps the neutral toolbar on every site.
* **New Skull App Icon**: New dark and light skull mark across the window icon, taskbar, installer, and tab fallback favicon, with resized sets and a rebuilt multi resolution ICO. Dark skull shows by default and the light skull follows the light theme.

**Bug Fixes**
* **Download Closes Browser**: Fixed the browser shutting itself when starting a download. Download landing pages call window.close once the file starts, and the WebView2 control answers that by closing the whole host window, not just the tab. Stride now detaches that built-in handler when wiring each tab, so only the tab closes and the download keeps running. The download handler also guards every native read, posts store updates without blocking, and falls back to the URL or a default file name when the native path is missing. Proven by stride.log showing WebView2's own window-close handler on the shutdown stack.

# Release Note: v1.2.2

**Features**
* **Updated App Icons**: New Stride logo across the app with larger, more visible taskbar and Task Manager icons, refreshed tab fallback favicon for new tabs, and updated branding in Onboarding and Release Notes. Includes multiple sizes 16, 32, 48, 128, 256, 512 and a new multi-resolution ICO.
* **Toolbar Auto-Scroll**: Opening a new tab now automatically scrolls the tab strip to bring the new tab into view when tabs overflow. Previously new tabs could be hidden behind the address bar and required manual scrolling.

**Bug Fixes**
* **Tab Strip Overflow**: Fixed an issue where newly opened tabs were hidden when the toolbar overflowed. The strip now ensures the newly created tab is visible without affecting manual scroll position when viewing older tabs.

# Release Note: v1.2.1

**Features**
* **Internal Pages Design Sync**: Redesigned the Onboarding and Release Notes pages to share the flat, utilitarian layout and styling (system fonts, standardized colors) used across the rest of the browser's internal pages.
* **Quick Access Links**: Added "What's New" and "Getting Started" buttons to the System & About section of Settings to easily reopen these pages at any time.

**Bug Fixes**
* **Security and Privacy**: Resolved multiple findings from the recent architecture and codebase security audits, including hardening the named pipe ACL, ensuring IPC tokens scope correctly, and properly cleaning up privacy data on exit.
* **YouTube Tools**: Script configurations are now strictly bound to the window object to prevent closure bugs on reload, and invalid playback speeds revert safely to 1.0x.
* **Fullscreen Modes**: The application correctly aborts entering fullscreen if the monitor positioning call fails, falling back smoothly to a maximized state, and F11 synchronizes correctly with video fullscreen.
* **Update Verification**: Downloaded update packages are securely verified without locking errors.
* **New Tab Page**: Background images from user or local folders safely URL-encode their path components.
# Critical Manual Update Required
The auto-updater in version 1.1.3 has a bug. Clicking Install below will fail.

To fix this and get all future updates automatically, you must download the latest setup file manually.

[Download Stride 1.2.0](https://github.com/danieldamilola/Stride/releases/download/v1.2.0/Stride-win-Setup.exe)

# Release Note: v1.2.0 Native Seamless Updater

**Features**
* **Native Seamless Updates**: The update flow has been completely redesigned. A native gear in a tray icon appears on your toolbar when an update is available. Clicking it shows a real-time circular progress ring while it downloads in the background, followed by a green checkmark to install.
* **Invisible Micro-Updater**: When you click the green checkmark, Stride swaps files behind the scenes using a new invisible Windows GUI executable updater, eliminating wizard dialogs, UAC prompts, and NetSparkle dependencies.
* **Reader Mode**: Press Ctrl+Shift+R on any article to switch it to a clean, distraction-free reading view. Powered by Mozilla Readability, scripts are disabled while reading, and links clicked inside reader exit gracefully back to the live page. The reader mode icon is now a static toolbar button and can be toggled in Settings. The hotkey Ctrl+Shift+R still works anytime.
* **Link Preview**: Hold Alt and click or hover any link to peek at it in an isolated preview window without leaving your current page. Open it in a new tab or your current tab from the preview toolbar, or press Esc to dismiss.
* **Customizable Tab Dim**: New Performance settings let you dim sleeping and hibernated tabs, with independent opacity sliders so you can tune how faded background tabs look.
* **Pinned Tab Resource Controls**: Two new toggles allow pinned tabs to sleep and hibernate like normal tabs when you want maximum memory savings. Off by default, pinned tabs stay fully alive unless you opt in.
* **What's New Page**: Stride now shows a styled release-notes page once after each update, using the same design language as onboarding. It never interrupts first-run setup and remembers what you have seen.
* **Redesigned Settings**: Settings were reorganized into clear sections with a search bar, making it easy to find any option fast.
* **First-Run Onboarding**: A guided welcome flow covers address bar style, privacy defaults, built-in tools, search engine, theme, accent color, and import. It appears only on first launch, never again on every start.
* **Downloads Page Redesign**: Borderless cards with subtle backgrounds match the History page philosophy. White progress bars replace green, and neutral status dots are used. File type icons use Icons8 instead of plain text abbreviations. Completed downloads show only the filename.
* **Popup Dialog Redesign**: All dialog windows now have rounded corners and neutral surface-colored buttons. The bright green accent OK button that clashed with the dark theme has been removed.
* **Dialog Window Positioning**: Dialogs no longer float above all applications. Topmost has been removed so dialogs stay with Stride, and the offset drop shadow has been removed.
* **Dynamic Context Menu**: Rebuilt the right-click menu to be context aware. The top row shows Back, Forward, and Reload. Plain pages show Find in Page and Select All. Editable fields show Undo, Redo, Cut, Copy, and Paste. Links show Open in new tab and Copy link. Images show Save image and Copy image URL. Reader View appears when available. The T&C Lens extension is accessible from the menu. Keyboard shortcuts display for supported actions.
* **Release Notes Auto-Open**: Stride opens internal://releasenotes automatically after an update installs.

**Bug Fixes**
* **Micro-Updater Merge Crash**: Fixed a crash where updates touching existing subdirectories were silently dropped because the updater skipped directories that already existed. The updater now merges directories recursively and validates the package before swapping.
* **Tab Dim Flicker**: Stale suspend callbacks could mark a tab you just switched to as sleeping, re-dimming it during restoration or crash recovery. Suspension generations now invalidate outdated callbacks.
* **Sleep Setting Race**: Disabling Tab Sleep now reliably clears the sleeping state instead of racing with in-flight suspends.
* **Reader Extraction Hang**: Extraction now times out instead of freezing the UI when a page is hibernated or unresponsive.
* **Preview Safety**: Link previews validate window dimensions and suppress stray downloads triggered inside the peek window.
* **Update Pipeline Hardening**: Installer downloads are Ed25519 signature verified against the appcast before anything executes, and the micro updater now validates zip integrity, handles pre-release tags, and uses ArgumentList with retries.
* **Link Preview and Reader Mode**: Alt+Click now displays the link preview window instead of only dimming. The Reader Mode button now successfully activates.
* **Right-Click Menu**: Fixed a COM error that prevented the context menu from rendering. The application now checks for the existence of link, image, or selection data before reading it.
* **Downloads Hang**: Fixed a bug where downloads froze at 100 percent. .NET Garbage Collection dropped native events, and SmartScreen suspended background downloads. SmartScreen is now disabled by default.
* **Download UI Commands**: The Pause, Resume, and Cancel buttons on active downloads now function.
* **Local HTML Files**: Stride now opens local .html files launched from Windows Explorer or the command line. Stride supports being the default offline HTML viewer.
* **New Tab Reloads**: Adding or removing a shortcut updates the UI instantly without reloading the entire New Tab page.
* **New Tab Shortcuts**: Adding a shortcut with special characters or importing bookmarks no longer breaks the New Tab page. Shortcuts are now passed using Base64 encoding.
* **Toolbar Icon Toggles**: Disabling the Show Reader Icon setting instantly removes the icon from the toolbar.

**Under the Hood**
* Removed all NetSparkle dependencies. `UpdateService` now uses `HttpClient` to hit the GitHub REST API and download the zip archive. `build-release.ps1` now outputs both a zip for the micro updater and an exe installer for new users, with atomic zip creation and clean publish.
* Added hardening to the micro updater: recursive directory merge, zip validation for `Stride.exe`, retry and timeout handling for GitHub API including pre-release tags, dynamic version wiring into Inno Setup, self update via pending .new files, and robust process handling with ArgumentList and grace period for WebView2 locks.
* Message handlers moved into the engine layer with a sealed router contract, and WebView2 environment plus IPC ownership extracted into dedicated classes.
* Test suite grew to over 160 unit tests covering navigation policies, router behavior, reader sanitizing, link preview policy, and update verification.
* Extracted URL parsing and command-line argument dispatch into dedicated services, and removed the NetSparkleUpdater WPF row from Settings.

