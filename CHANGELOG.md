# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **Reopen closed tab survives a restart**: Ctrl+Shift+T could not bring back a tab after the browser was closed. The recently-closed stack lived only in memory, so turning session restore off left no way to recover closed tabs at all. The stack is now saved alongside the session and reloaded at startup, and clearing data on exit clears it too.
- **Tab context menu groups**: Copy tab URL no longer runs into Close Tab, and a pinned tab no longer loses its divider entirely.
- **Menu separator lines were invisible**: Separators rendered as a bare gap because the template bound a property the element does not have. They now paint a themed line that follows the light and dark themes.
- **Settings shows the installed runtime**: The WebView2 entry reported the SDK package version bundled with the app rather than the runtime actually installed, so it showed the same number on every machine. It now reports the real Chromium build.

## [0.1.0] - 2026-10-11

First release under the reset 0.1.0 numbering.

### Added

- **Tab tearing**: Drag a tab away from the strip to open it in its own window, or drop it onto another Stride window's tab strip to dock it there. The tab keeps its loaded page, JavaScript state, unsaved form input, running timers, and navigation history across the move. Tearing the last tab in a window moves that window instead of opening a new one, and a window left empty by a dock closes itself. Tabs also reorder by dragging within the strip.
- **Reader mode**: Press Ctrl+Shift+R on an article for a clean, distraction-free reading view powered by Mozilla Readability. Scripts are disabled while reading, and links clicked inside the reader exit back to the live page. The toolbar button is static and toggles in Settings.
- **Link preview**: Hold Alt and click or hover any link to peek at it in an isolated preview window without leaving the current page. Open it in a new tab or the current tab from the preview toolbar, or press Esc to dismiss.
- **Native seamless updates**: A gear in the tray icon appears when an update is available. Clicking it shows a circular progress ring during a background download, followed by a checkmark to install. Installing swaps files behind the scenes through an invisible Windows GUI updater, with no wizard dialogs, UAC prompts, or NetSparkle dependency.
- **Customizable tab dim**: Performance settings dim sleeping and hibernated tabs, with independent opacity sliders for each.
- **Pinned tab resource controls**: Two toggles let pinned tabs sleep and hibernate like normal tabs. Off by default, so pinned tabs stay fully alive unless opted in.
- **What's New page**: A styled release notes page opens once after each update, using the same design language as onboarding. It never interrupts first-run setup and remembers what has been seen.
- **Redesigned settings**: Settings are reorganized into clear sections with a search bar.
- **First-run onboarding**: A guided welcome flow covers address bar style, privacy defaults, built-in tools, search engine, theme, accent color, and import. It appears only on first launch.
- **Redesigned downloads page**: Borderless cards with subtle backgrounds match the History page. White progress bars replace green, neutral status dots replace colored ones, and completed downloads show only the filename.
- **Redesigned dialogs**: All dialog windows have rounded corners and neutral surface-colored buttons. The bright green accent OK button is gone, dialogs no longer float above all applications, and the offset drop shadow is removed.
- **Dynamic context menu**: The right-click menu is context aware. The top row shows Back, Forward, and Reload. Plain pages show Find in Page and Select All, editable fields show Undo, Redo, Cut, Copy, and Paste, links show Open in new tab and Copy link, and images show Save image and Copy image URL. Reader View appears when available and the T&C Lens extension is reachable from the menu.
- **Updated app icons**: New Stride logo across the app with larger taskbar and Task Manager icons, a refreshed tab fallback favicon, and updated branding in Onboarding and What's New. Includes sizes 16 through 512 and a new multi-resolution ICO.
- **Toolbar auto-scroll**: Opening a new tab scrolls the tab strip to bring it into view when tabs overflow, so a new tab is never hidden behind the address bar.
- **Quick access links**: What's New and Getting Started buttons in the System and About section of Settings.
- **Settings shortcut**: Ctrl+, is documented on the Settings icon tooltip.
- **Taskbar grouping and start icon**: Installer shortcuts carry AppUserModelID Stride to match the running process, so pinned taskbar buttons group with the open window instead of opening a second button. Stride verifies its Start menu and taskbar shortcuts on every launch and stamps a missing AppUserModelID in place.

### Changed

