using System;
using System.Windows;
using System.Windows.Controls;

namespace UmberBrowser.Services.UI;

/// <summary>Reserves room for the new tab button and places it after the visible tabs.</summary>
public sealed class TabStripLayout : Panel
{
    private static readonly DependencyPropertyKey AvailableTabWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AvailableTabWidth), typeof(double), typeof(TabStripLayout),
            new PropertyMetadata(0.0));
    public static readonly DependencyProperty AvailableTabWidthProperty = AvailableTabWidthPropertyKey.DependencyProperty;
    public double AvailableTabWidth => (double)GetValue(AvailableTabWidthProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count != 2) return new Size();
        var button = InternalChildren[1];
        button.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var available = Math.Max(0, availableSize.Width - button.DesiredSize.Width);
        SetValue(AvailableTabWidthPropertyKey, available);
        InternalChildren[0].Measure(new Size(available, availableSize.Height));
        // Claim the full width handed down. Reporting a smaller desired size makes
        // the owning DockPanel leave the remainder as dead space beside the strip.
        return new Size(availableSize.Width,
            Math.Max(InternalChildren[0].DesiredSize.Height, button.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count != 2) return finalSize;
        var buttonWidth = Math.Min(InternalChildren[1].DesiredSize.Width, finalSize.Width);
        // The list takes every DIP the toolbar handed over, so the cards expand
        // into the space instead of leaving a dead gap after the plus.
        var tabWidth = Math.Max(0, finalSize.Width - buttonWidth);
        InternalChildren[0].Arrange(new Rect(0, 0, tabWidth, finalSize.Height));
        InternalChildren[1].Arrange(new Rect(tabWidth, 0, buttonWidth, finalSize.Height));
        return finalSize;
    }
}
