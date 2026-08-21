using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StrideBrowser.Controls;

/// <summary>
/// A StackPanel that animates children to their new positions when layout changes
/// (tab reorder, add, remove). Preserves the spatial cognitive model - position
/// is the identifier on an icon-only rail, so teleporting without feedback breaks
/// the user's mental map.
/// </summary>
public class AnimatedStackPanel : Panel
{
    private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(150));
    private static readonly CubicEase Ease = new() { EasingMode = EasingMode.EaseOut };

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(AnimatedStackPanel),
            new FrameworkPropertyMetadata(Orientation.Vertical, FrameworkPropertyMetadataOptions.AffectsArrange));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly DependencyProperty IsAnimationEnabledProperty =
        DependencyProperty.Register(nameof(IsAnimationEnabled), typeof(bool), typeof(AnimatedStackPanel),
            new PropertyMetadata(true));

    public bool IsAnimationEnabled
    {
        get => (bool)GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    private readonly Dictionary<UIElement, Point> _previousPositions = new();

    protected override Size MeasureOverride(Size availableSize)
    {
        double totalWidth = 0, totalHeight = 0;
        double maxWidth = 0, maxHeight = 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
            var desired = child.DesiredSize;

            if (Orientation == Orientation.Vertical)
            {
                totalHeight += desired.Height;
                maxWidth = Math.Max(maxWidth, desired.Width);
            }
            else
            {
                totalWidth += desired.Width;
                maxHeight = Math.Max(maxHeight, desired.Height);
            }
        }

        return Orientation == Orientation.Vertical
            ? new Size(maxWidth, totalHeight)
            : new Size(totalWidth, maxHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double offset = 0;

        foreach (UIElement child in InternalChildren)
        {
            var desired = child.DesiredSize;
            Point targetPos;
            Rect arrangeRect;

            if (Orientation == Orientation.Vertical)
            {
                targetPos = new Point(0, offset);
                arrangeRect = new Rect(0, offset, finalSize.Width, desired.Height);
                offset += desired.Height;
            }
            else
            {
                targetPos = new Point(offset, 0);
                arrangeRect = new Rect(offset, 0, desired.Width, finalSize.Height);
                offset += desired.Width;
            }

            child.Arrange(arrangeRect);

            if (IsAnimationEnabled && _previousPositions.TryGetValue(child, out var prevPos) && prevPos != targetPos)
            {
                AnimateToPosition(child, prevPos, targetPos);
            }

            _previousPositions[child] = targetPos;
        }

        var current = new HashSet<UIElement>();
        foreach (UIElement child in InternalChildren)
            current.Add(child);

        var stale = _previousPositions.Keys.Where(k => !current.Contains(k)).ToList();
        foreach (var k in stale)
            _previousPositions.Remove(k);

        return finalSize;
    }

    private void AnimateToPosition(UIElement child, Point from, Point to)
    {
        double deltaX = from.X - to.X;
        double deltaY = from.Y - to.Y;

        if (Math.Abs(deltaX) < 1 && Math.Abs(deltaY) < 1) return;

        var transform = child.RenderTransform as TranslateTransform;
        if (transform is null)
        {
            transform = new TranslateTransform();
            child.RenderTransform = transform;
        }

        if (Orientation == Orientation.Vertical)
        {
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = deltaY;
            var anim = new DoubleAnimation(deltaY, 0, AnimDuration) { EasingFunction = Ease };
            transform.BeginAnimation(TranslateTransform.YProperty, anim);
        }
        else
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = deltaX;
            var anim = new DoubleAnimation(deltaX, 0, AnimDuration) { EasingFunction = Ease };
            transform.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }
}

