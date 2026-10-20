using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StrideBrowser.Services.UI;

/// <summary>
/// Builds menu separators. A Separator inside a ContextMenu resolves its style from the
/// WPF theme default rather than from Application.Resources, so a styled Separator paints
/// as a bright grey line that ignores the theme. Applying the template and brush here,
/// on the element itself as a local value, cannot be overridden by the theme default.
/// </summary>
public static class MenuSeparator
{
    /// <summary>
    /// A separator painted with the themed line colour. The template is built per call so a
    /// theme switch is picked up: caching one would keep serving the brush from whichever
    /// theme happened to be active when it was first built.
    /// </summary>
    public static Separator Create() => new()
    {
        Height = 1,
        Margin = new Thickness(10, 5, 10, 5),
        Template = BuildTemplate(),
    };

    private static ControlTemplate BuildTemplate()
    {
        // A real Brush instance rather than a DynamicResource inside the template, so the
        // colour is fixed at creation and the separator never flashes the wrong theme.
        var brush = Application.Current?.TryFindResource("SeparatorLineBrush") as SolidColorBrush
                    ?? new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x42));
        brush.Freeze();

        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(Border.HeightProperty, 1d);
        line.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        line.SetValue(Border.BorderBrushProperty, brush);

        var template = new ControlTemplate(typeof(Separator));
        template.VisualTree = line;
        template.Seal();
        return template;
    }
}