- **Tab card design**: Tabs are unified into standard cards with site favicon, clean title, and hover close buttons. Cards render as detached squarcles with 8px corner radii, 31px height, and equal-share shrinking across the viewport. Titles lose their ellipsis and soften out through a gradient mask so a clipped title ends in a fade. No dots truncation anywhere, including tab titles, URL label, suggestion lists, link preview, drag ghost, and context menu preview.
- **Pinned tab layout**: Pinned tabs render as 32 DIP wide squarcles with centered favicons, reordered only within the pinned range. Unpinned tabs reorder only within the unpinned range. Pinning moves a tab to the end of the pinned group and unpinning returns it to the start of the unpinned group. Pinned tabs stay icon only in every display mode.
- **Tab strip sizing**: The strip no longer grows past the space it is given. Tabs shrink from full card width toward favicon width and only scroll once even favicons stop fitting. Full Names mode shares the strip width across every tab, capped at full card width, and thirty tabs on a 1920 window fit end to end.
- **Close button behaviour**: The close slot stays live at zero opacity so the end of every wide tab closes on click the way Helium does, cross-fading in on hover and staying lit on the active tab. A crowded tab shrunk to a bare favicon only offers close once selected, so a stray hover cannot close a tab you never chose.
- **Address bar**: The bar shows the bare domain instead of the full URL. Focusing reveals the full address for editing or copying, and Enter on the untouched display reloads the page. It starts at 90 DIP, grows to its content, and no longer reserves a long empty box for a short or empty URL. Internal pages name themselves instead of showing blank.
- **Suggestion dropdown**: Visited pages come first, then shortcuts, then web phrases. Local matches render instantly and web results append without rebuilding the list, so the flicker is gone, and turning off search suggestions actually stops the network calls. Rows use a per-type icon plus page title and domain instead of a single clock icon and raw URLs. Arrow keys fill the box with the highlighted item, Up from the top row and Esc restore what you typed, and Enter opens the highlighted target.
- **Toolbar layout**: Toolbar height is 38 DIP with a 28 DIP location bar and 28x28 action buttons. The tab strip takes every DIP the toolbar hands over so cards expand to fill and the plus button sits at the end instead of leaving a dead gap. The new tab button follows the last tab rather than trailing the address bar.
- **Window resize floor**: Minimum window size is now 760 by 480, matching the width where the toolbar collapses its centre dock into a second row.
- **Internal pages design sync**: Onboarding and What's New share the flat, utilitarian layout and styling used across the rest of the browser's internal pages.
- **Release notes auto-open**: Stride opens the What's New page automatically after an update installs.
- **Multi-window scopes**: Windows are created from a scope owned by BrowserWindowManager, so each gets its own TabEngine and settings view model, with process-wide state in BrowserSession.

### Fixed

- **Torn-off tab died with its old window**: WebView2 keeps each control tied to the window it was created in and tears it down when that window closes, so a tab moved to another window went blank as soon as the window it came from shut. The control now follows its tab to the window showing it.
- **Download closes browser**: Download landing pages call window.close once the file starts, and the WebView2 control answered that by closing the whole host window rather than just the tab. Stride now detaches that built-in handler when wiring each tab, so only the tab closes and the download keeps running.
- **Tab drag reorder**: An OLE drag source is never a drop target for itself, so the drop never fired for a same-window reorder and every reorder was silently discarded. Reorder, cross-window dock, and tear-off now run on mouse capture, with the gesture decided on release from the pointer's location.
- **Micro-updater merge crash**: Updates touching existing subdirectories were silently dropped because the updater skipped directories that already existed. It now merges directories recursively and validates the package before swapping.
- **Update pipeline hardening**: Installer downloads are Ed25519 signature verified against the appcast before anything executes. The micro updater validates zip integrity, handles pre-release tags, and uses ArgumentList with retries. Downloaded update packages are verified without locking errors, and the updater safely ignores external or elevated ghost processes.
- **Tab strip overflow**: Newly opened tabs were hidden when the toolbar overflowed. The strip now ensures the newly created tab is visible without affecting manual scroll position when viewing older tabs.
- **Security and privacy**: Resolved findings from architecture and codebase security audits, including hardening the named pipe ACL, scoping IPC tokens correctly, and cleaning up privacy data on exit.
- **YouTube tools**: Script configurations are strictly bound to the window object to prevent closure bugs on reload, and invalid playback speeds revert safely to 1.0x.
- **Fullscreen modes**: The application aborts entering fullscreen if the monitor positioning call fails, falling back to a maximized state, and F11 synchronizes correctly with video fullscreen.
- **Tab dim flicker**: Stale suspend callbacks could mark a just-switched-to tab as sleeping, re-dimming it during restoration or crash recovery. Suspension generations now invalidate outdated callbacks.
- **Sleep setting race**: Disabling Tab Sleep now reliably clears the sleeping state instead of racing with in-flight suspends.
- **Reader extraction hang**: Extraction times out instead of freezing the UI when a page is hibernated or unresponsive.
- **Preview safety**: Link previews validate window dimensions and suppress stray downloads triggered inside the peek window. Alt+Click now displays the preview window instead of only dimming, and the Reader Mode button activates correctly.
- **New tab page**: Background images from user or local folders safely URL-encode their path components.
- **WebView teardown**: One failed unsubscribe from a dead page used to abort the rest of the teardown and take the browser process down with it. Each removal is now guarded on its own.

### Known issues

- **Dragged tab card slides within the strip**: Reorder feedback translates the card horizontally only, so pulling a tab toward the window edge does not draw it out past the strip. The card follows the cursor along the strip rather than floating freely.
- **Torn-off composition tabs reload**: With the floating command bar on, WebView2 builds a torn-off tab's page surface against the window it started in and cannot move it. That tab reloads when the window it was torn off from closes instead of going blank.
- **Window cannot be resized**: `WindowChromeManager` intercepts WM_GETMINMAXINFO, marks it handled, and leaves the minimum and maximum track sizes unset, so Windows clamps every manual resize drag to the current bounds. The duplicate `WindowChromeHelper` hook has been removed as dead code; the remaining fix is outstanding.
- **Drag card opacity unverified**: The tab card border is now painted with an opaque brush during a drag, because the item template's IsSelected trigger otherwise forces a translucent wash over the card. This was confirmed by a pixel sample but not yet by a manual drag.