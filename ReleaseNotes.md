# Release Note: v1.2.1

**Features**
* **Dynamic Context Menu**: Rebuilt the right-click menu to be context-aware. The top row shows Back, Forward, and Reload buttons. The menu adapts to the clicked target. Plain pages show Find in Page and Select All. Editable fields show Undo, Redo, Cut, Copy, and Paste. Links show "Open in new tab" and "Copy link". Images show "Save image" and "Copy image URL". Reader View appears when available. The T&C Lens extension is accessible from the menu. Keyboard shortcuts display for supported actions.
* **Release Notes Auto-Open**: Stride opens stride://release-notes automatically after an update installs.
* **Internal Pages Design Sync**: Redesigned the Onboarding and Release Notes pages to share the flat, utilitarian layout and styling (system fonts, standardized colors) used across the rest of the browser's internal pages.
* **Quick Access Links**: Added "What's New" and "Getting Started" buttons to the System & About section of Settings to easily reopen these pages at any time.
* **UI Tweaks**: Removed icon boxes on the Downloads page for a cleaner look. Adjusted the default tab hibernation opacity from 15% to 50%.

**Bug Fixes**
* **YouTube Tools**: The enhancer and unhook scripts no longer run on lookalike domains that merely contain "youtube.com" in the hostname. Live setting changes no longer revert on the next navigation, re-injection no longer stacks observers and listeners on every settings change, and enhancer settings now apply live to open tabs.
* **YouTube Enhancer**: Asking for a quality the video does not offer now picks the closest lower quality instead of jumping to the highest. Manually chosen playback speed is respected until the next video. Settings apply after multi-ad breaks. Quality values are validated before reaching the generated script.
* **YouTube Unhook**: Removed debug console logging. The "More from YouTube" sidebar section is now hidden on non-English YouTube interfaces too.
* **Video Fullscreen**: Clicking fullscreen on a video now covers the taskbar and fills the whole monitor with no light border around the content. Exiting restores the window to its previous size.
* **Fullscreen State Sync**: F11 and video fullscreen now share one state machine, so pressing F11 while a video is fullscreen no longer leaves the toolbar or window state stuck.
* **Security: Virtual Hosts**: The temp.stride virtual host that exposed the user's temp directory to every web page has been removed. The local.assets and user.assets mappings now use the more restrictive DenyCors access kind.
* **Security: WebView2 Browser Arguments**: Removed --allow-file-access-from-files so the file:// origin sandbox is enforced by Chromium's default. The file:// scheme can no longer be used to read other local files from a downloaded HTML page.
* **Security: IPC Token Race**: The per-session IPC token gate in the WebView IPC bridge now re-verifies the active tab's source URL immediately before every trusted message, eliminating a race window where a tab could navigate to an external site and receive a token-protected message.
* **Security: Internal Page Inputs**: Theme color and link preview messages are now gated behind the IPC token, the wallhaven token script is no longer registered on every page, and shortcut labels / categories / descriptions in the Settings page are HTML-encoded before reaching the page.
* **Security: Downloads**: Download filenames are sanitized to strip path traversal characters and reserved device names before being passed to the system download handler.
* **Security: Dialog Suppression**: The right-hand tab dialog handler no longer suppresses dialogs based on a keyword list. Removing adblock no longer changes dialog behavior.
* **Security: Update Pipeline**: Downloaded update packages are sanity-checked: minimum size, valid ZIP, and at least one entry before the updater exe is launched.
* **Security: Named Pipe**: The single-instance pipe now validates every message: bounded size, JSON shape, argument count cap of 32, and rejects any argument containing control characters or longer than 2048 bytes.
* **Privacy: Clear Data on Exit**: When the ClearDataOnExit setting is enabled, the new exit path also deletes history.json, session.json, OneTab.json, the favicon cache directory, downloads.json, the trace log, and the crash log.
* **Privacy: Favicon Cache**: The in-memory favicon cache uses an LRU eviction policy capped at 200 entries, and no longer caches failed lookups as null entries across navigations.
* **Security: Crypto Hygiene**: Switched from MD5 to SHA-256 for focus mode blocklist cache file naming.
* **Privacy: Trace Log Rotation**: The diagnostic trace log is now capped at 1 MB, preventing unbounded growth.
* **Link Preview & Reader Mode**: Alt+Click now displays the link preview window instead of only dimming the screen. The Reader Mode button now successfully activates Reader Mode.
* **Right-Click Menu**: Fixed a COM error that prevented the context menu from rendering. The application now checks for the existence of link, image, or selection data before reading it from the WebView2 target.
* **Downloads Hang**: Fixed a bug where downloads froze at 100 percent. .NET Garbage Collection dropped native events, and Microsoft SmartScreen suspended background downloads. SmartScreen reputation checks are now disabled by default.
* **Download UI Commands**: The Pause, Resume, and Cancel buttons on active downloads now function.
* **Local HTML Files**: Stride now opens local .html files launched from Windows Explorer or the command line. Stride supports being the default offline HTML viewer.
* **New Tab Reloads**: Adding or removing a shortcut updates the UI instantly without reloading the entire New Tab page.
* **New Tab Shortcuts**: Adding a shortcut with special characters or importing bookmarks no longer breaks the New Tab page. Stride passes shortcuts to the frontend using Base64 encoding.
* **Toolbar Icon Toggles**: Disabling the "Show Reader Icon" setting instantly removes the icon from the toolbar.

**Under the Hood**
* Extracted URL parsing and command-line argument dispatch from the main window into CommandLineUrlParser and StartupCoordinator services.
* Removed the "NetSparkleUpdater WPF" row from Settings. Updated updater comments. Deleted the UpdateServiceE2ETests test.

# Release Note: v1.2.0

**Features**
* **Native Updates**: Redesigned the update flow. A gear icon appears on the toolbar when an update is available. Clicking it shows a circular progress ring during the background download, followed by a green checkmark.
* **Micro-Updater**: Clicking the green checkmark restarts the application. A background Windows executable swaps the files without wizard dialogs or UAC prompts.

**Under the Hood**
* Removed NetSparkle dependencies.
* UpdateService uses HttpClient to call the GitHub REST API and download the zip archive.
* The build-release.ps1 script outputs a zip archive for the micro-updater and an exe installer for new users.
