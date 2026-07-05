using System.Net;
using System.Text;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the OneTab page HTML showing saved tab groups.</summary>
public sealed class OneTabPage
{
    public string Render(List<OneTabGroup> groups, string accentColor, string accentRgb, string ipcToken)
    {
        var sb = new StringBuilder();
        var search = "";
        // All postMessage calls embed the per-session token so the C# host can
        // reject messages from untrusted pages that don't know the token.
        var tok = ipcToken + ":";

        if (groups.Count == 0)
        {
            sb.Append("<div class='empty'>No saved tabs. Press Ctrl+Shift+S or Ctrl+Shift+1 to save all open tabs.</div>");
        }
        else
        {
            search = "<div class='search-bar'><input id='onetab-search' type='text' placeholder='Search saved tabs...' autocomplete='off' spellcheck='false'></div>";

            for (int gi = 0; gi < groups.Count; gi++)
            {
                var group = groups[gi];
                var safeId = WebUtility.HtmlEncode(group.Id);
                var jsId = JsEncoder.Encode(group.Id);
                var safeName = WebUtility.HtmlEncode(group.Name);
                var timestamp = group.SavedAt.ToLocalTime().ToString("MMM d, h:mm tt");

                sb.Append("<div class='group' data-group-id='" + safeId + "'>");
                sb.Append("<div class='group-header'>");
                sb.Append("<span class='group-name' data-id='" + safeId + "' onclick=\"this.contentEditable='true';this.focus();\" ");
                sb.Append("onblur=\"this.contentEditable='false';window.chrome.webview.postMessage('" + tok + "onetab-rename:" + jsId + ":'+this.textContent);\" ");
                sb.Append("onkeydown=\"if(event.key==='Enter'){event.preventDefault();this.blur()}\">" );
                sb.Append(safeName);
                sb.Append("</span>");
                sb.Append("<span class='group-time'>" + WebUtility.HtmlEncode(timestamp) + "</span>");
                sb.Append("<span class='group-count'>" + group.Tabs.Count + " tabs</span>");
                sb.Append("<button class='btn' onclick=\"if(confirm('Restore all tabs in this group?'))window.chrome.webview.postMessage('" + tok + "onetab-restore:" + jsId + "')\">Restore All</button>");
                sb.Append("<button class='btn btn-danger' onclick=\"if(confirm('Delete this group?'))window.chrome.webview.postMessage('" + tok + "onetab-delete:" + jsId + "')\">Delete</button>");
                sb.Append("</div>");

                sb.Append("<div class='tab-list' data-group-id='" + safeId + "'>");
                for (int ti = 0; ti < group.Tabs.Count; ti++)
                {
                    var tab = group.Tabs[ti];
                    var safeUrl = WebUtility.HtmlEncode(tab.Url);
                    var jsUrl = JsEncoder.Encode(tab.Url);
                    var safeTitle = WebUtility.HtmlEncode(tab.Title);
                    var starClass = tab.IsStarred ? "starred" : "";

                    sb.Append("<div class='tab' data-tab-index='" + ti + "' data-group-id='" + safeId + "'>");
                    sb.Append("<span class='tab-star " + starClass + "' onclick=\"event.stopPropagation();window.chrome.webview.postMessage('" + tok + "onetab-star:" + jsId + ":" + ti + "')\" title='Star this tab'>" + (tab.IsStarred ? "★" : "☆") + "</span>");
                    sb.Append("<a class='tab-title' href='#' onclick=\"event.preventDefault();window.chrome.webview.postMessage('" + tok + "onetab-open:" + jsId + ":" + jsUrl + "');return false;\">" + safeTitle + "</a>");
                    sb.Append("<span class='tab-url'>" + safeUrl + "</span>");
                    sb.Append("<button class='tab-delete' onclick=\"event.stopPropagation();window.chrome.webview.postMessage('" + tok + "onetab-delete-tab:" + jsId + ":" + jsUrl + "')\" title='Remove this tab'>×</button>");
                    sb.Append("</div>");
                }
                sb.Append("</div>");

                sb.Append("</div>");
            }
        }

        return Helpers.ResourceLoader.LoadTemplate("Resources.Pages.OneTab.html",
            new Dictionary<string, string>
            {
                ["CONTENT"] = sb.ToString(),
                ["SEARCH"] = search,
                ["ACCENT"] = accentColor,
                ["ACCENT_RGB"] = accentRgb
            });
    }
}
