using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using UmberBrowser.Models;

namespace UmberBrowser.Services.UI;

/// <summary>Sizes tabs for the viewport and scrolls once their minimum widths no longer fit.</summary>
public sealed class TabShrinkPanel : Panel
{
    public const double MaxTabWidth = 160;
    public const double IconTabWidth = 32;
    public const double MinNamedTabWidth = 48;
    public const double TabGap = 4;

    public static readonly DependencyProperty ShrinkEnabledProperty =
        DependencyProperty.RegisterAttached("ShrinkEnabled", typeof(bool), typeof(TabShrinkPanel),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static bool GetShrinkEnabled(DependencyObject obj) => (bool)obj.GetValue(ShrinkEnabledProperty);
    public static void SetShrinkEnabled(DependencyObject obj, bool value) => obj.SetValue(ShrinkEnabledProperty, value);

    public static readonly DependencyProperty ViewportWidthProperty =
        DependencyProperty.Register(nameof(ViewportWidth), typeof(double), typeof(TabShrinkPanel),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }

    private static readonly DependencyProperty AnimatedBoundsProperty =
        DependencyProperty.RegisterAttached("AnimatedBounds", typeof(Rect), typeof(TabShrinkPanel),
            new PropertyMetadata(Rect.Empty, (d, _) =>
            {
                if (VisualTreeHelper.GetParent(d) is TabShrinkPanel panel) panel.InvalidateArrange();
            }));

    private readonly Dictionary<UIElement, Rect> _targets = new();
    private readonly Dictionary<UIElement, double> _heldWidths = new();
    private double[] _widths = Array.Empty<double>();
    private double? _heldActiveWidth;
    private double _measuredViewport = double.NaN;
    private double _arrangedWidth = double.NaN;
    private double _arrangedViewport = double.NaN;
    public bool IsDragging { get; set; }
    public Rect GetTargetBounds(UIElement child) =>
        _targets.TryGetValue(child, out var bounds) ? bounds : System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(child as FrameworkElement);

    /// <summary>
    /// Whether a card may slide to its new bounds. A tab action keeps the viewport, so a card
    /// that moved because a sibling opened, closed or reordered animates there. A viewport
    /// change is a window resize: every card retargets on that pass, and animating each one
    /// makes the strip chase the pointer for as long as the animation runs, so the resize
    /// snaps instead. The arrangement decides this; the invariant is pinned here for tests.
    /// </summary>
    public static bool ShouldAnimate(bool viewportChanged, bool hasCurrent, bool dragging, bool animationsEnabled) =>
        !viewportChanged && hasCurrent && !dragging && animationsEnabled;

    private static bool Changed(double value, double previous) =>
        double.IsNaN(previous) || Math.Abs(value - previous) > 0.01;

    // Keep the close button under the pointer when several neighboring tabs are closed.
    public void HoldWidths(BrowserTab? closing = null)
    {
        foreach (UIElement child in InternalChildren)
            if (_targets.TryGetValue(child, out var bounds))
            {
                _heldWidths[child] = bounds.Width;
                if (child is FrameworkElement { DataContext: BrowserTab tab } && tab == closing && tab.IsActive)
                    _heldActiveWidth = bounds.Width;
            }
    }

    public void ReleaseWidths()
    {
        if (_heldWidths.Count == 0) return;
        _heldWidths.Clear();
        _heldActiveWidth = null;
        InvalidateMeasure();
    }

    /// <summary>
    /// Splits the strip width across every tab. Tabs shrink from <see cref="MaxTabWidth"/> down and
    /// each inactive tab keeps its title while the strip can still give it a readable card width.
    /// Past that they ramp down to the favicon width, and the strip scrolls once even favicons overflow.
    /// </summary>
    public static double[] ComputeWidths(double viewport, IReadOnlyList<bool> pinned, int activeIndex)
    {
        var widths = new double[pinned.Count];
        var total = pinned.Count;
        if (total == 0) return widths;

        var pinnedCount = 0;
        for (var i = 0; i < total; i++)
            if (pinned[i]) pinnedCount++;
        var unpinnedCount = total - pinnedCount;
        var active = activeIndex >= 0 && activeIndex < total && !pinned[activeIndex];
        if (!double.IsFinite(viewport)) viewport = MaxTabWidth * total;
        viewport = Math.Max(0, viewport);
        var available = Math.Max(0, viewport - pinnedCount * IconTabWidth - Math.Max(0, total - 1) * TabGap);
        if (unpinnedCount <= 0)
        {
            for (var i = 0; i < total; i++) widths[i] = IconTabWidth;
            return widths;
        }

        // The rest of the tabs split whatever the selected one leaves behind and
        // ramp down continuously as tabs open, from a full card down to the
        // favicon pill. There is no separate icon-only display mode.
        var rest = Math.Max(0, unpinnedCount - 1);
        var activeWidth = active
            ? Math.Clamp(available - rest * IconTabWidth, IconTabWidth, MaxTabWidth)
            : 0;
        var others = active ? rest : unpinnedCount;
        var width = others > 0
            ? Math.Clamp((available - activeWidth) / others, IconTabWidth, MaxTabWidth)
            : activeWidth;

        for (var i = 0; i < total; i++)
            widths[i] = pinned[i] ? IconTabWidth : i == activeIndex ? activeWidth : width;
        return widths;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var viewport = ViewportWidth > 0 ? ViewportWidth : double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        // A held width keeps the close button under the pointer for the length of a close,
        // which only means anything while the viewport is the one the hold was taken in. A
        // resize is a new viewport, so the hold is dropped rather than pinning an old width.
        if (Changed(viewport, _measuredViewport))
        {
            _heldWidths.Clear();
            _heldActiveWidth = null;
        }
        _measuredViewport = viewport;

        var pinned = new bool[InternalChildren.Count];
        var active = -1;
        for (var i = 0; i < pinned.Length; i++)
        {
            if (InternalChildren[i] is FrameworkElement { DataContext: BrowserTab tab })
            {
                pinned[i] = tab.IsPinned;
                if (tab.IsActive) active = i;
            }
        }
        _widths = ComputeWidths(viewport, pinned, active);
        var height = 0.0;
        var total = 0.0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            if (_heldWidths.TryGetValue(child, out var held)) _widths[i] = held;
            if (_heldActiveWidth != null && child is FrameworkElement { DataContext: BrowserTab { IsActive: true, IsPinned: false } })
                _widths[i] = _heldActiveWidth.Value;
            if (!GetShrinkEnabled(this))
            {
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                _widths[i] = child.DesiredSize.Width;
            }
            else child.Measure(new Size(_widths[i], availableSize.Height));
            total += _widths[i];
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(total + Math.Max(0, InternalChildren.Count - 1) * TabGap, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // A viewport change is a resize. Every card retargets on this pass, so the whole strip
        // is placed on the new bounds here instead of sliding there: an animated card would
        // otherwise chase the pointer for the length of the animation while the window drags.
        // Every card loses its clock, not just the ones whose target moved, because a card
        // still animating toward an old target would keep chasing it.
        var viewportChanged = Changed(finalSize.Width, _arrangedWidth) | Changed(ViewportWidth, _arrangedViewport);
        _arrangedWidth = finalSize.Width;
        _arrangedViewport = ViewportWidth;
        var animationsOn = SystemParameters.ClientAreaAnimation;

        var x = 0.0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var width = i < _widths.Length ? _widths[i] : child.DesiredSize.Width;
            var target = new Rect(x, 0, width, finalSize.Height);
            if (viewportChanged || !_targets.TryGetValue(child, out var previous) || previous != target)
            {
                var current = (Rect)child.GetValue(AnimatedBoundsProperty);
                child.BeginAnimation(AnimatedBoundsProperty, null);
                child.SetValue(AnimatedBoundsProperty, target);
                if (ShouldAnimate(viewportChanged, !current.IsEmpty, IsDragging, animationsOn))
                    child.BeginAnimation(AnimatedBoundsProperty, new RectAnimation(current, target, TimeSpan.FromMilliseconds(160))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                        FillBehavior = FillBehavior.Stop
                    });
                _targets[child] = target;
            }
            child.Arrange(IsDragging ? target : (Rect)child.GetValue(AnimatedBoundsProperty));
            x += width + TabGap;
        }
        foreach (var child in _targets.Keys.Where(c => !InternalChildren.Contains(c)).ToArray())
        {
            _targets.Remove(child);
            _heldWidths.Remove(child);
        }
        return finalSize;
    }
}
