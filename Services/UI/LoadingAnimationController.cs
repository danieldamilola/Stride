using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StrideBrowser.Services.UI;

/// <summary>
/// Controls the indeterminate loading animation sweep on the address bar.
/// </summary>
public sealed class LoadingAnimationController
{
    private const double LoadingIndicatorWidth = 100;
    private const double LoadingOvershootPx = 20;
    private const double FallbackContainerWidth = 900;
    private const double SweepDurationSeconds = 1.2;

    private readonly UIElement _loadingIndicator;
    private readonly FrameworkElement _loadingBar;
    private Storyboard? _loadingStoryboard;

    public LoadingAnimationController(UIElement loadingIndicator, FrameworkElement loadingBar)
    {
        _loadingIndicator = loadingIndicator;
        _loadingBar = loadingBar;
    }

    public void Start()
    {
        Stop();

        var transform = new TranslateTransform();
        _loadingIndicator.RenderTransform = transform;

        var containerWidth = _loadingBar.ActualWidth > 0 ? _loadingBar.ActualWidth : FallbackContainerWidth;
        var anim = new DoubleAnimation
        {
            From = -LoadingIndicatorWidth,
            To = containerWidth + LoadingOvershootPx,
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

    public void Stop()
    {
        _loadingStoryboard?.Stop();
        _loadingStoryboard = null;
    }
}
