using System.Text.Json;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the downloads page HTML. Download items are passed as JSON for client-side rendering.</summary>
public sealed class DownloadPage
{
    public string Render(List<DownloadItem> items, string accentColor, string accentRgb, string ipcToken)
    {
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null // Keep PascalCase to match JS expectations
        });

        // Escape for safe embedding in JS string literal
        json = json.Replace("\\", "\\\\").Replace("'", "\\'");

        return ResourceLoader.LoadTemplate("Resources.Pages.Downloads.html",
            new Dictionary<string, string>
            {
                ["DOWNLOADS"] = json,
                ["ACCENT"] = accentColor,
                ["ACCENT_RGB"] = accentRgb,
                ["IPC_TOKEN"] = ipcToken
            });
    }
}
