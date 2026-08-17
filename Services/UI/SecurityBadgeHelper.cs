using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using StrideBrowser.Models;
using StrideBrowser.ViewModels;

namespace StrideBrowser.Services.UI;

/// <summary>
/// Manages the visibility, icon (padlock/globe), and loading animations
/// for the security badges in the address bar.
/// </summary>
public sealed class SecurityBadgeHelper
{
    private readonly Path _securityIcon;
    private readonly Path _standardSecurityIcon;
    private readonly BrowserSettings _settings;

    public SecurityBadgeHelper(Path securityIcon, Path standardSecurityIcon, BrowserSettings settings)
    {
        _securityIcon = securityIcon;
        _standardSecurityIcon = standardSecurityIcon;
        _settings = settings;
    }

    public void UpdateSecurityIcon(string url)
    {
        if (string.IsNullOrEmpty(url) || InternalUrls.IsInternal(url))
        {
            _securityIcon.Visibility = Visibility.Collapsed;
            _standardSecurityIcon.Visibility = Visibility.Collapsed;
            return;
        }

        if (_settings.UseFloatingCommandBar)
        {
            _securityIcon.Visibility = Visibility.Visible;
            _standardSecurityIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            _securityIcon.Visibility = Visibility.Collapsed;
            _standardSecurityIcon.Visibility = Visibility.Visible;
        }

        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _securityIcon.Data = (StreamGeometry)Application.Current.FindResource("IconLock");
            _securityIcon.Stroke = new SolidColorBrush(Color.FromRgb(0x6B, 0x8F, 0x71));
            _standardSecurityIcon.Data = (StreamGeometry)Application.Current.FindResource("IconLock");
            _standardSecurityIcon.Stroke = new SolidColorBrush(Color.FromRgb(0x6B, 0x8F, 0x71));
        }
        else
        {
            _securityIcon.Data = (StreamGeometry)Application.Current.FindResource("IconGlobe");
            _securityIcon.Stroke = (Brush)Application.Current.FindResource("TextSecondary");
            _standardSecurityIcon.Data = (StreamGeometry)Application.Current.FindResource("IconGlobe");
            _standardSecurityIcon.Stroke = (Brush)Application.Current.FindResource("TextSecondary");
        }
    }

    public void StartSecuritySpinner()
    {
        if (_securityIcon.Visibility != Visibility.Visible) return;

        StopSecuritySpinner();
        _securityIcon.Data = (StreamGeometry)Application.Current.FindResource("IconRefresh");
        _securityIcon.Stroke = (Brush)Application.Current.FindResource("Accent");
        var rot = new RotateTransform();
        _securityIcon.RenderTransformOrigin = new Point(0.5, 0.5);
        _securityIcon.RenderTransform = rot;
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        rot.BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    public void StopSecuritySpinner()
    {
        if (_securityIcon.RenderTransform is RotateTransform rot)
            rot.BeginAnimation(RotateTransform.AngleProperty, null);
        _securityIcon.RenderTransform = null;
    }
}
