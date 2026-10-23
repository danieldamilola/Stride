using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UmberBrowser.Helpers;

namespace UmberBrowser.Services.UI;

/// <summary>
/// Renders the dragged tab at its actual size and keeps the pointer's grip position.
/// </summary>
public sealed class TabDragAdorner : Adorner
{
    private readonly ImageSource? _favicon;
    private readonly string _title;
    private readonly bool _isPinned;
    private readonly double _ghostWidth;
    private readonly double _ghostHeight;
    private readonly Brush _plate;
    private readonly Typeface _typeface;
    private readonly double _pixelsPerDip;
    private Point _mouse;

    public TabDragAdorner(UIElement adornedElement, ImageSource? favicon, string title, double height, bool isPinned = false)
        : base(adornedElement)
    {
        _favicon = favicon;
        _isPinned = isPinned;
        _title = isPinned ? string.Empty : Shorten(title, 28);
        IsHitTestVisible = false;
        _typeface = new Typeface("Segoe UI");
        _pixelsPerDip = VisualTreeHelper.GetDpi(adornedElement).PixelsPerDip;
        _plate = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E));
        _plate.Freeze();

        if (isPinned)
        {
            _ghostWidth = 32;
            _ghostHeight = 31;
        }
        else
        {
            var text = Measure(_title, 12);
            _ghostWidth = Math.Min(TabShrinkPanel.MaxTabWidth, 6 + 16 + 6 + text.Width + 12);
            _ghostHeight = 31;
        }
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
        dc.DrawRoundedRectangle(_plate, null, rect, 8, 8);

        if (_isPinned)
        {
            var iconRect = new Rect(rect.X + (_ghostWidth - 16) / 2, top + (_ghostHeight - 16) / 2, 16, 16);
            if (_favicon is not null)
                dc.DrawImage(_favicon, iconRect);
        }
        else
        {
            var iconRect = new Rect(rect.X + 6, top + (_ghostHeight - 16) / 2, 16, 16);
            if (_favicon is not null)
                dc.DrawImage(_favicon, iconRect);
            var text = new FormattedText(_title, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, _typeface, 12,
                Brushes.White, _pixelsPerDip);
            text.MaxTextWidth = Math.Max(0, rect.Width - 6 - 16 - 6 - 10);
            text.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(text, new Point(iconRect.Right + 6, top + (_ghostHeight - text.Height) / 2));
        }
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
    public static int ComputeShiftedIndex(IReadOnlyList<double> centers, int oldIndex, double x, double lastX, double halfWidth, int minSlot = 0, int maxSlot = -1)
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
        var hi = maxSlot >= 0 ? Math.Min(maxSlot, centers.Count - 1) : centers.Count - 1;
        var lo = Math.Clamp(minSlot, 0, hi);
        return Math.Clamp(slot, lo, hi);
    }

    /// <summary>Loads the fallback Umber mark used when a tab has no favicon. Dark by default.</summary>
    public static ImageSource? DefaultIcon(bool light = false)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(light
                ? AppIconResources.TabLight
                : AppIconResources.TabDark);
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
