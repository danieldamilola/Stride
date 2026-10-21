using StrideBrowser.Models;
using StrideBrowser.Helpers;
using StrideBrowser.Services.Pages;

namespace StrideBrowser.Engine;

public sealed partial class TabEngine
{
    public async Task OpenExtensionsAsync()
    {
        var tab = Tabs.FirstOrDefault(t => t.Url == InternalUrls.Extensions) ?? CreateTab(InternalUrls.Extensions);
        SwitchTo(tab);
        await ActivateAsync(tab);
    }
    private void NavigateToExtensions(BrowserTab tab)
    {
        var html = TemplateRenderer.RenderJsonPage("Resources.Pages.Extensions.html", "EXTENSIONS_DATA",
            "{\"developerMode\":false,\"extensions\":[]}", IpcToken)
            .Replace("{{EXTENSIONS_SCRIPT}}", ResourceLoader.Load("Resources.Scripts.extensions-page.js"));
        html = html.Replace("{{THEME}}", _themeManager.GetThemeString());
        NavigateToHtmlString(tab, html, "extensions");
    }
    public void PostMessageToExtensionsTabs(string message)
    {
        foreach (var tab in Tabs.Where(t => t.Url == InternalUrls.Extensions))
            if (TryGetCore(tab.Id, out var core) && tab.ActiveInternalDocument?.PageKind == InternalPageKind.Extensions)
                try { core!.PostWebMessageAsString(IpcToken + ":" + message); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }
}
