using System.Text.Json;
using UmberBrowser.Models;

namespace UmberBrowser.Services.Pages;

/// <summary>Generates the history page HTML. Entries are passed as JSON for client-side rendering.</summary>
public sealed class HistoryPage
{
    public string Render(List<HistoryEntry> entries, string ipcToken, bool faviconProxyEnabled = true)
    {
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { PropertyNamingPolicy = null });
        return TemplateRenderer.RenderJsonPage("Resources.Pages.History.html", "ENTRIES", json, ipcToken).Replace("{{FAVICON_PROXY}}", faviconProxyEnabled ? "true" : "false");
    }
}
