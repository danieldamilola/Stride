using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StrideBrowser.Models;

namespace StrideBrowser.Services.UI;

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

    public static readonly DependencyProperty ShowTabNamesProperty =
        DependencyProperty.Register(nameof(ShowTabNames), typeof(bool), typeof(TabShrinkPanel),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public bool ShowTabNames { get => (bool)GetValue(ShowTabNamesProperty); set => SetValue(ShowTabNamesProperty, value); }

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
    public bool IsDragging { get; set; }
    public Rect GetTargetBounds(UIElement child) =>
        _targets.TryGetValue(child, out var bounds) ? bounds : System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(child as FrameworkElement);

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
    /// keep their title until the strip can no longer give every tab <see cref="MinNamedTabWidth"/>.
    /// Past that they go favicon only, and the strip scrolls once even favicons overflow.
    /// </summary>
    public static double[] ComputeWidths(double viewport, IReadOnlyList<bool> pinned, int activeIndex, bool showNames)
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
        // ramp down continuously as tabs open. Icon-only mode pins them to the
        // favicon pill; named mode lets them shrink toward it.
        var rest = Math.Max(0, unpinnedCount - 1);
        var activeWidth = active
            ? Math.Clamp(available - rest * IconTabWidth, IconTabWidth, MaxTabWidth)
            : 0;
        var others = active ? rest : unpinnedCount;
        var width = others > 0
            ? Math.Clamp((available - activeWidth) / others, IconTabWidth, showNames ? MaxTabWidth : IconTabWidth)
            : activeWidth;

        for (var i = 0; i < total; i++)
            widths[i] = pinned[i] ? IconTabWidth : i == activeIndex ? activeWidth : width;
        return widths;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
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
        _widths = ComputeWidths(ViewportWidth > 0 ? ViewportWidth : double.IsFinite(availableSize.Width) ? availableSize.Width : 0, pinned, active, ShowTabNames);
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
        var x = 0.0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var width = i < _widths.Length ? _widths[i] : child.DesiredSize.Width;
            var target = new Rect(x, 0, width, finalSize.Height);
            if (!_targets.TryGetValue(child, out var previous) || previous != target)
            {
                var current = (Rect)child.GetValue(AnimatedBoundsProperty);
                child.BeginAnimation(AnimatedBoundsProperty, null);
                child.SetValue(AnimatedBoundsProperty, target);
                if (!current.IsEmpty && !IsDragging && SystemParameters.ClientAreaAnimation)
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
