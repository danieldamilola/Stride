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
    private static int _lastBgIndex = -1;
    private static readonly Random _random = new();

    /// <summary>Returns the new tab page HTML.</summary>
    public string Render(List<ShortcutItem> shortcuts, string accentColor, string accentRgb, string ipcToken, int zoom, string backgroundPath = "")
    {
        var shortcutsJson = JsonSerializer.Serialize(shortcuts);
        var shortcutsB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(shortcutsJson));
        
        string[] backgroundUrls = Array.Empty<string>();
        
        // 1. Check AppData folder for user custom backgrounds
        var userBgFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stride", "Backgrounds");
        if (Directory.Exists(userBgFolder))
        {
            var userFiles = Directory.GetFiles(userBgFolder)
                                 .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                             f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                             f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                             f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                                 .Select(f => "https://user.assets/" + Path.GetFileName(f))
                                 .ToArray();
            if (userFiles.Length > 0)
                backgroundUrls = userFiles;
        }
        
        // 2. Fallback to default backgrounds
        if (backgroundUrls.Length == 0)
        {
            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Pages", "Backgrounds"),
                Path.Combine(AppContext.BaseDirectory, "Resources", "Pages", "Backgrounds"),
                Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Pages", "Backgrounds")
            };

            foreach (var path in searchPaths)
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path)
                                         .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                                     f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                                         .Select(f => "https://local.assets/Backgrounds/" + Path.GetFileName(f))
                                         .ToArray();
                    if (files.Length > 0)
                    {
                        backgroundUrls = files;
                        break;
                    }
                }
            }
        }
        if (backgroundUrls.Length > 0 && string.IsNullOrEmpty(backgroundPath))
        {
            int nextIdx = _random.Next(backgroundUrls.Length);
            if (nextIdx == _lastBgIndex && backgroundUrls.Length > 1)
                nextIdx = (nextIdx + 1) % backgroundUrls.Length;
            _lastBgIndex = nextIdx;
            backgroundPath = backgroundUrls[nextIdx];
        }

        // Defense in depth: encode the background path before it lands in
        // href="" and url(''). The path is built by the app from its own
        // directories, but encoding prevents a future refactor from
        // accidentally introducing a CSS-context injection if a value
        // ever comes from user input.
        var safeBackground = StrideBrowser.Helpers.JsEncoder.HtmlEncode(backgroundPath);

        return Helpers.ResourceLoader.LoadTemplate("Resources.Pages.NewTab.html",
            new Dictionary<string, string>
            {
                ["BACKGROUND"] = safeBackground,
                ["SHORTCUTS"] = shortcutsB64,
                ["ACCENT"] = accentColor,
                ["ACCENT_RGB"] = accentRgb,
                ["IPC_TOKEN"] = ipcToken,
                ["ZOOM"] = zoom.ToString()
            });
    }
}
