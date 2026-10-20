using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using StrideBrowser.Models;
using Path = System.Windows.Shapes.Path;

namespace StrideBrowser.Services.UI;

public sealed class SiteInfoController : IDisposable
{
    private readonly Button _button;
    private readonly Path _icon;
    private readonly Popup _popup;
    private readonly TextBlock _host;
    private readonly TextBlock _connection;
    private readonly TextBlock _description;
    private readonly Action _closeSuggestions;
    private Guid? _tabId;
    private string? _url;
    private bool _loading;

    public SiteInfoController(Button button, Path icon, Popup popup, TextBlock host,
        TextBlock connection, TextBlock description, Action closeSuggestions)
    {
        _button = button;
        _icon = icon;
        _popup = popup;
        _host = host;
        _connection = connection;
        _description = description;
        _closeSuggestions = closeSuggestions;
        _popup.CustomPopupPlacementCallback = PlacePopup;
        _button.Click += OnClick;
        _popup.PreviewKeyDown += OnPopupKeyDown;
    }

    public void Update(BrowserTab? tab)
    {
        if (_tabId != tab?.Id || _url != tab?.Url || tab?.IsLoading == true)
            _popup.IsOpen = false;
        _tabId = tab?.Id;
        _url = tab?.Url;

        var info = SiteConnectionInfo.FromUrl(_url);
        _button.Visibility = info is null ? Visibility.Collapsed : Visibility.Visible;
        _button.IsEnabled = info is not null && tab?.IsLoading != true;
        SetLoading(info is not null && tab?.IsLoading == true);
        if (info is null)
        {
            _popup.IsOpen = false;
            return;
        }

        _host.Text = info.Host;
        _connection.Text = info.Summary;
        _description.Text = info.Description;
        _connection.SetResourceReference(TextBlock.ForegroundProperty,
            info.IsInsecure ? "DangerBrush" : "TextPrimary");
        var label = info.IsInsecure ? "Site information: connection is not encrypted" : "Site information";
        _button.ToolTip = label;
        AutomationProperties.SetName(_button, label);

        _icon.Data = (Geometry)_button.FindResource(_loading ? "IconRefresh" :
            info.IsInsecure ? "IconConnectionWarning" : "IconSiteControls");
        _icon.SetResourceReference(Shape.StrokeProperty,
            _loading ? "Accent" : info.IsInsecure ? "DangerBrush" : "TextMuted");
    }

    private void SetLoading(bool loading)
    {
        if (_loading == loading) return;
        _loading = loading;
        if (_icon.RenderTransform is RotateTransform previous)
            previous.BeginAnimation(RotateTransform.AngleProperty, null);
        _icon.RenderTransform = Transform.Identity;
        if (!loading) return;

        var rotation = new RotateTransform();
        _icon.RenderTransformOrigin = new Point(0.5, 0.5);
        _icon.RenderTransform = rotation;
        rotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });
    }

    private CustomPopupPlacement[] PlacePopup(Size popupSize, Size targetSize, Point offset)
    {
        var window = Window.GetWindow(_button);
        var targetLeft = window is null ? 0 : _button.TranslatePoint(new Point(), window).X;
        return GetPopupPlacements(popupSize, targetSize, targetLeft,
            window?.ActualWidth ?? popupSize.Width + 16);
    }

    public static CustomPopupPlacement[] GetPopupPlacements(Size popupSize, Size targetSize,
        double targetLeft, double windowWidth)
    {
        const double inset = 8;
        const double gap = 6;
        var left = Math.Clamp(targetLeft, inset, Math.Max(inset, windowWidth - popupSize.Width - inset));
        var x = left - targetLeft;
        return
        [
            new(new Point(x, targetSize.Height + gap), PopupPrimaryAxis.Vertical),
            new(new Point(x, -popupSize.Height - gap), PopupPrimaryAxis.Vertical)
        ];
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        _closeSuggestions();
        _popup.IsOpen = !_popup.IsOpen;
        if (_popup.IsOpen) _popup.Child?.Focus();
        e.Handled = true;
    }

    private void OnPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _popup.IsOpen = false;
        _button.Focus();
        e.Handled = true;
    }

    public void Dispose()
    {
        _popup.CustomPopupPlacementCallback = null;
        _button.Click -= OnClick;
        _popup.PreviewKeyDown -= OnPopupKeyDown;
        _popup.IsOpen = false;
        SetLoading(false);
    }
}
