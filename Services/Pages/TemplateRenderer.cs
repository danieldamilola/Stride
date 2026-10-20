using StrideBrowser.Helpers;

namespace StrideBrowser.Services.Pages;

/// <summary>
/// Renders internal pages that embed a JSON payload for client-side rendering.
/// The JSON is injected into a <c>script type="application/json"</c> block, so
/// only the <c>&lt;/script&gt;</c> terminator needs escaping. No backslash or
/// quote escaping is required; the page reads the block with JSON.parse.
/// </summary>
public static class TemplateRenderer
{
    /// <summary>
    /// Escapes <paramref name="json"/> for embedding in a JSON script block and
    /// substitutes it plus the accent/IPC values into the template.
    /// Callers serialize with their own options and pass the raw JSON.
    /// </summary>
    public static string RenderJsonPage(string templateResource, string jsonKey, string json, string accentColor, string accentRgb, string ipcToken)
    {
        // Replacing "</" with "<\/" keeps a "script" sequence inside the data
        // from closing the block early. "\/" is valid JSON and JSON.parse
        // decodes it back to "/".
        var safe = json.Replace("</", "<\\/");

        return ResourceLoader.LoadTemplate(templateResource,
            new Dictionary<string, string>
            {
                [jsonKey] = safe,
                // ACCENT lands in a raw CSS custom property on every page that uses this
                // renderer. SettingsStore normalises it to a hex literal on load; encoding
                // here keeps the sink safe for any other caller.
                ["ACCENT"] = JsEncoder.HtmlEncode(accentColor),
                ["ACCENT_RGB"] = accentRgb,
                ["IPC_TOKEN"] = ipcToken
            });
    }
}
