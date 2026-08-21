using System.Windows;
using StrideBrowser.Models.LinkPreview;

namespace StrideBrowser.Services.LinkPreview;

public interface ILinkPreviewService
{
    LinkPreviewState Current { get; }
    event Action<LinkPreviewState>? StateChanged;

    LinkPreviewOptions Options { get; }

    bool RequestPeek(Guid tabId, string url, Rect anchorRect, LinkPreviewTrigger trigger, string currentTabUrl);
    void Dismiss();
    void NotifyPreviewLoaded(string url);
    void UpdateOptions(LinkPreviewOptions options);

    bool IsPreviewVisible { get; }
    Guid? ActiveOriginTabId { get; }
}
