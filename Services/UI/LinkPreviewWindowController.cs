using System.Diagnostics;
using System.Windows;
using StrideBrowser.Controls;
using StrideBrowser.Engine;
using StrideBrowser.ViewModels.LinkPreview;

namespace StrideBrowser.Services.UI;

public sealed class LinkPreviewWindowController
{
    private readonly LinkPreviewViewModel _viewModel;
    private readonly Services.LinkPreview.ILinkPreviewService _service;
    private readonly TabEngine _engine;
    private readonly Models.BrowserSettings _settings;
    private Window? _owner;
    private LinkPreviewWindow? _window;

    public LinkPreviewWindowController(LinkPreviewViewModel viewModel, Services.LinkPreview.ILinkPreviewService service, TabEngine engine, Models.BrowserSettings settings)
    {
        _viewModel = viewModel;
        _service = service;
        _engine = engine;
        _settings = settings;
        _viewModel.PropertyChanged += OnViewModelChanged;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    public void Attach(Window owner)
    {
        if (_owner != null)
        {
            _owner.SizeChanged -= OnOwnerBoundsChanged;
            _owner.LocationChanged -= OnOwnerBoundsChanged;
        }

        _owner = owner;
        _owner.SizeChanged += OnOwnerBoundsChanged;
        _owner.LocationChanged += OnOwnerBoundsChanged;
        _engine.ActiveTabChanged += OnActiveTabChanged;
        _engine.TabClosed += OnTabClosed;
        if (_viewModel.IsVisible)
            Show();
    }

    private void OnOwnerBoundsChanged(object? sender, EventArgs e)
    {
        if (_window is { IsVisible: true })
        {
            UpdateWindowBounds();
        }
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Models.BrowserSettings.DefaultZoom))
        {
            _window?.SetZoom(_settings.DefaultZoom / 100.0);
        }
    }

    private void OnActiveTabChanged(Models.BrowserTab? tab)
    {
        if (!_viewModel.IsVisible) return;
        if (tab is null) { _service.Dismiss(); return; }
        if (tab.Id != _viewModel.OriginTabId)
            _service.Dismiss();
    }

    private void OnTabClosed(Guid tabId)
    {
        if (_viewModel.IsVisible && tabId == _viewModel.OriginTabId)
            _service.Dismiss();
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LinkPreviewViewModel.IsVisible))
        {
            if (_owner is null) return;
            _owner.Dispatcher.InvokeAsync(() =>
            {
                if (_viewModel.IsVisible)
                    Show();
                else
                    Hide();
            });
        }
    }

    private void Show()
    {
        if (_owner is null) return;
        if (_window is null)
        {
            _window = new LinkPreviewWindow();
            _window.Owner = _owner;
            _window.OpenInNewTabRequested += OnOpenNewTab;
            _window.OpenInCurrentTabRequested += OnOpenCurrentTab;
            _window.CloseRequested += OnCloseRequested;
            _window.PreviewCloseKey += OnCloseRequested;
            _window.PreviewLoaded += url => _service.NotifyPreviewLoaded(url);
            _window.Closed += (_, _) => _window = null;
        }

        var env = _engine.WebViewEnvironment;
        if (env is not null)
            _window.SetEnvironment(env);

        UpdateWindowBounds();
        _window.SetZoom(_settings.DefaultZoom / 100.0);

        if (!_window.IsVisible)
            _window.Show();
        else
            _window.Activate();

        if (!string.IsNullOrWhiteSpace(_viewModel.Url))
        {
            _ = _window.NavigateToAsync(_viewModel.Url);
        }
    }

    private void UpdateWindowBounds()
    {
        if (_window is null) return;
        var size = GetDynamicSize();
        var pos = GetCenteredPosition(size);
        _window.Left = pos.X;
        _window.Top = pos.Y;
        _window.Width = size.Width;
        _window.Height = size.Height;
    }

    private Size GetDynamicSize()
    {
        if (_owner is null) return _viewModel.Size;
        var ow = _owner.ActualWidth;
        var oh = _owner.ActualHeight;
        if (ow < 100 || oh < 100)
        {
            ow = 1280;
            oh = 800;
        }
        var w = Math.Max(640, Math.Min(ow * 0.85, ow - 40));
        var h = Math.Max(480, Math.Min(oh * 0.88, oh - 40));
        return new Size(Math.Round(w), Math.Round(h));
    }

    private Point GetCenteredPosition(Size size)
    {
        if (_owner is null) return _viewModel.Position;

        try
        {
            var screenPoint = _owner.PointToScreen(new Point(0, 0));
            var source = PresentationSource.FromVisual(_owner);
            double dpiX = 1.0, dpiY = 1.0;
            if (source?.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }

            double ownerLeftDips = screenPoint.X / (dpiX > 0 ? dpiX : 1.0);
            double ownerTopDips = screenPoint.Y / (dpiY > 0 ? dpiY : 1.0);

            double left = ownerLeftDips + (_owner.ActualWidth - size.Width) / 2.0;
            double top = ownerTopDips + (_owner.ActualHeight - size.Height) / 2.0;

            return new Point(Math.Round(left), Math.Round(top));
        }
        catch
        {
            double left = _owner.Left + (_owner.ActualWidth - size.Width) / 2.0;
            double top = _owner.Top + (_owner.ActualHeight - size.Height) / 2.0;
            return new Point(Math.Round(left), Math.Round(top));
        }
    }

    private void Hide()
    {
        if (_window is null) return;
        try { _window.CleanupWebView(); } catch (Exception ex) { Trace.WriteLine(ex); }
        _window.Hide();
    }

    private void OnOpenNewTab()
    {
        var url = _viewModel.Url;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var tab = _engine.CreateTab(url);
            _engine.SwitchTo(tab);
            _ = _engine.ActivateAsync(tab);
        }
        catch (Exception ex) { Trace.WriteLine($"LinkPreview open new tab failed: {ex.Message}"); }
        _service.Dismiss();
    }

    private void OnOpenCurrentTab()
    {
        var url = _viewModel.Url;
        if (string.IsNullOrWhiteSpace(url)) return;
        var active = _engine.ActiveTab;
        if (active is null) return;
        try
        {
            active.Url = url;
            _engine.Navigate(active, url);
        }
        catch (Exception ex) { Trace.WriteLine($"LinkPreview open current failed: {ex.Message}"); }
        _service.Dismiss();
    }

    private void OnCloseRequested()
    {
        _service.Dismiss();
    }
}
