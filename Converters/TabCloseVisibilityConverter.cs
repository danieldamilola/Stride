using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UmberBrowser.Converters;

/// <summary>
/// Reserves the end close button's space on wide tabs and reveals it on hover.
/// Narrow tabs replace the favicon with a close button on hover.
/// Pinned tabs keep their favicon and have no close button.
/// </summary>
public sealed class TabCloseVisibilityConverter : IMultiValueConverter
{
    /// <summary>Width below which a card drops its end close button for a favicon overlay.</summary>
    public const double NarrowTabWidth = 56.0;

    public double NarrowThreshold { get; set; } = NarrowTabWidth;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is string mode && (mode == "Narrow" || mode == "NarrowInverse"))
        {
            // A favicon card shows the cross only once it is selected, so the
            // close never appears from a stray hover on a crowded tab.
            var show = values.Length >= 4 && values[0] is false && values[3] is true
                && values[2] is double narrowWidth && narrowWidth < NarrowThreshold;
            return show == (mode == "Narrow") ? Visibility.Visible : Visibility.Collapsed;
        }
        if (string.Equals(parameter as string, "Swap", StringComparison.OrdinalIgnoreCase))
            return ShouldSwap(values) ? Visibility.Visible : Visibility.Collapsed;
        if (string.Equals(parameter as string, "SwapInverse", StringComparison.OrdinalIgnoreCase))
            return ShouldSwap(values) ? Visibility.Collapsed : Visibility.Visible;

        if (values.Length < 3) return Visibility.Collapsed;
        if (values[0] is bool pinned && pinned) return Visibility.Collapsed;
        if (values[2] is double width && width < NarrowThreshold) return Visibility.Collapsed;
        // The slot stays visible even when the cross is not drawn, so the end of
        // every wide card is clickable before the cross fades in. Opacity does
        // the appearing, not Visibility.
        return Visibility.Visible;
    }

    private static bool ShouldSwap(object[] values)
    {
        if (values.Length < 3) return false;
        if (values[2] is bool pinned && pinned) return false;
        return values[0] is bool selected && selected
            && values[1] is bool hovered && hovered;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
