using StrideBrowser.Models;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the new tab page HTML.</summary>
public sealed class NewTabPage
{
    private static string[]? _backgroundUrls = null;

    /// <summary>Returns the new tab page HTML.</summary>
    public string Render(List<ShortcutItem> shortcuts, string accentColor, string accentRgb, string ipcToken, int zoom, string backgroundPath = "")
    {
        var shortcutsJson = JsonSerializer.Serialize(shortcuts);
        
        if (_backgroundUrls == null)
        {
            var bgFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Pages", "Backgrounds");
            if (Directory.Exists(bgFolder))
            {
                var files = Directory.GetFiles(bgFolder)
                                     .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                                 f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                     .Select(f => "https://local.assets/Backgrounds/" + Path.GetFileName(f))
                                     .ToArray();
                _backgroundUrls = files.Length > 0 ? files : new string[] { };
            }
            else
            {
                _backgroundUrls = new string[] { };
            }
        }
        
        var backgroundsJson = JsonSerializer.Serialize(_backgroundUrls);
        return Helpers.ResourceLoader.LoadTemplate("Resources.Pages.NewTab.html",
            new Dictionary<string, string>
            {
                ["BACKGROUND"] = backgroundPath,
                ["BACKGROUNDS_JSON"] = backgroundsJson,
                ["SHORTCUTS"] = shortcutsJson,
                ["ACCENT"] = accentColor,
                ["ACCENT_RGB"] = accentRgb,
                ["IPC_TOKEN"] = ipcToken,
                ["ZOOM"] = zoom.ToString()
            });
    }
}

