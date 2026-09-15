using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StrideBrowser.Services.UI;

/// <summary>
/// Drag feedback for tab reorder, modeled on Zen's drag image: a floating pill
/// built from the tab's favicon and title that follows the cursor while the
/// strip slides the remaining tabs out of the way underneath it.
/// </summary>
public sealed class TabDragAdorner : Adorner
{
    private readonly ImageSource? _favicon;
    private readonly string _title;
    private readonly double _ghostWidth;
    private readonly double _ghostHeight;
    private readonly Brush _plate;
    private readonly Typeface _typeface;
    private readonly double _pixelsPerDip;
    private Point _mouse;

    public TabDragAdorner(UIElement adornedElement, ImageSource? favicon, string title, double height)
        : base(adornedElement)
    {
        _favicon = favicon;
        _title = Shorten(title, 28);
        IsHitTestVisible = false;
        _typeface = new Typeface("Segoe UI");
        _pixelsPerDip = VisualTreeHelper.GetDpi(adornedElement).PixelsPerDip;
        _plate = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E));
        _plate.Freeze();

        var text = Measure(_title, 12);
        _ghostWidth = Math.Min(220, 12 + 20 + 6 + text.Width + 12);
        _ghostHeight = Math.Max(20, height);
    }

    public void Update(Point mouse)
    {
        _mouse = mouse;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var top = Math.Max(0, (AdornedElement.RenderSize.Height - _ghostHeight) / 2);
        var rect = new Rect(_mouse.X - _ghostWidth / 2, top, _ghostWidth, _ghostHeight);
        dc.DrawRoundedRectangle(_plate, null, rect, 9, 9);
        var iconRect = new Rect(rect.X + 10, top + (_ghostHeight - 16) / 2, 16, 16);
        if (_favicon is not null)
            dc.DrawImage(_favicon, iconRect);
        var text = new FormattedText(_title, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, _typeface, 12,
            Brushes.White, _pixelsPerDip);
        text.MaxTextWidth = Math.Max(0, rect.Width - 12 - 20 - 6 - 10);
        text.Trimming = TextTrimming.None;
        dc.DrawText(text, new Point(iconRect.Right + 6, top + (_ghostHeight - text.Height) / 2));
    }

    private FormattedText Measure(string text, double size) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            _typeface, size, SystemColors.ControlTextBrush, _pixelsPerDip);

    private static string Shorten(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "New Tab";
        return text.Length <= max ? text : text.Substring(0, max);
    }

    /// <summary>
    /// Target slot for the dragged card, driven by the card body rather than the
    /// cursor point: moving right, the card's leading right edge decides; moving
    /// left, its leading left edge decides. Pure logic, covered by unit tests.
    /// </summary>
    public static int ComputeShiftedIndex(IReadOnlyList<double> centers, int oldIndex, double x, double lastX, double halfWidth)
    {
        if (centers.Count == 0) return 0;
        int slot;
        if (x >= lastX)
        {
            var edge = x + halfWidth;
            var i = 0;
            while (i < centers.Count && centers[i] <= edge) i++;
            slot = i - 1;
        }
        else
        {
            var edge = x - halfWidth;
            var i = 0;
            while (i < centers.Count && centers[i] < edge) i++;
            slot = i;
        }
        return Math.Clamp(slot, 0, centers.Count - 1);
    }

    /// <summary>Loads the fallback Stride mark used when a tab has no favicon. Dark by default.</summary>
    public static ImageSource? DefaultIcon(bool light = false)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(light
                ? "pack://application:,,,/icons/stride-browser-light-32x32.png"
                : "pack://application:,,,/icons/stride-browser-dark-32x32.png");
            bmp.DecodePixelWidth = 32;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
