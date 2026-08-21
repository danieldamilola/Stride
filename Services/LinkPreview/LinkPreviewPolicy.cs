using System.Windows;
using StrideBrowser.Models;
using StrideBrowser.Models.LinkPreview;

namespace StrideBrowser.Services.LinkPreview;

public sealed class LinkPreviewPolicy : ILinkPreviewPolicy
{

    public bool IsPreviewableUrl(string url, string currentTabUrl)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (string.IsNullOrWhiteSpace(url.Trim())) return false;

        var trimmed = url.Trim();

        if (InternalUrls.IsInternal(trimmed)) return false;
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return false;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not "http" and not "https") return false;
        if (string.IsNullOrWhiteSpace(uri.Host)) return false;

        // Reject same-document navigation (including fragment differences)
        if (!string.IsNullOrWhiteSpace(currentTabUrl))
        {
            if (Uri.TryCreate(currentTabUrl.Trim(), UriKind.Absolute, out var current))
            {
                var sameWithoutFragment = Uri.Compare(
                    uri, current,
                    UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
                    UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
                if (sameWithoutFragment)
                {
                    return false;
                }
            }
        }

        // Reject very short or malformed hosts
        if (uri.Host.Length < 3) return false;
        if (uri.Host.Contains(' ')) return false;

        return true;
    }

    public bool ShouldPeek(LinkPreviewRequest request, LinkPreviewOptions options)
    {
        if (options is null) return false;
        if (!options.Enabled) return false;
        if (request is null) return false;
        if (string.IsNullOrWhiteSpace(request.Url)) return false;

        // Trigger allow list
        if (request.Trigger == LinkPreviewTrigger.AltPress && !options.AllowPress) return false;

        // Hotkey must be Alt. We keep parsing flexible for future rebind.
        var hotkey = options.Hotkey?.Trim() ?? "Alt";
        if (!string.Equals(hotkey, "Alt", StringComparison.OrdinalIgnoreCase))
            return false;

        // Anchor rect must be valid
        if (request.AnchorRect.IsEmpty) return false;
        if (double.IsNaN(request.AnchorRect.Width) || double.IsNaN(request.AnchorRect.Height)) return false;
        if (request.AnchorRect.Width < 0 || request.AnchorRect.Height < 0) return false;

        // Tab must be set
        if (request.TabId == Guid.Empty) return false;

        // URL must still be previewable without current context
        if (!IsPreviewableUrl(request.Url, string.Empty)) return false;

        return true;
    }

}
