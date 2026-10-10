using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StrideBrowser.Models;

namespace StrideBrowser.Services.UI;

/// <summary>Moves a captured tab with the pointer and commits its new slot on release.</summary>
public sealed class TabStripDragDropBehavior : IDisposable
{
    private readonly ListBox _listBox;
    private readonly Action<int, int> _moveTab;
    private readonly DispatcherTimer _scrollTimer;
    private readonly Action? _onDragStarted;
    private BrowserTab? _pressedTab;
    private ListBoxItem? _container;
    private TabShrinkPanel? _panel;
    private int _originalZIndex;
    private Point _pressedPoint;
    private double _grip;
    private double _width;
    private int _target;
    private bool _dragging;
    private bool _ending;
    private double _scrollStep;
    private Rect[] _slots = Array.Empty<Rect>();
    public bool IsDragging => _dragging;

    public TabStripDragDropBehavior(ListBox listBox, Action<int, int> moveTab, Action? onDragStarted = null)
    {
        _listBox = listBox;
        _moveTab = moveTab;
        _onDragStarted = onDragStarted;
        _scrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, OnScrollTick, listBox.Dispatcher);
        _scrollTimer.Stop();
        listBox.PreviewMouseLeftButtonDown += OnDown;
        listBox.PreviewMouseMove += OnMove;
        listBox.PreviewMouseLeftButtonUp += OnUp;
        listBox.PreviewKeyDown += OnKeyDown;
        listBox.LostMouseCapture += OnLostCapture;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        End(false);
        if (IsButton(e.OriginalSource as DependencyObject)) return;
        _container = ItemsControl.ContainerFromElement(_listBox, e.OriginalSource as DependencyObject) as ListBoxItem;
        _pressedTab = _container?.DataContext as BrowserTab;
        _pressedPoint = e.GetPosition(_listBox);
        if (_container != null)
        {
            _grip = e.GetPosition(_container).X;
            _originalZIndex = Panel.GetZIndex(_container);
            _panel = TabStripController.FindVisualChild<TabShrinkPanel>(_listBox);
            _panel?.HoldWidths();
            e.Handled = true;
            _listBox.Focus();
            _listBox.SelectedItem = _pressedTab;
            if (!_listBox.CaptureMouse()) End(false);
        }
    }

    public static bool IsButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase) return true;
            source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_pressedTab == null || _container == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { End(false); return; }
        e.Handled = true;
        var point = e.GetPosition(_listBox);
        if (!_dragging)
        {
            if (Math.Abs(point.X - _pressedPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _pressedPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _panel = TabStripController.FindVisualChild<TabShrinkPanel>(_listBox);
            _onDragStarted?.Invoke();
            if (_panel != null) { _panel.IsDragging = true; _panel.InvalidateArrange(); }
            _listBox.UpdateLayout();
            _slots = _listBox.Items.Cast<object>().Select(item =>
                _listBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem c
                    ? LayoutInformation.GetLayoutSlot(c) : Rect.Empty).ToArray();
            if (_slots.Any(r => r.IsEmpty)) { End(false); return; }
            _target = _listBox.Items.IndexOf(_pressedTab);
            _width = _slots[_target].Width;
            _grip = Math.Clamp(_grip, 0, _width);
            Panel.SetZIndex(_container, int.MaxValue);
            _dragging = true;
            if (!_listBox.CaptureMouse()) { End(false); return; }
        }
        Update(point);
        e.Handled = true;
    }

    private void Update(Point point)
    {
        var scroll = TabStripController.GetScrollViewer(_listBox);
        var offset = scroll?.HorizontalOffset ?? 0;
        var oldIndex = _listBox.Items.IndexOf(_pressedTab);
        if (oldIndex < 0 || oldIndex >= _slots.Length || _listBox.Items.Count != _slots.Length) { End(false); return; }
        var pinnedCount = _listBox.Items.Cast<BrowserTab>().Count(t => t.IsPinned);
        var min = _pressedTab!.IsPinned ? 0 : pinnedCount;
        var max = _pressedTab.IsPinned ? pinnedCount - 1 : _slots.Length - 1;
        var left = Math.Clamp(point.X + offset - _grip, _slots[min].Left,
            Math.Max(_slots[min].Left, _slots[max].Right - _width));
        var draggedTransform = _container!.RenderTransform as TranslateTransform ?? new TranslateTransform();
        _container.RenderTransform = draggedTransform;
        draggedTransform.X = left - _slots[oldIndex].Left;
        var next = ComputeTargetIndex(_slots, oldIndex, left, min, max);
        if (next != _target)
        {
            _target = next;
            for (var i = 0; i < _slots.Length; i++)
            {
                if (i == oldIndex) continue;
                if (_listBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem c) continue;
                var shift = next > oldIndex && i > oldIndex && i <= next ? -(_width + TabShrinkPanel.TabGap)
                    : next < oldIndex && i >= next && i < oldIndex ? _width + TabShrinkPanel.TabGap : 0;
                AnimateShift(c, shift);
            }
        }
        _scrollStep = ComputeEdgeScroll(point.X, _listBox.ActualWidth);
        if (_scrollStep != 0 && scroll?.ScrollableWidth > 0) _scrollTimer.Start();
        else _scrollTimer.Stop();
    }

    public static int ComputeTargetIndex(IReadOnlyList<Rect> slots, int oldIndex, double left, int min, int max)
    {
        if (slots.Count == 0) return 0;
        var target = Math.Clamp(oldIndex, 0, slots.Count - 1);
        if (left > slots[target].Left)
        {
            var right = left + slots[target].Width;
            while (target < slots.Count - 1 && right > slots[target + 1].Left + slots[target + 1].Width / 2) target++;
        }
        else
        {
            while (target > 0 && left < slots[target - 1].Left + slots[target - 1].Width / 2) target--;
        }
        return Math.Clamp(target, Math.Clamp(min, 0, slots.Count - 1), Math.Clamp(max, Math.Clamp(min, 0, slots.Count - 1), slots.Count - 1));
    }

    public static double ComputeEdgeScroll(double x, double viewport)
    {
        if (viewport <= 0 || x < 0 || x > viewport) return 0;
        var edge = Math.Min(32, viewport / 2);
        if (x < edge) return -12 * (1 - x / edge);
        if (x > viewport - edge) return 12 * (1 - (viewport - x) / edge);
        return 0;
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        if (!_dragging) { _scrollTimer.Stop(); return; }
        var scroll = TabStripController.GetScrollViewer(_listBox);
        if (scroll == null) return;
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + _scrollStep);
        _listBox.UpdateLayout();
        Update(Mouse.GetPosition(_listBox));
    }

    private static void AnimateShift(UIElement element, double target)
    {
        var transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        var current = transform.X;
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = target;
        if (SystemParameters.ClientAreaAnimation)
            transform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(120))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                });
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        var hadPress = _pressedTab != null;
        End(true);
        if (hadPress) e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _dragging) { End(false); e.Handled = true; }
    }

    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (_pressedTab != null && !_ending) End(false);
    }

    public void Cancel() => End(false);

    private void End(bool commit)
    {
        if (_ending) return;
        _ending = true;
        try
        {
            var old = _pressedTab == null ? -1 : _listBox.Items.IndexOf(_pressedTab);
            var target = _target;
            var dragging = _dragging;
            _scrollTimer.Stop();
            if (_container != null) Panel.SetZIndex(_container, _originalZIndex);
            foreach (var item in _listBox.Items)
                if (_listBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem c)
                    c.ClearValue(UIElement.RenderTransformProperty);
            _dragging = false;
            _pressedTab = null;
            _container = null;
            if (_listBox.IsMouseCaptured) _listBox.ReleaseMouseCapture();
            if (commit && dragging && old >= 0 && target >= 0 && target < _listBox.Items.Count && old != target)
            {
                _moveTab(old, target);
                _listBox.UpdateLayout();
            }
            if (_panel != null)
            {
                _panel.IsDragging = false;
                _panel.ReleaseWidths();
                _panel.InvalidateMeasure();
                _panel = null;
            }
        }
        finally { _ending = false; }
    }

    public void Dispose()
    {
        End(false);
        _listBox.PreviewMouseLeftButtonDown -= OnDown;
        _listBox.PreviewMouseMove -= OnMove;
        _listBox.PreviewMouseLeftButtonUp -= OnUp;
        _listBox.PreviewKeyDown -= OnKeyDown;
        _listBox.LostMouseCapture -= OnLostCapture;
        _scrollTimer.Tick -= OnScrollTick;
    }
}
