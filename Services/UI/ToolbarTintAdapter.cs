using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StrideBrowser.Models;

namespace StrideBrowser.Services.UI;

/// <summary>
/// Animates the toolbar background color based on the current tab's theme color.
/// Adapts foreground text resources to ensure contrast.
/// </summary>
public sealed class ToolbarTintAdapter
{
    private readonly FrameworkElement _toolbar;
    private string _currentThemeColorHex = "";

    public ToolbarTintAdapter(FrameworkElement toolbar)
    {
        _toolbar = toolbar;
    }

    public void UpdateTint(BrowserTab tab)
    {
        var hex = tab.ThemeColor ?? "";
        if (hex == _currentThemeColorHex) return;
        _currentThemeColorHex = hex;

        var baseColor = (Color)_toolbar.FindResource("SidebarColor");
        Color targetColor;

        if (!string.IsNullOrEmpty(hex))
        {
            try
            {
                targetColor = (Color)ColorConverter.ConvertFromString(hex);
                // Reject pure/near white. This prevents the toolbar from turning blindingly white 
                // when Dark Reader is active but the site's meta theme-color tag still says #FFFFFF.
                if (targetColor.R > 245 && targetColor.G > 245 && targetColor.B > 245)
                {
                    targetColor = Color.FromRgb(0x11, 0x11, 0x11);
                }
                // Reject strong green colors (e.g. jiji.ng) because they clash with the dark theme
                else if (targetColor.G > 120 && targetColor.G > targetColor.R + 40 && targetColor.G > targetColor.B + 40)
                {
                    targetColor = Color.FromRgb(0x11, 0x11, 0x11);
                }
            }
            catch
            {
                targetColor = baseColor;
            }
        }
        else
        {
            targetColor = baseColor;
        }

        var anim = new ColorAnimation
        {
            To = targetColor,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        // FrameworkElement doesn't expose Background, so we check if it's a Control or Panel
        SolidColorBrush? brush = null;
        if (_toolbar is System.Windows.Controls.Control control)
        {
            brush = control.Background as SolidColorBrush;
            if (brush is null || brush.IsFrozen)
            {
                brush = new SolidColorBrush(baseColor);
                control.Background = brush;
            }
        }
        else if (_toolbar is System.Windows.Controls.Panel panel)
        {
            brush = panel.Background as SolidColorBrush;
            if (brush is null || brush.IsFrozen)
            {
                brush = new SolidColorBrush(baseColor);
                panel.Background = brush;
            }
        }
        else if (_toolbar is System.Windows.Controls.Border border)
        {
            brush = border.Background as SolidColorBrush;
            if (brush is null || brush.IsFrozen)
            {
                brush = new SolidColorBrush(baseColor);
                border.Background = brush;
            }
        }

        if (brush != null)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }

        // Dynamic Contrast: If the toolbar adapts to a light color, switch icons/text to dark
        var luminance = (0.299 * targetColor.R + 0.587 * targetColor.G + 0.114 * targetColor.B) / 255.0;
        if (luminance > 0.5)
        {
            _toolbar.Resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(30, 30, 34));
            _toolbar.Resources["TextSecondary"] = new SolidColorBrush(Color.FromRgb(70, 70, 74));
            _toolbar.Resources["TextMuted"] = new SolidColorBrush(Color.FromRgb(100, 100, 104));
        }
        else
        {
            // Dark background -> remove local overrides so they fall back to global App.xaml theme
            _toolbar.Resources.Remove("TextPrimary");
            _toolbar.Resources.Remove("TextSecondary");
            _toolbar.Resources.Remove("TextMuted");
        }
    }
}
