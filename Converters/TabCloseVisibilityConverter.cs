using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StrideBrowser.Converters;

/// <summary>
/// Decides the tab end close button visibility from hover, selection, tab style,
/// container width, and pin state. One binding replaces a fragile trigger
/// stack: the button only ever shows while hovering its tab. Pinned never,
/// crowded yields to the favicon overlay, otherwise the hovered active tab and
/// every hovered wide Full Names tab get the end button.
/// </summary>
public sealed class TabCloseVisibilityConverter : IMultiValueConverter
{
    public double NarrowThreshold { get; set; } = 44.0;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 5) return Visibility.Collapsed;
        if (values[4] is bool pinned && pinned) return Visibility.Collapsed;
        if (values[2] is double width && width < NarrowThreshold) return Visibility.Collapsed;
        if (values[3] is not bool hovered || !hovered) return Visibility.Collapsed;
        if (values[0] is bool selected && selected) return Visibility.Visible;
        if (values[1] is bool fullNames && fullNames) return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
