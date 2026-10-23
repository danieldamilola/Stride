# Changelog

All notable changes to Stride are recorded here, newest first.

## [Unreleased]

### Added

- Added a project performance audit with Release measurements, WebView2 page and asset checks, and a repeatable isolated diagnostic tool. The tool modes reproduce the delayed extension activation case and sample native suspension latency.
- Performance settings can allow pinned tabs to follow tab sleep and automatic hibernation separately.

### Changed

- History and Downloads use pages of results. Download updates keep unchanged controls and send only changed visible rows.
- History, download history and saved tabs coalesce ordinary file writes in the background and flush pending writes before the last window closes.
- Saved-tab edits update the current page without reloading it.
- Bundled wallpapers use smaller screen-sized images and lazy picker thumbnails.
- Favicon responses and disk storage have size and retention limits.
- Restored sessions now start with every background tab hibernated, including pinned tabs, regardless of the tab hibernation and pinned tab settings. The saved active tab stays loaded.
- Reorganized Settings into General, Appearance, Tabs, Search, Privacy & Security, Focus, Shortcuts and About. Search has its own page, YouTube Unhook is under Focus, and default browser, hardware acceleration and reset actions are under General.
- Settings search keeps matching controls with their group headings and opens matching detail groups.
- Settings no longer flashes routine saving text when a control changes. Save errors and restart notices remain visible.
- The address bar selects the first suggestion automatically so Enter opens it. Typing and deleting continue to edit the text you entered.
- Online search suggestions are now off by default, matching Helium and Zen. Existing profiles switch to local suggestions once and can opt in again in Settings.
- The online suggestions setting explains that search text goes to DuckDuckGo. History and shortcut suggestions stay available when it is off.

### Fixed

- Favicon cache failures now produce diagnostics while preserving best-effort recovery. Tab-transfer verification uses a separate fixture directory for each run.
- Performance verification checks actual Downloads rendering during extension startup. The tab-transfer tool uses the browser navigation path and current page and message contracts.
- Downloads opens with large retained histories, and unchanged records no longer rebuild the full list on each update.
- A tab waiting for extension startup no longer blocks creation of another tab.
- Background tabs avoid duplicate suspension requests, and downloads protect the tab that owns their native transfer.
- Toolbar color messages skip unchanged colors and stop extraction while adaptive tint is disabled.
- Reader sanitization bounds malformed-input work and prepares article content off the UI thread.
- Recovered four bundled wallpapers damaged by an earlier encoding conversion.
- Download toolbar progress tracks active transfers without rescanning completed history on each progress update.
- Turning off tab sleep resumes sleeping pages in every open window, including suspensions that finish after the setting changes.
- Pinned tabs stay awake when their sleep setting is off. Turning it off wakes pinned tabs that are already sleeping.
- Online suggestions skip address and file path inputs. Turning suggestions off cancels pending requests and discards late responses.

## [0.1.0] - 2026-10-21

### Added

- Appearance settings can show or hide the extensions toolbar icon.

- Extensions toolbar panel with installed extension icons and pinned actions.
- Dedicated Extensions page with search, details, enable controls and removal for custom extensions.
- Developer mode for loading unpacked extension folders and reloading local extensions.
- Downloads now opens a compact toolbar panel with progress and file actions while the current page stays open.
- Added optional history retention and clearing on exit to the roadmap.
- Added a focused performance review with isolated Release measurements and a T3 orchestration prompt.
- The address bar uses a muted sliders button for HTTPS pages. It opens site and connection details, shows a warning for HTTP pages, and closes when the active page changes.

### Changed

- Extensions includes YouTube tools, adaptive toolbar color, Focus and forced dark websites with their existing settings. The toolbar menu uses the same controls and spacing as Downloads.

- Moved extension management out of Settings. YouTube controls now have their own Settings category.
- Built-in pages share heading and search-field sizes. Download and history searches sit beside the header actions, and saved tabs uses the same header layout.

- Removed custom accent colors. Light and dark themes now define button, toggle, focus, selection, link and progress colors. Older saved accents no longer override the theme.

- Light mode uses a neutral gray palette with white surfaces, readable secondary text, clearer controls and matching colors across built-in pages.

- Get started uses a five-step setup with native choices and a review of preferences. What's New groups published updates by release and category in a new layout for light and dark mode.

- Download history groups files by date and uses neutral file links, Windows file icons and compact icon actions. Search filters the history without leaving the page.

- The download popup shows recent downloads with compact icon actions. Full download history stays on the Downloads page.

- Reset the application, updater and installer versions to 0.1.0 for a fresh development baseline.
- Replaced the old release history with this changelog. The in-app release notes now read published entries from the same file.
- The dark theme uses neutral charcoal backgrounds, gray borders and neutral text across the browser frame, address suggestions, link previews and built-in pages.
- Settings no longer shows routine save confirmations. Save errors and restart notices remain visible.

### Fixed

- Extension action panels open beneath their toolbar icon, size to their content and dismiss on outside clicks, Escape or tab changes.

- User-installed download manager extensions are no longer removed by the former bundled-extension cleanup.

- Cancelling a download restart explicitly cancels its pending navigation-completion task without changing an already-started transfer.

- Header search fields show a single border only while focused, without a second outline around the input.

- Settings keeps its page background when accent customization is removed.

- Menus, scrollbars and toolbar fields follow live theme changes. Light mode keeps readable toolbar text when a site uses a dark theme color.

- Setup choices follow the app settings, save the HTTPS preference and report save failures. Removed inactive import controls.

- Light mode keeps new-tab wallpaper colors without a white overlay.

- The Downloads tab uses a neutral selected background instead of the browser accent.

- Download history marks moved or deleted files, and removing a record keeps the downloaded file on disk.

- Download progress reaches the toolbar, speed and time remaining update, failures explain what happened, and completed downloads keep their size and source.
- Live downloads pause and resume through the native operation. Interrupted downloads from a previous run offer Restart and start a real transfer without duplicate history rows.
- Downloads reuses its existing tab. The panel supports keyboard dismissal, and download buttons keep focus during live updates.
- The site information card opens below the button toward the right and stays within the browser window.
- The window's settings handler releases its update subscriptions when the window closes, so closed windows no longer stay reachable through the shared update service.
- Shortcut recording reads physical digit and punctuation keys, so combinations such as Ctrl+Shift+1 can be recorded and rebound.
- Update checks report no available update when the repository has no published releases.
- Context menu separators now use the theme's line colour instead of the WPF default, so they stay visible in both themes.
