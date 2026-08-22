using System.Text;
using System.Collections.Generic;
using StrideBrowser.Helpers;

namespace StrideBrowser.Services.Pages;

public sealed class ReleaseNotesPage
{
    public string Render(string currentVersion, IReadOnlyList<Engine.ReleaseNotesProvider.ReleaseVersion> releases, string accentColor, string accentRgb, string ipcToken)
    {
        string ItemsHtml(IReadOnlyList<(string title, string desc)> items, string tag, string tagCls)
        {
            if (items.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var (t, d) in items)
            {
                sb.Append("<li class=\"changelog-item\">");
                sb.Append("<div class=\"changelog-tag ").Append(tagCls).Append("\">").Append(tag).Append("</div>");
                sb.Append("<div class=\"changelog-content\">");
                sb.Append("<div class=\"changelog-title\">").Append(System.Net.WebUtility.HtmlEncode(t)).Append("</div>");
                if (!string.IsNullOrWhiteSpace(d))
                {
                    sb.Append("<div class=\"changelog-desc\">").Append(System.Net.WebUtility.HtmlEncode(d)).Append("</div>");
                }
                sb.Append("</div></li>");
            }
            return sb.ToString();
        }

        var logsHtml = new StringBuilder();

        foreach (var release in releases)
        {
            logsHtml.Append("<div class=\"release-log\">");
            logsHtml.Append("<div class=\"log-header\">");
            logsHtml.Append("<div class=\"log-version\">v").Append(System.Net.WebUtility.HtmlEncode(release.Version)).Append("</div>");
            logsHtml.Append("<div class=\"log-date\">").Append(System.Net.WebUtility.HtmlEncode(release.Date)).Append("</div>");
            logsHtml.Append("</div>");
            logsHtml.Append("<ul class=\"changelog-list\">");
            logsHtml.Append(ItemsHtml(release.Features, "Added", "feature"));
            logsHtml.Append(ItemsHtml(release.Fixes, "Fixed", "fix"));
            logsHtml.Append("</ul></div>");
        }

        var latest = releases.Count > 0
            ? releases[0]
            : new Engine.ReleaseNotesProvider.ReleaseVersion(
                currentVersion, "", "Thanks for updating",
                "Stride keeps getting lighter and more customizable.",
                new List<(string, string)>(), new List<(string, string)>());

        return ResourceLoader.LoadTemplate("Resources.Pages.ReleaseNotes.html", new Dictionary<string, string>
        {
            ["VERSION"]          = System.Net.WebUtility.HtmlEncode(currentVersion),
            ["DATE"]             = System.Net.WebUtility.HtmlEncode(latest.Date),
            ["HIGHLIGHT_TITLE"]  = System.Net.WebUtility.HtmlEncode(latest.HighlightTitle),
            ["HIGHLIGHT_DESC"]   = System.Net.WebUtility.HtmlEncode(latest.HighlightDesc),
            ["ALL_RELEASES_HTML"] = logsHtml.ToString(),
            ["ACCENT"]           = accentColor,
            ["ACCENT_RGB"]       = accentRgb,
            ["IPC_TOKEN"]        = ipcToken
        });
    }
}
