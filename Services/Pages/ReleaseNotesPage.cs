using System.Globalization;
using System.Net;
using System.Text;
using UmberBrowser.Engine;
using UmberBrowser.Helpers;

namespace UmberBrowser.Services.Pages;

public sealed class ReleaseNotesPage
{
    public string Render(string currentVersion, IReadOnlyList<ReleaseNotesProvider.ReleaseVersion> releases, string ipcToken)
    {
        var content = new StringBuilder();
        var navigation = new StringBuilder();
        for (var index = 0; index < releases.Count; index++)
        {
            var release = releases[index];
            var id = "release-" + index;
            var version = WebUtility.HtmlEncode(release.Version);
            var date = DateTime.TryParseExact(release.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) : release.Date;
            var time = "<time datetime=\"" + WebUtility.HtmlEncode(release.Date) + "\">" + WebUtility.HtmlEncode(date) + "</time>";
            navigation.Append("<button type=\"button\" data-release=\"").Append(id).Append("\">Version ").Append(version).Append("</button>");
            if (index == 0)
                content.Append("<article class=\"release\" id=\"").Append(id).Append("\"><header class=\"release-heading\"><div class=\"release-version\"><h2>Version ")
                    .Append(version).Append("</h2><span class=\"latest-badge\">Latest published</span></div>").Append(time).Append("</header>");
            else
                content.Append("<details class=\"release release-log\" id=\"").Append(id).Append("\"><summary class=\"release-summary\"><span class=\"older-version\">Version ")
                    .Append(version).Append("</span>").Append(time).Append("<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"m6 9 6 6 6-6\"/></svg></summary>");
            content.Append("<div class=\"release-body\">");
            foreach (var section in release.Sections)
            {
                if (section.Items.Count == 0) continue;
                content.Append("<section class=\"change-section\"><h3>").Append(WebUtility.HtmlEncode(section.Category)).Append("</h3><ul>");
                foreach (var item in section.Items) content.Append("<li>").Append(WebUtility.HtmlEncode(item)).Append("</li>");
                content.Append("</ul></section>");
            }
            content.Append("</div>").Append(index == 0 ? "</article>" : "</details>");
        }
        if (releases.Count == 0)
            content.Append("""
                <section class="empty-release"><div class="empty-mark" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M6 3h12v18H6ZM9 8h6m-6 4h6m-6 4h4"/></svg></div><h2>No releases have been published yet.</h2><p>You're using a development build. Published release notes will appear here when they are available.</p></section>
                """);

        return ResourceLoader.LoadTemplate("Resources.Pages.ReleaseNotes.html", new Dictionary<string, string>
        {
            ["VERSION"] = WebUtility.HtmlEncode(currentVersion),
            ["RELEASE_STATUS"] = releases.Count == 0 ? "Unreleased" : "Release history",
            ["RELEASE_NAV_HTML"] = navigation.ToString(),
            ["ALL_RELEASES_HTML"] = content.ToString(),
            ["APP_ICON"] = PageAssets.AppIcon,
            ["IPC_TOKEN"] = ipcToken
        });
    }
}
