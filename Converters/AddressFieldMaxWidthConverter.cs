using System;
using System.Globalization;
using System.Windows.Data;
using StrideBrowser.Services.UI;

namespace StrideBrowser.Converters;

/// <summary>
/// Caps the address field at the width the middle of the toolbar row can spare. The field is
/// declared at its full <see cref="ToolbarLayoutMetrics.AddressMaxWidth"/> and WPF clamps it to
/// this maximum, so a roomy window lays out exactly as it did and a narrow one hands the width
/// back to the tab strip instead of squeezing it. The rule lives in
/// <see cref="ToolbarLayoutMetrics"/> so it is testable without a window.
/// </summary>
public sealed class AddressFieldMaxWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double windowWidth
            ? ToolbarLayoutMetrics.AddressWidth(windowWidth)
            : ToolbarLayoutMetrics.AddressMaxWidth;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
