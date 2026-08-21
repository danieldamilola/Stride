using System.Windows;

namespace StrideBrowser.Models.LinkPreview;

/// <summary>Observable peek state. Owned by LinkPreviewService. ViewModel mirrors it.</summary>
public sealed record LinkPreviewState(
    bool IsVisible,
    string Url,
    Rect AnchorRect,
    Point Position,
    Size Size,
    bool IsLoading,
    LinkPreviewTrigger Trigger,
    Guid TabId
)
{
    public static LinkPreviewState Hidden => new(
        IsVisible: false,
        Url: string.Empty,
        AnchorRect: Rect.Empty,
        Position: new Point(0, 0),
        Size: new Size(640, 480),
        IsLoading: false,
        Trigger: LinkPreviewTrigger.AltPress,
        TabId: Guid.Empty
    );
}
