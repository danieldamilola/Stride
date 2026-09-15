using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StrideBrowser.Converters;

/// <summary>
/// Collapses tab titles once the tab container shrinks near icon width, so a
/// crowded strip degrades to favicons instead of clipping text. A parameter
/// sets the threshold in pixels and a leading ! inverts the result, so the
/// favicon close overlay can reappear exactly when the end button hides.
/// </summary>
public sealed class TabWidthToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var threshold = 52.0;
        var invert = false;
        var text = parameter as string;
        if (!string.IsNullOrEmpty(text))
        {
            if (text.StartsWith("!", StringComparison.Ordinal))
            {
                invert = true;
                text = text[1..];
            }
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                threshold = parsed;
        }
        else if (parameter is double d)
            threshold = d;

        var wide = value is double width && width >= threshold;
        return wide != invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
