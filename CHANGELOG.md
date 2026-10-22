# Changelog

All notable changes to Stride are recorded here, newest first.

## [Unreleased]

### Added

- Performance settings can allow pinned tabs to follow tab sleep and automatic hibernation separately.

### Changed

- Restored sessions now start with every background tab hibernated, including pinned tabs, regardless of the tab hibernation and pinned tab settings. The saved active tab stays loaded.
- The address bar selects the first suggestion automatically so Enter opens it. Typing and deleting continue to edit the text you entered.
- Online search suggestions are now off by default, matching Helium and Zen. Existing profiles switch to local suggestions once and can opt in again in Settings.
- The online suggestions setting explains that search text goes to DuckDuckGo. History and shortcut suggestions stay available when it is off.

### Fixed

- Turning off tab sleep resumes sleeping pages in every open window, including suspensions that finish after the setting changes.
- Pinned tabs stay awake when their sleep setting is off. Turning it off wakes pinned tabs that are already sleeping.
- Online suggestions skip address and file path inputs. Turning suggestions off cancels pending requests and discards late responses.

## [0.1.0] - 2026-10-21

### Added

- Added optional history retention and clearing on exit to the roadmap.
- Added a focused performance review with isolated Release measurements and a T3 orchestration prompt.
- The address bar uses a muted sliders button for HTTPS pages. It opens site and connection details, shows a warning for HTTP pages, and closes when the active page changes.

### Changed

- Reset the application, updater and installer versions to 0.1.0 for a fresh development baseline.
- Replaced the old release history with this changelog. The in-app release notes now read published entries from the same file.
- The dark theme uses neutral charcoal backgrounds, gray borders and neutral text across the browser frame, address suggestions, link previews and built-in pages.
- Settings no longer shows routine save confirmations. Save errors and restart notices remain visible.

### Fixed

- The site information card opens below the button toward the right and stays within the browser window.
- The window's settings handler releases its update subscriptions when the window closes, so closed windows no longer stay reachable through the shared update service.
- Shortcut recording reads physical digit and punctuation keys, so combinations such as Ctrl+Shift+1 can be recorded and rebound.
- Update checks report no available update when the repository has no published releases.
- Context menu separators now use the theme's line colour instead of the WPF default, so they stay visible in both themes.
