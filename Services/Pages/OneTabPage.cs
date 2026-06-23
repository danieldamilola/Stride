using System.Net;
using System.Text;
using SpurBrowser.Helpers;
using SpurBrowser.Models;

namespace SpurBrowser.Services.Pages;

/// <summary>Generates the OneTab page HTML showing saved tab groups.</summary>
public sealed class OneTabPage
{
    public string Render(List<OneTabGroup> groups)
    {
        var sb = new StringBuilder();

        if (groups.Count == 0)
        {
            sb.Append("<div class='empty'>No saved tabs. Press Ctrl+Shift+S to save all open tabs.</div>");
        }
        else
        {
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var group = groups[gi];
                var safeId = WebUtility.HtmlEncode(group.Id);
                var jsId = JsEncoder.Encode(group.Id);
                var safeName = WebUtility.HtmlEncode(group.Name);
                var timestamp = group.SavedAt.ToLocalTime().ToString("MMM d, h:mm tt");

                sb.Append("<div class='group' draggable='true' data-group-index='" + gi + "' data-group-id='" + safeId + "'>");
                sb.Append("<div class='group-header'>");
                sb.Append("<span class='drag-handle group-drag' title='Drag to reorder'>⠿</span>");
                sb.Append("<span class='group-name' data-id='" + safeId + "' onclick=\"this.contentEditable='true';this.focus();\" ");
                sb.Append("onblur=\"this.contentEditable='false';window.chrome.webview.postMessage('onetab-rename:" + jsId + ":'+this.textContent);\" ");
                sb.Append("onkeydown=\"if(event.key==='Enter'){event.preventDefault();this.blur();}\">" );
                sb.Append(safeName);
                sb.Append("</span>");
                sb.Append("<span class='group-time'>" + WebUtility.HtmlEncode(timestamp) + "</span>");
                sb.Append("<span class='group-count'>" + group.Tabs.Count + " tabs</span>");
                sb.Append("<button class='btn' onclick=\"window.chrome.webview.postMessage('onetab-restore:" + jsId + "')\">Restore All</button>");
                sb.Append("<button class='btn btn-danger' onclick=\"if(confirm('Delete this group?'))window.chrome.webview.postMessage('onetab-delete:" + jsId + "')\">Delete</button>");
                sb.Append("</div>");

                sb.Append("<div class='tab-list' data-group-id='" + safeId + "'>");
                for (int ti = 0; ti < group.Tabs.Count; ti++)
                {
                    var tab = group.Tabs[ti];
                    var safeUrl = WebUtility.HtmlEncode(tab.Url);
                    var jsUrl = JsEncoder.Encode(tab.Url);
                    var safeTitle = WebUtility.HtmlEncode(tab.Title);
                    var starClass = tab.IsStarred ? "starred" : "";

                    sb.Append("<div class='tab' draggable='true' data-tab-index='" + ti + "' data-group-id='" + safeId + "'>");
                    sb.Append("<span class='tab-star " + starClass + "' onclick=\"event.stopPropagation();window.chrome.webview.postMessage('onetab-star:" + jsId + ":" + ti + "')\" title='Star this tab'>" + (tab.IsStarred ? "★" : "☆") + "</span>");
                    sb.Append("<a class='tab-title' href='#' onclick=\"event.preventDefault();window.chrome.webview.postMessage('onetab-open:" + jsId + ":" + jsUrl + "');return false;\">" + safeTitle + "</a>");
                    sb.Append("<span class='tab-url'>" + safeUrl + "</span>");
                    sb.Append("<button class='tab-delete' onclick=\"event.stopPropagation();window.chrome.webview.postMessage('onetab-delete-tab:" + jsId + ":" + jsUrl + "')\" title='Remove this tab'>×</button>");
                    sb.Append("</div>");
                }
                sb.Append("</div>");

                sb.Append("</div>");
            }
        }

        return Helpers.ResourceLoader.LoadTemplate("Resources.Pages.OneTab.html",
            new Dictionary<string, string> { ["CONTENT"] = sb.ToString() });
    }
}
