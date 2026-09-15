using System;
using System.Windows.Media;

namespace StrideBrowser.Services.UI;

/// <summary>
/// Pure color math behind the adaptive toolbar tint: maps a site theme color
/// to the toolbar target, rejecting near-white and clashing greens and
/// falling back to the neutral base on bad input. Covered by unit tests.
/// </summary>
public static class ToolbarTintResolver
{
    public static Color ResolveTargetColor(string? hex, Color baseColor)
    {
        if (string.IsNullOrEmpty(hex)) return baseColor;
        Color target;
        try
        {
            target = (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return baseColor;
        }

        // Reject pure/near white. Prevents a blinding toolbar when Dark Reader
        // is active but the site meta theme-color still says white.
        if (target.R > 245 && target.G > 245 && target.B > 245)
            return Color.FromRgb(0x11, 0x11, 0x11);

        // Reject strong greens that clash with the dark theme.
        if (target.G > 120 && target.G > target.R + 40 && target.G > target.B + 40)
            return Color.FromRgb(0x11, 0x11, 0x11);

        return target;
    }

    public static bool IsLight(Color color) =>
        (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0 > 0.5;
}
