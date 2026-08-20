using System.Text.Json;
using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the downloads page HTML. Download items are passed as JSON for client-side rendering.</summary>
public sealed class DownloadPage
{
    public string Render(List<DownloadItem> items, string accentColor, string accentRgb, string ipcToken)
    {
        var json = JsonSerializer.Serialize(items, DownloadJson.Options);
        return TemplateRenderer.RenderJsonPage("Resources.Pages.Downloads.html", "DOWNLOADS", json, accentColor, accentRgb, ipcToken);
    }
}
