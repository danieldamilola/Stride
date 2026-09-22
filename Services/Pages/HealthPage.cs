using System.Text;
using StrideBrowser.Services;

namespace StrideBrowser.Services.Pages;

public sealed class HealthPage
{
    public string Render(string currentVersion, ExtensionManager.ExtensionHealth health, string ipcToken)
    {
        var statusColor = health.HasFolder ? "#22c55e" : "#ef4444";
        var statusText = health.HasFolder ? "Active" : "Missing";
        var zipText = health.HasZip ? "Bundled" : "Not bundled, will download";
        var hashText = health.HasHash ? "Stored" : "Not stored";
        var assetsText = health.AssetsStripped ? "Stripped" : "Not stripped";
        var html = Helpers.ResourceLoader.LoadTemplate("Resources.Pages.Health.html",
            new Dictionary<string, string>
            {
                ["CURRENT_VERSION"] = System.Web.HttpUtility.HtmlEncode(currentVersion),
                ["EXT_VERSION"] = System.Web.HttpUtility.HtmlEncode(health.Version),
                ["EXT_STATUS_COLOR"] = statusColor,
                ["EXT_STATUS_TEXT"] = statusText,
                ["EXT_ZIP_TEXT"] = zipText,
                ["EXT_HASH_TEXT"] = hashText,
                ["EXT_ASSETS_TEXT"] = assetsText,
                ["HAS_FOLDER"] = health.HasFolder ? "true" : "false",
                ["HAS_ZIP"] = health.HasZip ? "true" : "false",
                ["HAS_HASH"] = health.HasHash ? "true" : "false",
                ["ASSETS_STRIPPED"] = health.AssetsStripped ? "true" : "false",
                ["IPC_TOKEN"] = ipcToken,
            });
        if (string.IsNullOrEmpty(html))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<html><body style='font-family:sans-serif;padding:24px'>");
            sb.AppendLine($"<h1>Stride Health</h1>");
            sb.AppendLine($"<p>Version: {System.Web.HttpUtility.HtmlEncode(currentVersion)}</p>");
            sb.AppendLine($"<p>uBlock {System.Web.HttpUtility.HtmlEncode(health.Version)}: <span style='color:{statusColor}'>{statusText}</span></p>");
            sb.AppendLine($"<p>Folder: {health.HasFolder} Zip: {health.HasZip} Hash: {health.HasHash} Assets: {assetsText}</p>");
            sb.AppendLine($"</body></html>");
            return sb.ToString();
        }
        return html;
    }
}
