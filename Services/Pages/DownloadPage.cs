using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates a small shell. The document requests bounded download pages from the host.</summary>
public sealed class DownloadPage
{
    public string Render(List<DownloadItem> items, string ipcToken)
    {
        return TemplateRenderer.RenderJsonPage("Resources.Pages.Downloads.html", "DOWNLOADS", "[]", ipcToken);
    }
}
