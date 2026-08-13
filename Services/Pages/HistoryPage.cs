using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the history page HTML. Entries are passed as JSON for client-side rendering.</summary>
public sealed class HistoryPage
{
    public string Render(List<HistoryEntry> entries, string accentColor, string accentRgb, string ipcToken)
    {
        return TemplateRenderer.RenderJsonPage("Resources.Pages.History.html", "ENTRIES", entries, accentColor, accentRgb, ipcToken);
    }
}