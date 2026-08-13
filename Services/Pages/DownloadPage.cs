using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the downloads page HTML. Download items are passed as JSON for client-side rendering.</summary>
public sealed class DownloadPage
{
    public string Render(List<DownloadItem> items, string accentColor, string accentRgb, string ipcToken)
    {
        return TemplateRenderer.RenderJsonPage("Resources.Pages.Downloads.html", "DOWNLOADS", items, accentColor, accentRgb, ipcToken);
    }
}