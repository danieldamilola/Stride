using System.Net;
using System.Text;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the history page HTML with browsing entries grouped by date.</summary>
public sealed class HistoryPage
{
    public string Render(List<HistoryEntry> entries)
    {
        var sb = new StringBuilder();

        var grouped = entries
            .OrderByDescending(e => e.VisitedAt)
            .GroupBy(e =>
            {
                var local = e.VisitedAt.ToLocalTime().Date;
                var today = DateTime.Now.Date;
                if (local == today) return "Today";
                if (local == today.AddDays(-1)) return "Yesterday";
                return local.ToString("MMMM d, yyyy");
            });

        foreach (var group in grouped)
        {
            sb.Append("<div class='group'>");
            sb.Append("<div class='group-header'><span>" + WebUtility.HtmlEncode(group.Key) + "</span></div>");
            foreach (var entry in group)
            {
                var safeUrl = WebUtility.HtmlEncode(entry.Url);
                var safeTitle = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title);
                var jsUrl = JsEncoder.Encode(entry.Url);
                var time = entry.VisitedAt.ToLocalTime().ToString("h:mm tt");
                sb.Append("<div class='tab history-entry'>");
                sb.Append("<div class='tab-main'>");
                sb.Append("<a class='tab-title' href='#' onclick=\"event.preventDefault();window.chrome.webview.postMessage('history-open:" + jsUrl + "');return false;\">" + safeTitle + "</a>");
                sb.Append("<span class='tab-url'>" + safeUrl + "</span>");
                sb.Append("</div>");
                sb.Append("<span class='entry-time'>" + WebUtility.HtmlEncode(time) + "</span>");
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }

        if (entries.Count == 0)
            sb.Append("<div class='empty'>No browsing history yet.</div>");

        return Helpers.ResourceLoader.LoadTemplate("Resources.Pages.History.html",
            new Dictionary<string, string> { ["CONTENT"] = sb.ToString() });
    }
}
