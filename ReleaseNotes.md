# Release Note: v1.2.1

**Features**
* **Dynamic Context Menu**: Rebuilt the right-click menu to be context-aware. The top row shows Back, Forward, and Reload buttons. The menu adapts to the clicked target. Plain pages show Find in Page and Select All. Editable fields show Undo, Redo, Cut, Copy, and Paste. Links show "Open in new tab" and "Copy link". Images show "Save image" and "Copy image URL". Reader View appears when available. The T&C Lens extension is accessible from the menu. Keyboard shortcuts display for supported actions.
* **Release Notes Auto-Open**: Stride opens stride://release-notes automatically after an update installs.

**Bug Fixes**
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
