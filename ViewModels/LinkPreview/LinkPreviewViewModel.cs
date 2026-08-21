using CommunityToolkit.Mvvm.ComponentModel;
using StrideBrowser.Models.LinkPreview;
using StrideBrowser.Services.LinkPreview;
using System.Windows;

namespace StrideBrowser.ViewModels.LinkPreview;

/// <summary>
/// Thin ViewModel that mirrors LinkPreviewService state for binding.
/// No business logic. Controller watches this for Window lifetime.
/// </summary>
public sealed partial class LinkPreviewViewModel : ObservableObject
{
    private readonly ILinkPreviewService _service;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private Rect _anchorRect = Rect.Empty;

    [ObservableProperty]
    private Point _position;

    [ObservableProperty]
    private Size _size = new(640, 480);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private LinkPreviewTrigger _trigger = LinkPreviewTrigger.AltPress;

    [ObservableProperty]
    private Guid _originTabId = Guid.Empty;

    public LinkPreviewViewModel(ILinkPreviewService service)
    {
        _service = service;
        SyncFromService(_service.Current);
        _service.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(LinkPreviewState state)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            d.InvokeAsync(() => SyncFromService(state));
        else
            SyncFromService(state);
    }

    private void SyncFromService(LinkPreviewState state)
    {
        IsVisible = state.IsVisible;
        Url = state.Url;
        AnchorRect = state.AnchorRect;
        Position = state.Position;
        Size = state.Size;
        IsLoading = state.IsLoading;
        Trigger = state.Trigger;
        OriginTabId = state.TabId;
    }

    public void Dismiss() => _service.Dismiss();

    public void NotifyLoaded() => _service.NotifyPreviewLoaded(Url);
}
