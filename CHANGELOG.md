# Changelog

All notable changes to Stride are recorded here, newest first.
Versioning restarts at 0.1.0. This version is in development and has not been published.

## [Unreleased]

### Added

- Downloads now opens a compact toolbar panel with progress and file actions while the current page stays open.
- Added optional history retention and clearing on exit to the roadmap.
- Added a focused performance review with isolated Release measurements and a T3 orchestration prompt.
- The address bar uses a muted sliders button for HTTPS pages. It opens site and connection details, shows a warning for HTTP pages, and closes when the active page changes.

### Changed

- Light mode uses a neutral gray palette with white surfaces, readable secondary text, clearer controls and matching colors across built-in pages.

- Get started uses a five-step setup with native choices and a review of preferences. What's New groups published updates by release and category in a new layout for light and dark mode.

- Download history groups files by date and uses neutral file links, Windows file icons and compact icon actions. Search filters the history without leaving the page.

- The download popup shows recent downloads with compact icon actions. Full download history stays on the Downloads page.

- Reset the application, updater and installer versions to 0.1.0 for a fresh development baseline.
- Replaced the old release history with this changelog. The in-app release notes now read published entries from the same file.
- The dark theme uses neutral charcoal backgrounds, gray borders and neutral text across the browser frame, address suggestions, link previews and built-in pages.
- Settings no longer shows routine save confirmations. Save errors and restart notices remain visible.

### Fixed

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
