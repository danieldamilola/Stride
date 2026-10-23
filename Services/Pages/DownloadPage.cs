using System.Text.Json;
using UmberBrowser.Models;

namespace UmberBrowser.Services.Pages;

/// <summary>Generates the downloads page HTML. Download items are passed as JSON for client-side rendering.</summary>
public sealed class DownloadPage
{
    public string Render(List<DownloadItem> items, string ipcToken)
    {
        var json = DownloadJson.SerializeForPage(items);
        return TemplateRenderer.RenderJsonPage("Resources.Pages.Downloads.html", "DOWNLOADS", json, ipcToken);
    }
}
