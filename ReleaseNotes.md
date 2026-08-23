# Release Note: Unreleased

**Features**
* **Dynamic Context Menu**: Rebuilt the right-click menu from scratch to be context-aware, inspired by Zen Browser. The top row always shows navigation buttons (Back, Forward, Reload). Below that, the menu adapts to what you clicked: plain pages get Find in Page and Select All; editable fields get Undo, Redo, Cut, Copy, Paste; links get "Open in new tab" and "Copy link"; images get "Save image" and "Copy image URL". Plus, Reader View dynamically appears if available, and the T&C Lens extension is available right from the menu. Keyboard shortcuts are now visible for all supported actions. Pure builder logic is now strictly unit-tested.

**Bug Fixes**
* **Right-Click Menu**: Fixed a bug where right-clicking on web content showed no Stride menu (and often nothing at all). Reading link/image/selection data from the WebView2 context menu target now only happens when that data actually exists; reading it unconditionally threw a COM error that silently killed every menu.

**Under the Hood**
* Purged the last NetSparkle references from live surfaces: removed the stale "NetSparkleUpdater WPF" row from Settings, reworded updater comments, and deleted the obsolete `UpdateServiceE2ETests` E2E test that targeted the retired appcast pipeline. The test suite compiles and runs again (161 passing).

# Release Note: v1.2.1 Polish & Bug Fixes

**Features**
* **Release Notes Auto-Open**: Stride now correctly opens the `stride://release-notes` page automatically after an update is installed, so you always know what's new.

**Bug Fixes**
* **Downloads 100% Hang**: Fixed a frustrating bug where large downloads would freeze at 100%. This was caused by .NET Garbage Collection dropping native events, combined with Microsoft SmartScreen silently suspending downloads in the background. (SmartScreen reputation checks are now fully disabled by default for privacy and speed).
* **Download UI Commands**: Fixed a bug where clicking Pause, Resume, or Cancel on active downloads did nothing.
* **Local HTML Files**: Fixed an issue where Stride ignored local `.html` files when launched from Windows Explorer or the command line. Stride can now securely open local files and be set as your default offline HTML viewer.
* **New Tab Reloads**: Fixed an issue where adding or removing a shortcut caused the entire New Tab page to flicker and reload. The UI now updates instantly.
* **New Tab Shortcuts**: Fixed a bug where adding a shortcut with special characters (like single quotes or backslashes) or importing bookmarks would completely break the New Tab page. Shortcuts are now securely passed to the frontend using Base64 encoding.
* **Toolbar Icon Toggles**: Fixed a bug where toggling the "Show Reader Icon" setting off would not actually hide the icon from the toolbar. The setting is now correctly wired to instantly update the UI.

# Release Note: v1.2.0 Native Seamless Updater

**Features**
* **Native Seamless Updates**: The update flow has been completely redesigned. Say goodbye to the red Settings dot and UAC prompts. A beautiful, native "gear in a tray" icon now appears directly on your toolbar when an update is available. Clicking it shows a real-time circular progress ring wrapping the icon while it downloads in the background, followed by a green checkmark.
* **Invisible Micro-Updater**: When you click the green checkmark, Stride orchestrates a lightning-fast native restart. It swaps the files behind the scenes using a new invisible Windows GUI executable updater, entirely eliminating wizard dialogs, UAC prompts, and NetSparkle dependencies. 

**Under the Hood**
* Removed all NetSparkle dependencies from the project.
* `UpdateService` now uses `HttpClient` to natively hit the GitHub REST API and download the `.zip` archive.
* Reconfigured the build pipeline (`build-release.ps1`) to output both a `.zip` archive for the micro-updater and a `.exe` installer for new users.
