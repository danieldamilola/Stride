using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using StrideBrowser.Models;

namespace StrideBrowser.Services.UI;

internal sealed class TabDragPreview : IDisposable
{
    private readonly Popup _popup;

    public TabDragPreview(BrowserTab tab)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Image { Source = tab.Favicon ?? TabDragAdorner.DefaultIcon(false), Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(new TextBlock { Text = tab.Title, Foreground = Brushes.White, MaxWidth = 220, VerticalAlignment = VerticalAlignment.Center });
        _popup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.AbsolutePoint,
            StaysOpen = true,
            IsHitTestVisible = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 42, 42, 46)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Child = row
            }
        };
        _popup.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(_popup.Child) is HwndSource source)
            {
                const int extendedStyle = -20;
                const int transparent = 0x20;
                const int noActivate = 0x08000000;
                SetWindowLong(source.Handle, extendedStyle, GetWindowLong(source.Handle, extendedStyle) | transparent | noActivate);
            }
        };
    }

    public void Update(Point screenPoint, bool visible, Visual source)
    {
        var dpi = VisualTreeHelper.GetDpi(source);
        _popup.HorizontalOffset = screenPoint.X / dpi.DpiScaleX + 16;
        _popup.VerticalOffset = screenPoint.Y / dpi.DpiScaleY + 16;
        _popup.IsOpen = visible;
    }

    public void Dispose() => _popup.IsOpen = false;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);
}
