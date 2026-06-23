using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SpurBrowser.Helpers;

/// <summary>
/// Manages the loading bar sweep animation and security icon spinner.
/// Owns the storyboard lifecycle so MainWindow stays clean.
/// </summary>
public sealed class LoadingIndicatorController
{
    private readonly FrameworkElement _loadingBar;
    private readonly FrameworkElement _loadingIndicator;
    private readonly Path _securityIcon;
    private readonly FrameworkElement _resourceHost;

    private Storyboard? _loadingStoryboard;

    private const double IndicatorWidth = 140;
    private const double OvershootPx = 20;
    private const double FallbackContainerWidth = 900;
    private const double SweepDurationSeconds = 1.4;

    public LoadingIndicatorController(
        FrameworkElement loadingBar,
        FrameworkElement loadingIndicator,
        Path securityIcon,
        FrameworkElement resourceHost)
    {
        _loadingBar = loadingBar;
        _loadingIndicator = loadingIndicator;
        _securityIcon = securityIcon;
        _resourceHost = resourceHost;
    }

    public void StartLoadingAnimation()
    {
        StopLoadingAnimation();

        var transform = new TranslateTransform();
        _loadingIndicator.RenderTransform = transform;

        var containerWidth = _loadingBar.ActualWidth > 0 ? _loadingBar.ActualWidth : FallbackContainerWidth;
        var anim = new DoubleAnimation
        {
            From = -IndicatorWidth,
            To = containerWidth + OvershootPx,
            Duration = TimeSpan.FromSeconds(SweepDurationSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        _loadingStoryboard = new Storyboard();
        Storyboard.SetTarget(anim, _loadingIndicator);
        Storyboard.SetTargetProperty(anim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
        _loadingStoryboard.Children.Add(anim);
        _loadingStoryboard.Begin();
    }

    public void StopLoadingAnimation()
    {
        _loadingStoryboard?.Stop();
        _loadingStoryboard = null;
    }

    public void UpdateSecurityIcon(string url)
    {
        if (string.IsNullOrEmpty(url) || Models.InternalUrls.IsInternal(url))
        {
            _securityIcon.Visibility = Visibility.Collapsed;
            return;
        }

        _securityIcon.Visibility = Visibility.Visible;
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _securityIcon.Data = (StreamGeometry)_resourceHost.FindResource("IconLock");
            _securityIcon.Stroke = new SolidColorBrush(Color.FromRgb(0x6B, 0x8F, 0x71));
        }
        else
        {
            _securityIcon.Data = (StreamGeometry)_resourceHost.FindResource("IconGlobe");
            _securityIcon.Stroke = (Brush)_resourceHost.FindResource("TextSecondary");
        }
    }

    public void StartSecuritySpinner()
    {
        if (_securityIcon.Visibility != Visibility.Visible) return;

        StopSecuritySpinner();
        _securityIcon.Data = (StreamGeometry)_resourceHost.FindResource("IconRefresh");
        _securityIcon.Stroke = (Brush)_resourceHost.FindResource("Accent");
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
