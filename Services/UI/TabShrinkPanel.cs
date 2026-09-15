using System;
using System.Windows;
using System.Windows.Controls;

namespace StrideBrowser.Services.UI;

/// <summary>
/// Horizontal tab strip panel. When shrinking is enabled, every tab gets an
/// equal share of the strip capped at a maximum width, so all tabs always fit
/// the viewport: opening another tab shrinks every card, never scrolls. When
/// disabled it stacks children at their desired sizes like a StackPanel.
/// </summary>
public sealed class TabShrinkPanel : Panel
{
    public const double MaxTabWidth = 190;

    public static readonly DependencyProperty ShrinkEnabledProperty =
        DependencyProperty.RegisterAttached("ShrinkEnabled", typeof(bool),
            typeof(TabShrinkPanel), new FrameworkPropertyMetadata(false,
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static bool GetShrinkEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(ShrinkEnabledProperty);

    public static void SetShrinkEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(ShrinkEnabledProperty, value);

    /// <summary>
    /// Equal width per tab for the available strip width and tab count.
    /// No minimum: the cards keep shrinking so every tab stays visible.
    /// Pure logic, covered by unit tests.
    /// </summary>
    public static double ComputeTabWidth(double availableWidth, int count, double maxWidth)
    {
        if (count <= 0 || double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
            return maxWidth;
        return Math.Min(availableWidth / count, maxWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var shrink = GetShrinkEnabled(this);
        var count = InternalChildren.Count;
        if (count == 0) return new Size(0, 0);

        if (!shrink)
        {
            var width = 0.0;
            var height = 0.0;
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                width += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }
            return new Size(width, height);
        }

        var tabWidth = ComputeTabWidth(availableSize.Width, count, MaxTabWidth);
        var childHeight = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(tabWidth, availableSize.Height));
            childHeight = Math.Max(childHeight, child.DesiredSize.Height);
        }
        return new Size(tabWidth * count, childHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var shrink = GetShrinkEnabled(this);
        if (!shrink)
        {
            var x = 0.0;
            foreach (UIElement child in InternalChildren)
            {
                child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
                x += child.DesiredSize.Width;
            }
            return finalSize;
        }

        var count = InternalChildren.Count;
        var tabWidth = ComputeTabWidth(finalSize.Width, count, MaxTabWidth);
        var xPos = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(xPos, 0, tabWidth, finalSize.Height));
            xPos += tabWidth;
        }
        return finalSize;
    }
}
