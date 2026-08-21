using System;
using System.Globalization;
using System.Windows.Data;

namespace StrideBrowser.Converters;

/// <summary>
/// Returns the favicon opacity for a tab based on its lifecycle state
/// and user-configured dim levels for sleeping and hibernated tabs.
/// </summary>
public sealed class TabOpacityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 6) return 1.0;

        var isActive = values[0] is bool b0 && b0;
        var isHibernated = values[1] is bool b1 && b1;
        var isSleeping = values[2] is bool b2 && b2;
        var isMouseOver = values[5] is bool b5 && b5;

        double sleepOpacity = 0.55;
        double hibernateOpacity = 0.15;
        bool sleepDimEnabled = true;
        bool hibernationDimEnabled = true;

        if (values[3] is double d0) sleepOpacity = d0;
        if (values[4] is double d1) hibernateOpacity = d1;
        if (values.Length > 6 && values[6] is bool b6) sleepDimEnabled = b6;
        if (values.Length > 7 && values[7] is bool b7) hibernationDimEnabled = b7;

        if (isMouseOver) return 1.0;
        if (isHibernated) return hibernationDimEnabled ? Math.Clamp(hibernateOpacity, 0.1, 1.0) : 1.0;
        if (isActive) return 1.0;
        if (isSleeping) return sleepDimEnabled ? Math.Clamp(sleepOpacity, 0.1, 1.0) : 1.0;
        return 1.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
