# Roadmap & Future Plans

Stride is a lightweight, privacy-focused Windows desktop browser built on WPF and the Microsoft Edge WebView2 engine. The roadmap below reflects the feature set the project is working toward, grouped by what already exists and what is planned.

## Already built

- **Favicon Pill Tabs** - Unique Stride Signature UI that compresses inactive tabs into clean icons and expands active ones, saving vertical and horizontal screen real estate.
- **Command Bar Navigation** - Press `Ctrl+L` to invoke a floating, intelligent command bar with instant local history and autocomplete.
- **Native Ad & Tracker Blocking** - Built-in network-level blocking capabilities with native support for parsing and applying uBlock Origin rules, enhanced with custom cosmetic filters.
- **Smart Memory Management** - Intelligent tab hibernation and LRU (Least Recently Used) eviction algorithms to keep memory usage strictly bounded, no matter how many tabs you open.
- **Force Dark Mode** - Integrated Dark Reader functionality to seamlessly force dark mode on websites that lack native dark themes.
- **Adaptive Tab Bar** - The browser UI intelligently adapts its color palette based on the active website's theme.
- **Focus Mode** - Distraction-free browsing modes with built-in domain blocklists to keep you on track.
- **YouTube Enhancer** - Native integration to enforce default video quality, playback speed, looping, and distraction removal ("Unhook").
- **Native Download Manager** - Built-in download tracker with real-time speed calculation and ETAs.
- **Custom Start Pages** - Personalize your New Tab page with beautiful, custom background image options.
- **OneTab Consolidation** - Built-in tools for sweeping your open tabs into a single, clean list for later reading.

## Upcoming features

- **Extensions** - Support for Chrome, Firefox, and Safari extensions. A curated set of 20 verified extensions will be included out of the box, with the engine expanding to support the standard Chromium manifest format.
- **Vertical tree tabs** - A tree-style vertical tab layout for power users with ultrawide monitors. This replaces and expands on the existing Vertical Tabs Mode planned in the roadmap.
- **Tab groups** - Visual tab grouping with the ability to collapse/expand groups, keeping many tabs organized without clutter.
- **Reader mode** - A distraction-free reading view that strips away navigation, ads, and sidebars, presenting the article content in a clean, readable format.
- **Mozilla PDF.js** - A built-in PDF renderer based on Mozilla's PDF.js, replacing the WebView2 native PDF view with a feature-rich, performant reader.
- **Translation** - On-page and offline translation support, integrated with a translation service for instant web page translation without leaving the browser.
- **Link preview** - Hover or click to preview the target page in a clean, resizable floating window, keeping you in context without navigating away.
- **Make download standard** - Elevate the download experience to a first-class, persistent UI with sidebar integration, speed and ETA tracking, resume support, and one-click folder selection.
- **Site as app** - Transform any website into a native web app with a Dock/Taskbar icon, its own window, and desktop integration, providing lightning-fast access from the desktop without cluttered bookmarks.
- **Fix TCLens** - Resolve the current T&C Lens integration issues and ensure the AI-powered terms analyzer runs reliably as a built-in feature, with Bring-Your-Own-Key support and zero-background activity.

## Existing items carried forward

- **Cross-Platform Linux Port (Avalonia)** - Exploring bringing the exact Stride experience (and UI parity) to Linux desktop environments via Avalonia UI.
- **Extension Marketplace Integration** - Expanding the internal extension engine to support standard Chromium extension manifest formats.
- **Sync Engine** - Secure, end-to-end encrypted synchronization of history, bookmarks, and settings across multiple devices.
- **Vertical Tabs Mode** - Optional vertical tab layout for power users with ultrawide monitors. This work is being integrated into the broader vertical tree tabs feature above.

## Notes

The roadmap is a living document. Priorities may shift as the project evolves, user feedback is gathered, and dependencies (such as WebView2 runtime updates) are addressed. All third-party components (uBlock Origin, Dark Reader) are documented in THIRD-PARTY-NOTICES.md. The project is MIT licensed and welcomes contributions from the community.
