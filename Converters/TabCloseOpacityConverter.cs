using System;
using System.Globalization;
using System.Windows.Data;

namespace UmberBrowser.Converters;

/// <summary>
/// Fades the tab close button in on hover and keeps it lit on the active tab.
/// The button keeps its layout slot at zero opacity so the whole end of a wide
/// card is clickable before the cross appears, the way Helium behaves. Narrow
/// cards hand the job to the favicon overlay instead, so they pass zero.
/// </summary>
public sealed class TabCloseOpacityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 3 && values[2] is double width && width < TabCloseVisibilityConverter.NarrowTabWidth)
            return 0.0;
        if (values.Length >= 1 && values[0] is bool hovered && hovered) return 1.0;
        return values.Length >= 2 && values[1] is bool active && active ? 1.0 : 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
