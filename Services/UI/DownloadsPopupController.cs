using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using StrideBrowser.Controls;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.ViewModels;

namespace StrideBrowser.Services.UI;

public sealed class DownloadsPopupController : IDisposable
{
    private readonly Window _window;
    private bool _disposed;
    private readonly Button _button;
    private readonly FrameworkElement _fallback;
    private readonly Popup _popup;
    private readonly DownloadsPanel _panel;
    private readonly TabEngine _engine;
    private readonly Action _closeSuggestions;
    private readonly DownloadsPanelViewModel _vm;
    private readonly FrameworkElement? _statusIndicator;
    private readonly HashSet<DownloadItem> _ownItems = new();
    public DownloadsPopupController(Window window, Button button, FrameworkElement fallback, Popup popup,
        DownloadsPanel panel, TabEngine engine, IDownloadStore store, Action closeSuggestions, FrameworkElement? statusIndicator = null)
    {
        _window = window; _button = button; _fallback = fallback; _popup = popup; _panel = panel;
        _engine = engine; _closeSuggestions = closeSuggestions; _statusIndicator = statusIndicator;
        _vm = new DownloadsPanelViewModel(store, engine); _panel.DataContext = _vm;
        _popup.CustomPopupPlacementCallback = Place;
        _button.Click += Toggle;
        _panel.CloseButton.Click += CloseClicked;
        _panel.ViewAllButton.Click += ViewAll;
        _popup.PreviewKeyDown += OnKey;
        _engine.Downloads.DownloadStarted += Started;
        _engine.ActiveTabChanged += ActiveTabChanged;
        _window.Deactivated += Deactivated;
        _window.SizeChanged += Resized;
    }
    private void Toggle(object sender, RoutedEventArgs e) { if (_popup.IsOpen) _popup.IsOpen = false; else Open(true); }
    private void Open(bool focus)
    {
        if (!_window.IsVisible) return;
        _closeSuggestions();
        _vm.Refresh();
        _panel.Width = Math.Min(360, Math.Max(220, _window.ActualWidth - 24));
        var target = _button.IsVisible ? _button : _fallback;
        _popup.PlacementTarget = target;
        var availableHeight = _window.ActualHeight - target.TranslatePoint(new Point(0, target.ActualHeight), _window).Y - 16;
        _panel.DownloadsScroll.MaxHeight = Math.Clamp(availableHeight - 90, 80, 320);
        _popup.IsOpen = true;
        if (_statusIndicator is not null) _statusIndicator.Visibility = Visibility.Collapsed;
        _button.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        _button.ToolTip = "Downloads";
        if (focus) _panel.ViewAllButton.Focus();
    }
    private void Started(DownloadItem item, Guid originTabId)
    {
        if (!_engine.Tabs.Any(tab => tab.Id == originTabId)) return;
        if (_ownItems.Add(item)) item.PropertyChanged += ItemChanged;
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            if (_window.IsActive) Open(false);
            else
            {
                _button.ToolTip = item.FileName + ": " + item.StatusText;
                if (_statusIndicator is not null) _statusIndicator.Visibility = Visibility.Visible;
            }
        });
    }
    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DownloadItem.State) || sender is not DownloadItem item) return;
        if (item.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled)
        {
            if (!_popup.IsOpen && item.State != DownloadState.Cancelled)
            {
                _button.SetResourceReference(Control.ForegroundProperty, item.State == DownloadState.Failed ? "DangerBrush" : "TextPrimary");
                _button.ToolTip = item.FileName + ": " + item.StatusText;
                if (_statusIndicator is not null) _statusIndicator.Visibility = Visibility.Visible;
            }
            item.PropertyChanged -= ItemChanged; _ownItems.Remove(item);
        }
    }
    private void CloseClicked(object sender, RoutedEventArgs e) { _popup.IsOpen = false; _button.Focus(); }
    private void ViewAll(object sender, RoutedEventArgs e)
    { _popup.IsOpen = false; _ = OpenDownloadsAsync(); }
    private async Task OpenDownloadsAsync()
    { try { await _engine.OpenDownloadsAsync(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); } }
    private void OnKey(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { _popup.IsOpen = false; _button.Focus(); e.Handled = true; } }
    private void ActiveTabChanged(BrowserTab? tab) => _popup.IsOpen = false;
    private void Deactivated(object? sender, EventArgs e) => _popup.IsOpen = false;
    private void Resized(object sender, SizeChangedEventArgs e) { if (_popup.IsOpen) _popup.IsOpen = false; }
    private CustomPopupPlacement[] Place(Size popup, Size target, Point offset)
    {
        var anchor = (FrameworkElement)_popup.PlacementTarget;
        var left = anchor.TranslatePoint(new Point(), _window).X;
        return GetPlacements(popup, target, left, _window.ActualWidth);
    }
    public static CustomPopupPlacement[] GetPlacements(Size popup, Size target, double targetLeft, double windowWidth)
    {
        var left = Math.Clamp(targetLeft + target.Width - popup.Width, 8, Math.Max(8, windowWidth - popup.Width - 8));
        return [new(new Point(left - targetLeft, target.Height + 6), PopupPrimaryAxis.Vertical),
            new(new Point(left - targetLeft, -popup.Height - 6), PopupPrimaryAxis.Vertical)];
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _popup.IsOpen = false; _popup.CustomPopupPlacementCallback = null;
        _button.Click -= Toggle; _panel.CloseButton.Click -= CloseClicked; _panel.ViewAllButton.Click -= ViewAll;
        _popup.PreviewKeyDown -= OnKey; _engine.Downloads.DownloadStarted -= Started;
        _engine.ActiveTabChanged -= ActiveTabChanged;
        _window.Deactivated -= Deactivated; _window.SizeChanged -= Resized;
        foreach (var item in _ownItems) item.PropertyChanged -= ItemChanged;
        _ownItems.Clear(); _vm.Dispose();
    }
}
