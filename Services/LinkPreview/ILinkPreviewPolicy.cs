using StrideBrowser.Models.LinkPreview;

namespace StrideBrowser.Services.LinkPreview;

/// <summary>Pure policy. No WPF, no WebView2. Testable without UI.</summary>
public interface ILinkPreviewPolicy
{
    bool IsPreviewableUrl(string url, string currentTabUrl);
    bool ShouldPeek(LinkPreviewRequest request, LinkPreviewOptions options);
}
