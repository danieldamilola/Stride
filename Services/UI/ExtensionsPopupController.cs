using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using StrideBrowser.Controls;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services.Extensions;
using StrideBrowser.ViewModels;

namespace StrideBrowser.Services.UI;

public sealed class ExtensionsPopupController : IDisposable
{
    private readonly Window _window;
    private readonly Button _button;
    private readonly Popup _popup;
    private readonly ExtensionsPanel _panel;
    private readonly TabEngine _engine;
    private readonly ExtensionLibrary _library;
    private readonly Action _closeSuggestions;
    private readonly ExtensionsPanelViewModel _vm;
    private readonly ExtensionActions _actions;
    private bool _disposed;
    private bool _opening;
    private int _openVersion;
    public ExtensionsPopupController(Window window, Button button, Popup popup, ExtensionsPanel panel,
        ItemsControl pins, TabEngine engine, ExtensionLibrary library, ExtensionActions actions, Action closeSuggestions)
    {
        _window = window; _button = button; _popup = popup; _panel = panel; _engine = engine;
        _library = library; _closeSuggestions = closeSuggestions;
        actions.Owner = window;
        _actions = actions;
        _vm = new(library, actions, engine); panel.DataContext = _vm; pins.ItemsSource = _vm.PinnedExtensions;
        actions.AnchorForExtension = id =>
        {
            var item = _vm.PinnedExtensions.FirstOrDefault(x => x.Extension.Id == id);
            if (item is not null && pins.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container && container.IsVisible)
                return container;
            var overflow = (FrameworkElement)_window.FindName("ToolbarOverflowButton");
            return _button.IsVisible ? _button : overflow.IsVisible ? overflow : (FrameworkElement)_window.FindName("Toolbar");
        };
        _vm.Dismiss += Close;
        _popup.CustomPopupPlacementCallback = Place;
        button.Click += Toggle; panel.CloseButton.Click += CloseClicked;
        popup.PreviewKeyDown += KeyDown; library.Changed += Changed;
        engine.ActiveTabChanged += ActiveTabChanged;
        window.SizeChanged += SizeChanged; window.Deactivated += Deactivated;
    }
    public async Task OpenAsync()
    {
        var version = ++_openVersion;
        _opening = true;
        _closeSuggestions();
        _panel.Width = Math.Min(340, Math.Max(220, _window.ActualWidth - 24));
        _panel.ExtensionsScroll.MaxHeight = Math.Clamp(_window.ActualHeight - 180, 70, 350);
        await _vm.RefreshAsync();
        if (_disposed || !_window.IsVisible || version != _openVersion) return;
        _opening = false;
        _popup.IsOpen = true; _panel.ManageButton.Focus();
    }
    private void Toggle(object sender, RoutedEventArgs e) { if (_popup.IsOpen || _opening) Close(); else _ = OpenFromClickAsync(); }
    private async Task OpenFromClickAsync()
    {
        try { await OpenAsync(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }
    private void Close() { ++_openVersion; _opening = false; _popup.IsOpen = false; }
    private void CloseClicked(object sender, RoutedEventArgs e) { Close(); _button.Focus(); }
    private void KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Close(); _button.Focus(); e.Handled = true; } }
    private void ActiveTabChanged(BrowserTab? tab) { Close(); Changed(); }
    private void Changed() => _window.Dispatcher.BeginInvoke(async () => { if (!_disposed) await _vm.RefreshAsync(); });
    private void Deactivated(object? sender, EventArgs e) => Close();
    private void SizeChanged(object sender, SizeChangedEventArgs e) => Close();
    private CustomPopupPlacement[] Place(Size popup, Size target, Point offset) => DownloadsPopupController.GetPlacements(
        popup, target, ((FrameworkElement)_popup.PlacementTarget).TranslatePoint(new Point(), _window).X, _window.ActualWidth);
    public void Dispose()
    {
        _disposed = true; Close(); _vm.Dismiss -= Close;
        _actions.AnchorForExtension = null;
        _button.Click -= Toggle; _panel.CloseButton.Click -= CloseClicked; _popup.PreviewKeyDown -= KeyDown;
        _popup.CustomPopupPlacementCallback = null; _library.Changed -= Changed; _engine.ActiveTabChanged -= ActiveTabChanged;
        _window.SizeChanged -= SizeChanged; _window.Deactivated -= Deactivated;
    }
}
