using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using StrideBrowser.Services.UI;

namespace StrideBrowser.Converters;

/// <summary>
/// Shows or hides one part of the toolbar action cluster from the window width. The parameter
/// names the part: "Optional" is the downloads, reader and update buttons, which fold into the
/// overflow button as the window narrows; "Overflow" is that button; "Settings" is the settings
/// button, which keeps its place longest. Which of them the row has room for is decided by
/// <see cref="ToolbarLayoutMetrics.ActionsMode"/>, so the ladder is testable without a window.
/// </summary>
public sealed class ToolbarActionVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var mode = value is double windowWidth
            ? ToolbarLayoutMetrics.ActionsMode(windowWidth)
            : ToolbarActionsMode.None;

        var visible = (parameter as string) switch
        {
            "Optional" => mode == ToolbarActionsMode.Full,
            "Overflow" => mode == ToolbarActionsMode.Overflow,
            "Settings" => mode != ToolbarActionsMode.None,
            _ => false,
        };

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
