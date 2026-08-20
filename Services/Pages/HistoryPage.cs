using System.Text.Json;
using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the history page HTML. Entries are passed as JSON for client-side rendering.</summary>
public sealed class HistoryPage
{
    public string Render(List<HistoryEntry> entries, string accentColor, string accentRgb, string ipcToken)
    {
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { PropertyNamingPolicy = null });
        return TemplateRenderer.RenderJsonPage("Resources.Pages.History.html", "ENTRIES", json, accentColor, accentRgb, ipcToken);
    }
}
