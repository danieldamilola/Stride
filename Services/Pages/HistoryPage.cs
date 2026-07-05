using System.Text.Json;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the history page HTML. Entries are passed as JSON for client-side rendering.</summary>
public sealed class HistoryPage
{
    public string Render(List<HistoryEntry> entries, string accentColor, string accentRgb, string ipcToken)
    {
        var entriesJson = JsonSerializer.Serialize(entries, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null // Keep PascalCase to match JS expectations
        });

        // Escape single quotes and backslashes for safe embedding in JS string literal
        entriesJson = entriesJson.Replace("\\", "\\\\").Replace("'", "\\'");

        return ResourceLoader.LoadTemplate("Resources.Pages.History.html",
            new Dictionary<string, string>
            {
                ["ENTRIES"] = entriesJson,
                ["ACCENT"] = accentColor,
                ["ACCENT_RGB"] = accentRgb,
                ["IPC_TOKEN"] = ipcToken
            });
    }
}
