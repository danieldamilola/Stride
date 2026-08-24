using System.Collections.Generic;

namespace StrideBrowser.Engine;

public static class ReleaseNotesProvider
{
    public record ReleaseVersion(string Version, string Date, string HighlightTitle, string HighlightDesc, IReadOnlyList<(string title, string desc)> Features, IReadOnlyList<(string title, string desc)> Fixes);

    public static IReadOnlyList<ReleaseVersion> GetAllReleases()
    {
        return new List<ReleaseVersion>
        {
            new("1.2.0", "Aug 24, 2026", "Native Seamless Updater", "Invisible micro updater, polished downloads, and a context aware menu.",
                new List<(string, string)> {
                    ("Native Seamless Updates", "Gear in tray icon with circular progress ring and checkmark to install without UAC or wizards"),
                    ("Invisible Micro-Updater", "Swaps files behind the scenes with a native GUI updater"),
                    ("Release Notes Auto-Open", "stride://release-notes opens once after each update"),
                    ("Dynamic Context Menu", "Context aware menu with navigation row, Find in Page, Edit, Link and Image actions, Reader and T&C Lens"),
                    ("Reader Mode & Link Preview", "Distraction-free reading and Alt triggered link previews")
                },
                new List<(string, string)> {
                    ("Micro-Updater merge crash", "Recursive merge and validation before swapping"),
                    ("Downloads 100 percent hang", "Fixed GC and SmartScreen suspend causing freeze at 100 percent"),
                    ("Download UI commands", "Pause Resume Cancel now work on active downloads"),
                    ("Local HTML files", "Open local html via Explorer or command line securely"),
                    ("New Tab reloads", "Shortcut add or remove no longer flickers"),
                    ("New Tab shortcuts", "Special characters no longer break the page, passed via Base64"),
                    ("Toolbar toggles", "Show Reader Icon now hides correctly"),
                    ("Link Preview and Reader", "Preview window and Reader activation fixed"),
                    ("Right-Click menu", "COM error from unconditional context data reading fixed")
                }),
        };
    }
}
