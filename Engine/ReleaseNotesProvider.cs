using System.Collections.Generic;

namespace StrideBrowser.Engine;

public static class ReleaseNotesProvider
{
    public record ReleaseVersion(string Version, string Date, string HighlightTitle, string HighlightDesc, IReadOnlyList<(string title, string desc)> Features, IReadOnlyList<(string title, string desc)> Fixes);

    public static IReadOnlyList<ReleaseVersion> GetAllReleases()
    {
        return new List<ReleaseVersion>
        {
            new("1.2.0", "Aug 22, 2026", "Reader Mode & Link Preview", "Distraction-free reading on any article and Alt-triggered link previews, plus smarter tab resource controls.",
                new List<(string, string)> {
                    ("Reader Mode", "Press Ctrl+Shift+R on an article for a clean, script-free reading view powered by Mozilla Readability"),
                    ("Link Preview", "Alt+click (or Alt+hover) any link to peek at it in an isolated window without leaving your page"),
                    ("Customizable Tab Dim", "New sliders and toggles to dim sleeping and hibernated tabs in Settings > Performance"),
                    ("Pinned Tab Controls", "Optional toggles let pinned tabs sleep or hibernate like normal tabs"),
                    ("What's New Page", "This page. Stride now shows release notes once after every update"),
                    ("Redesigned Settings", "Searchable settings with reorganized sections")
                },
                new List<(string, string)> {
                    ("Tab dim flicker", "Stale suspend callbacks no longer re-dim tabs you just switched to"),
                    ("Sleep setting race", "Disabling tab sleep now reliably clears the sleeping state"),
                    ("Reader extraction hang", "Extraction times out instead of freezing the UI on slow pages"),
                    ("Preview safety", "Link previews validate sizes and suppress stray downloads"),
                    ("Update pipeline", "Installer downloads are Ed25519-verified before they can run")
                }),
            new("1.1.3", "Aug 13, 2026", "Standard Auto-Updater & YouTube Fixes", "Quiet background checks with NetSparkle and Inno Setup, plus YouTube playback fix.",
                new List<(string, string)> { 
                    ("Standard Auto-Updater", "Red dot on Settings, user chooses when to install"), 
                    ("Extension Auto-Updater", "uBlock and T&C Lens update silently on restart"),
                    ("Update Control", "Added a new setting to disable automatic background update checks"),
                    ("Settings & UI Decoupling", "Refactored the core message routing and UI layers"),
                    ("Clean Uninstaller", "Ensured that installing updates will cleanly overwrite the existing installation")
                },
                new List<(string, string)> { 
                    ("YouTube playback", "Removed legacy blocker that broke player") 
                }),
            new("1.1.2", "Aug 7, 2026", "The Pinned Tab Redesign & Bug Fixes", "Sleek pinned tabs, Native Download Manager, and performance improvements.",
                new List<(string, string)> { 
                    ("Pinned Tabs Redesign", "Replaced the static amber dot with a sleek pill-shaped background"),
                    ("Native Download Manager", "Removed IDM bridge in favor of built-in native manager"),
                    ("Custom Start Page", "Added support for custom background images on New Tab"),
                    ("Adaptive Tab Bar", "Tab bar adjusts color based on the website's theme"),
                    ("Ad-blocker Expansion", "Expanded host filter list for streaming sites"),
                    ("Revamped Suggestions", "Redesigned the URL address bar's auto-suggest dropdown")
                },
                new List<(string, string)> { 
                    ("Decoupled App Theme", "Fixed bug where disabling Dark Reader forced Light Mode"),
                    ("Compact Mode Alignment", "Fixed layout issue pushing tabs to the right"),
                    ("Full-Screen Overlay", "Resolved taskbar not hiding in full-screen YouTube"),
                    ("Tab-Switching Lag", "Stabilized background rendering thread"),
                    ("Address Bar Bug", "Fixed race condition when typing quickly")
                }),
            new("1.1.0", "Jul 31, 2026", "Native Theming & UI Update", "OS theme matching and circular progress rings.",
                new List<(string, string)> { 
                    ("Native System Theming", "Browser UI automatically matches OS Light/Dark mode"),
                    ("Decoupled Dark Mode", "Internal theme separated from forced webpage dark mode"),
                    ("Circular Download Progress", "Real-time animated progress ring around downloads")
                },
                new List<(string, string)> { 
                    ("Accurate Download States", "Fixed issue where paused download was marked as failed"),
                    ("WPF Icon Crash", "Fixed crash caused by missing icon paths")
                }),
            new("1.0.1", "Jul 28, 2026", "Redesigned New Tab", "Cleaner New Tab page and background update checker.",
                new List<(string, string)> { 
                    ("Redesigned New Tab", "Removed gradients/clock, moved search bar to top"),
                    ("Local Backgrounds", "Added 4 high-quality local background images"),
                    ("Update Checker", "Automatic background update checker")
                },
                new List<(string, string)> { 
                    ("Keyboard Shortcuts", "Fixed shortcuts not triggering when webpage is in focus")
                }),
            new("1.0.0", "Jul 23, 2026", "Initial Windows Release", "The first public release of Stride Browser.",
                new List<(string, string)> { 
                    ("Native Architecture", "Built on WPF and Edge WebView2 engine"),
                    ("Favicon Pill Tab System", "Inactive tabs compress into sleek icons"),
                    ("Smart Command Bar", "Press Ctrl+L for a floating command interface"),
                    ("Built-in Privacy", "Native network-level ad blocking"),
                    ("Intelligent Resource Management", "Actively hibernates background tabs")
                },
                new List<(string, string)>())
        };
    }
}
