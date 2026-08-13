using System.Text.Json;
using StrideBrowser.Helpers;

namespace StrideBrowser.Services.Pages;

/// <summary>
/// Renders internal pages that embed a JSON payload for client-side rendering.
/// </summary>
public static class TemplateRenderer
{
    /// <summary>
    /// Serializes <paramref name="items"/> to JSON, escapes it for safe embedding in a JS
    /// string literal, and substitutes it plus the accent/IPC values into the template.
    /// </summary>
    public static string RenderJsonPage(string templateResource, string jsonKey, object items, string accentColor, string accentRgb, string ipcToken)
    {
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null // Keep PascalCase to match JS expectations
        });

        // Escape backslashes and single quotes for safe embedding in JS string literal
        json = json.Replace("\\", "\\\\").Replace("'", "\\'");

        return ResourceLoader.LoadTemplate(templateResource,
            new Dictionary<string, string>
            {
                [jsonKey] = json,
                ["ACCENT"] = accentColor,
                ["ACCENT_RGB"] = accentRgb,
                ["IPC_TOKEN"] = ipcToken
            });
    }
}