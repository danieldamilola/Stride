using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using UmberBrowser.Models;

namespace UmberBrowser.Services.UI;

/// <summary>
/// Paints the toolbar chrome from the active tab theme color, scoped to one
/// window. The toolbar background animates toward the resolved site color.
/// When that color is light, text, hover, address field, popup and tab card
/// resources are overridden on the toolbar element so every control inside
/// this window follows. Dark colors remove the overrides so everything falls
/// back to the window and app theme. Nothing is written to app-wide
/// resources, so a second window keeps its own tint.
/// </summary>
public sealed class ToolbarTintAdapter
{
    private readonly Border _toolbar;
    private readonly Border _addressField;
    private readonly Border _popup;
    private string _currentThemeColorHex = "";
    private bool _refreshForced;

    private static readonly string[] TintKeys =
    [
        "TextPrimary", "TextSecondary", "TextMuted", "SurfaceHoverBrush",
        "AddressFieldBackground", "AddressFieldBorder",
        "SuggestionPopupBackground", "SuggestionPopupBorder",
        "SuggestionHoverBackground", "PinnedTabBackground", "TabCloseHoverBackground",
        "SurfaceBrush", "BorderBrush", "ControlBorderBrush", "SeparatorLineBrush",
        "ToolbarHoverBrush", "ToolbarPressedBrush", "SelectedTabBrush", "NeutralSelectedTabBrush"
    ];

    public ToolbarTintAdapter(Border toolbar, Border addressField, Border popup)
    {
        _toolbar = toolbar;
        _addressField = addressField;
        _popup = popup;
    }

    public void Reset()
    {
        _currentThemeColorHex = "";
        _refreshForced = true;
    }

    public void UpdateTint(BrowserTab tab, bool adaptiveEnabled)
    {
        var hex = adaptiveEnabled ? tab.ThemeColor ?? "" : "";
        if (hex == _currentThemeColorHex && !_refreshForced) return;
        _currentThemeColorHex = hex;
        _refreshForced = false;

        var baseColor = (Color)_toolbar.FindResource("SidebarColor");
        var targetColor = ToolbarTintResolver.ResolveTargetColor(hex, baseColor);

        var anim = new ColorAnimation
        {
            To = targetColor,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        var brush = _toolbar.Background as SolidColorBrush;
        if (brush is null || brush.IsFrozen)
        {
            brush = new SolidColorBrush(baseColor);
            _toolbar.Background = brush;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);

        if (targetColor == baseColor)
            ClearOverrides();
        else if (ToolbarTintResolver.IsLight(targetColor))
            ApplyLightOverrides();
        else
            ApplyDarkOverrides();
    }

    private void ApplyLightOverrides()
    {
        Set("TextPrimary", Color.FromRgb(0x20, 0x20, 0x20));
        Set("TextSecondary", Color.FromRgb(0x5F, 0x5F, 0x5F));
        Set("TextMuted", Color.FromRgb(0x5F, 0x5F, 0x5F));
        Set("SurfaceBrush", Colors.White);
        Set("SurfaceHoverBrush", Color.FromRgb(0xE8, 0xE8, 0xE8));
        Set("BorderBrush", Color.FromRgb(0xD9, 0xD9, 0xD9));
        Set("ControlBorderBrush", Color.FromRgb(0x8D, 0x8D, 0x8D));
        Set("SeparatorLineBrush", Color.FromRgb(0xCE, 0xCE, 0xCE));
        Set("AddressFieldBackground", Colors.White);
        Set("AddressFieldBorder", Color.FromRgb(0x8D, 0x8D, 0x8D));
        Set("SuggestionPopupBackground", Colors.White);
        Set("SuggestionPopupBorder", Color.FromRgb(0xD9, 0xD9, 0xD9));
        Set("SuggestionHoverBackground", Color.FromRgb(0xF0, 0xF0, 0xF0));
        Set("PinnedTabBackground", Color.FromRgb(0xE8, 0xE8, 0xE8));
        Set("TabCloseHoverBackground", Color.FromRgb(0xD9, 0xD9, 0xD9));
        Set("ToolbarHoverBrush", Color.FromRgb(0xE0, 0xE0, 0xE0));
        Set("ToolbarPressedBrush", Color.FromRgb(0xD9, 0xD9, 0xD9));
        Set("SelectedTabBrush", Color.FromRgb(0xE0, 0xE0, 0xE0));
        Set("NeutralSelectedTabBrush", Color.FromRgb(0xE0, 0xE0, 0xE0));
    }

    private void ApplyDarkOverrides()
    {
        Set("TextPrimary", Color.FromRgb(0xEE, 0xEE, 0xEE));
        Set("TextSecondary", Color.FromRgb(0xB4, 0xB4, 0xB4));
        Set("TextMuted", Color.FromRgb(0x8A, 0x8A, 0x8A));
        Set("SurfaceBrush", Color.FromRgb(0x22, 0x22, 0x22));
        Set("SurfaceHoverBrush", Color.FromRgb(0x2A, 0x2A, 0x2A));
        Set("BorderBrush", Color.FromRgb(0x31, 0x31, 0x31));
        Set("ControlBorderBrush", Color.FromRgb(0x60, 0x60, 0x60));
        Set("SeparatorLineBrush", Color.FromRgb(0x3C, 0x3C, 0x42));
        Set("AddressFieldBackground", Color.FromArgb(0x0D, 255, 255, 255));
        Set("AddressFieldBorder", Color.FromArgb(0x1A, 255, 255, 255));
        Set("SuggestionPopupBackground", Color.FromRgb(0x22, 0x22, 0x22));
        Set("SuggestionPopupBorder", Color.FromRgb(0x31, 0x31, 0x31));
        Set("SuggestionHoverBackground", Color.FromRgb(0x2A, 0x2A, 0x2A));
        Set("PinnedTabBackground", Color.FromArgb(0x0D, 255, 255, 255));
        Set("TabCloseHoverBackground", Color.FromArgb(0x1A, 255, 255, 255));
        Set("ToolbarHoverBrush", Color.FromArgb(0x0A, 255, 255, 255));
        Set("ToolbarPressedBrush", Color.FromArgb(0x14, 255, 255, 255));
        Set("SelectedTabBrush", Color.FromRgb(0x31, 0x31, 0x31));
        Set("NeutralSelectedTabBrush", Color.FromRgb(0x2A, 0x2A, 0x2A));
    }

    private void ClearOverrides()
    {
        foreach (var key in TintKeys)
            _toolbar.Resources.Remove(key);
    }

    private void Set(string key, Color color) =>
        _toolbar.Resources[key] = new SolidColorBrush(color);
}
