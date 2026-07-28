using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the new tab page HTML.</summary>
public sealed class NewTabPage
{
    /// <summary>Returns the new tab page HTML.</summary>
    public string Render(List<ShortcutItem> shortcuts, string accentColor, string accentRgb, string ipcToken, int zoom, string backgroundPath = "")
    {
        var shortcutsJson = System.Text.Json.JsonSerializer.Serialize(shortcuts);
        return Helpers.ResourceLoader.LoadTemplate("Resources.Pages.NewTab.html",
            new Dictionary<string, string>
            {
                ["BACKGROUND"] = backgroundPath,
                ["SHORTCUTS"] = shortcutsJson,
                ["ACCENT"] = accentColor,
                ["ACCENT_RGB"] = accentRgb,
                ["IPC_TOKEN"] = ipcToken,
                ["ZOOM"] = zoom.ToString()
            });
    }
}

