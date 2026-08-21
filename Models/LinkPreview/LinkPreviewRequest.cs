using System.Windows;

namespace StrideBrowser.Models.LinkPreview;

/// <summary>Input to policy check. Pure data.</summary>
public sealed record LinkPreviewRequest(
    Guid TabId,
    string Url,
    Rect AnchorRect,
    LinkPreviewTrigger Trigger,
    DateTime SeenAt
);
