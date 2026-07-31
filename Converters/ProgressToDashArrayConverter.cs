using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StrideBrowser.Converters;

public class ProgressToDashArrayConverter : IValueConverter
{
    public double Radius { get; set; } = 11;
    public double StrokeThickness { get; set; } = 2;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double progress = 0;
        if (value is double d) progress = d;
        if (value is float f) progress = f;

        // Clamp progress to 0-1
        progress = Math.Max(0, Math.Min(1, progress));

        // Ensure we always show a tiny sliver if progress is > 0 but very small
        if (progress > 0 && progress < 0.05) progress = 0.05;

        // Circumference of the circle = 2 * PI * r
        double circumference = 2 * Math.PI * Radius;

        // In WPF, StrokeDashArray is measured in multiples of the StrokeThickness
        double circumferenceInThickness = circumference / StrokeThickness;
        
        return new DoubleCollection(new[] { progress * circumferenceInThickness, circumferenceInThickness });
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
