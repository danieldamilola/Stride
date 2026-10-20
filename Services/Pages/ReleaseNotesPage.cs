using System.Net;
using System.Text;
using StrideBrowser.Engine;
using StrideBrowser.Helpers;

namespace StrideBrowser.Services.Pages;

public sealed class ReleaseNotesPage
{
    public string Render(string currentVersion, IReadOnlyList<ReleaseNotesProvider.ReleaseVersion> releases, string accentColor, string accentRgb, string ipcToken)
    {
        var logsHtml = new StringBuilder();
        foreach (var release in releases)
        {
            logsHtml.Append("<details class=\"release-log\"");
            if (release == releases[0]) logsHtml.Append(" open");
            logsHtml.Append("><summary><div class=\"log-header\"><div class=\"log-version\">v")
                .Append(WebUtility.HtmlEncode(release.Version))
                .Append("</div><div class=\"log-date\">")
                .Append(WebUtility.HtmlEncode(release.Date))
                .Append("</div></div></summary><ul class=\"changelog-list\">");
            foreach (var section in release.Sections)
            {
                var tagClass = section.Category switch { "Added" => "feature", "Fixed" => "fix", _ => "" };
                foreach (var item in section.Items)
                {
                    logsHtml.Append("<li class=\"changelog-item\"><div class=\"changelog-tag ")
                        .Append(tagClass).Append("\">").Append(WebUtility.HtmlEncode(section.Category))
                        .Append("</div><div class=\"changelog-content\"><div class=\"changelog-title\">")
                        .Append(WebUtility.HtmlEncode(item)).Append("</div></div></li>");
                }
            }
            logsHtml.Append("</ul></details>");
        }
        if (releases.Count == 0)
        {
            logsHtml.Append("<p class=\"sub\">Version ").Append(WebUtility.HtmlEncode(currentVersion))
                .Append(" is in development. No releases have been published yet.</p>");
        }

        return ResourceLoader.LoadTemplate("Resources.Pages.ReleaseNotes.html", new Dictionary<string, string>
        {
            ["VERSION"] = WebUtility.HtmlEncode(currentVersion),
            ["DATE"] = releases.Count > 0 ? WebUtility.HtmlEncode(releases[0].Date) : "Unreleased",
            ["ALL_RELEASES_HTML"] = logsHtml.ToString(),
            ["ACCENT"] = accentColor,
            ["ACCENT_RGB"] = accentRgb,
            ["IPC_TOKEN"] = ipcToken
        });
    }
}
