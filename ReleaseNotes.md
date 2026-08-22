<div style="background: #ff4444; color: white; padding: 20px; border-radius: 8px; text-align: center; font-family: 'Segoe UI', sans-serif;">
  <h2 style="color: white; margin-top: 0;">?? Critical Manual Update Required ??</h2>
  <p>The auto-updater in version 1.1.3 has a bug. Clicking "Install" below will fail.</p>
  <p>To fix this and get all future updates automatically, you must download the latest setup file manually.</p>
  <a href="https://github.com/danieldamilola/Stride/releases/latest" style="color: #fff; font-weight: bold; text-decoration: underline; font-size: 1.2em;">Click here to download Stride 1.2.0</a>
</div>
<style>
  body {
    font-family: 'Segoe UI', -apple-system, sans-serif;
    background-color: #ffffff;
    color: #333333;
    line-height: 1.6;
    padding: 10px 20px;
  }
  h1 {
    font-size: 22px;
    color: #111111;
    border-bottom: 1px solid #eaeaea;
    padding-bottom: 8px;
    margin-top: 0;
  }
  h2, h3, h4 {
    font-size: 18px;
    color: #444444;
    margin-top: 20px;
  }
  code {
    background-color: #f4f4f4;
    padding: 2px 4px;
    border-radius: 4px;
    font-family: Consolas, monospace;
    font-size: 0.9em;
  }
  ul {
    padding-left: 20px;
  }
  li {
    margin-bottom: 8px;
  }
  strong {
    font-weight: 600;
    color: #222222;
  }
</style>
# Release Note: v1.2.1 Native Updater & Polish

**Features**
* **Native Seamless Updates**: The update flow has been completely redesigned. Say goodbye to the red Settings dot and UAC prompts. A beautiful, native "gear in a tray" icon now appears directly on your toolbar when an update is available. Clicking it shows a real-time circular progress ring wrapping the icon while it downloads in the background, followed by a green checkmark.
* **Invisible Micro-Updater**: When you click the green checkmark, Stride orchestrates a lightning-fast native restart. It swaps the files behind the scenes using a new invisible Console updater, entirely eliminating wizard dialogs, UAC prompts, and NetSparkle dependencies. 
* **Release Notes Auto-Open**: Stride now correctly opens the `stride://release-notes` page automatically after an update is installed, so you always know what's new.

**Bug Fixes**
* **New Tab Shortcuts**: Fixed a bug where adding a shortcut with special characters (like single quotes or backslashes) or importing bookmarks would completely break the New Tab page. Shortcuts are now securely passed to the frontend using Base64 encoding.
* **Toolbar Icon Toggles**: Fixed a bug where toggling the "Show Reader Icon" setting off would not actually hide the icon from the toolbar. The setting is now correctly wired to instantly update the UI.

**Under the Hood**
* Removed all NetSparkle dependencies from the project.
* `UpdateService` now uses `HttpClient` to natively hit the GitHub REST API and download the `.zip` archive.
* Reconfigured the build pipeline (`build-release.ps1`) to output both a `.zip` archive for the micro-updater and a `.exe` installer for new users.
