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
* **Invisible Micro-Updater**: When you click the green checkmark, Stride orchestrates a lightning-fast native restart. It swaps the files behind the scenes using a new invisible Console updater, entirely eliminating wizard dialogs, UAC prompts, and NetSparkle dependencies. 

**Under the Hood**
* Removed all NetSparkle dependencies from the project.
* `UpdateService` now uses `HttpClient` to natively hit the GitHub REST API and download the `.zip` archive.
* Reconfigured the build pipeline (`build-release.ps1`) to output both a `.zip` archive for the micro-updater and a `.exe` installer for new users.
