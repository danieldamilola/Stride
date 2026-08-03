using StrideBrowser.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the new tab page HTML.</summary>
public sealed class NewTabPage
{
    /// <summary>Returns the new tab page HTML.</summary>
    public string Render(List<ShortcutItem> shortcuts, string accentColor, string accentRgb, string ipcToken, int zoom, string backgroundPath = "")
    {
        var shortcutsJson = JsonSerializer.Serialize(shortcuts);
        
        string[] backgroundUrls = Array.Empty<string>();
        
        // 1. Check AppData folder for user custom backgrounds
        var userBgFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stride", "Backgrounds");
        if (Directory.Exists(userBgFolder))
        {
            var userFiles = Directory.GetFiles(userBgFolder)
                                 .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                             f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                 .Select(f => "https://user.assets/" + Path.GetFileName(f))
                                 .ToArray();
            if (userFiles.Length > 0)
                backgroundUrls = userFiles;
        }
        
        // 2. Fallback to default backgrounds
        if (backgroundUrls.Length == 0)
        {
            var defaultBgFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Pages", "Backgrounds");
            if (Directory.Exists(defaultBgFolder))
            {
                backgroundUrls = Directory.GetFiles(defaultBgFolder)
                                     .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                                 f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                     .Select(f => "https://local.assets/Backgrounds/" + Path.GetFileName(f))
                                     .ToArray();
            }
        }
        
        var backgroundsJson = JsonSerializer.Serialize(backgroundUrls);
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
