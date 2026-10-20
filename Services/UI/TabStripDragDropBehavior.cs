using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StrideBrowser.Engine;
using StrideBrowser.Models;

namespace StrideBrowser.Services.UI;

/// <summary>
/// Moves a tab with the pointer and commits its new slot on release. Reordering runs on
/// mouse capture rather than the OLE drag session, because a capture drag on one strip can
/// never be a drop target for itself, so an OLE session would silently drop every reorder.
/// Cross-window docking and tearing off are decided on release from the captured pointer.
/// </summary>
public sealed class TabStripDragDropBehavior : IDisposable
{
    private readonly ListBox _listBox;
    private readonly Action<int, int> _moveTab;
    private readonly Action? _onDragStarted;

    /// <summary>
    /// The card border inside the tab item's template. Its Background is set directly during
    /// a drag because the template's IsSelected trigger outranks the item's own Background,
    /// so assigning ListBoxItem.Background is overwritten the moment the drag starts.
    /// </summary>
    private static Border? CardBorder(ListBoxItem item) =>
        item.Template.FindName("Bd", item) as Border;

    /// <summary>
    /// An inactive tab is see-through, so a dragged one would show the page behind it.
    /// Reuse the brush hover already paints on, falling back to the drag card colour.
    /// </summary>
    private static Brush OpaqueCardBrush() =>
        Application.Current?.TryFindResource("SurfaceHoverBrush") as SolidColorBrush
        ?? new SolidColorBrush(Color.FromArgb(235, 42, 42, 46));

    private BrowserTab? _pressedTab;
    private ListBoxItem? _container;
    private TabShrinkPanel? _panel;
    private int _originalZIndex;
    private Brush? _originalBackground;
    private Border? _cardBorder;
    private Point _pressedPoint;
    private double _grip;
    private double _width;
    private int _target;
    private int _oldIndex = -1;
    private bool _dragging;
    private bool _ending;
    private Rect[] _slots = Array.Empty<Rect>();

    public bool IsDragging => _dragging;

    /// <summary>Set by the window that owns this strip. Null disables tearing off.</summary>
    public TabTransferHost? Transfer { get; set; }

    public TabStripDragDropBehavior(ListBox listBox, Action<int, int> moveTab, Action? onDragStarted = null)
    {
        _listBox = listBox;
        _moveTab = moveTab;
        _onDragStarted = onDragStarted;
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
            // Selecting rebuilds the containers, so the press would be lost. Capture first.
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
            _oldIndex = _listBox.Items.IndexOf(_pressedTab);
            if (_oldIndex < 0) { End(false); return; }
            _target = _oldIndex;
            _width = _slots[_target].Width;
            _grip = Math.Clamp(_grip, 0, _width);
            Panel.SetZIndex(_container, int.MaxValue);
            _originalBackground = _container.Background;
            _container.Background = OpaqueCardBrush();
            // The template's IsSelected trigger repaints Bd over the item Background, so set
            // the border itself as well. Its local value outranks the trigger.
            _cardBorder = CardBorder(_container);
            if (_cardBorder is not null) _cardBorder.Background = OpaqueCardBrush();
            _dragging = true;
        }
        Update(point);
        UpdateEdgeScroll(point.X);
        e.Handled = true;
    }

    /// <summary>Live reorder feedback. Follows the pointer and slides neighbours aside.</summary>
    private void Update(Point point)
    {
        var scroll = TabStripController.GetScrollViewer(_listBox);
        var offset = scroll?.HorizontalOffset ?? 0;
        var oldIndex = _oldIndex;
        if (oldIndex < 0 || oldIndex >= _slots.Length || _listBox.Items.Count != _slots.Length) return;
        var pinnedCount = _listBox.Items.Cast<BrowserTab>().Count(t => t.IsPinned);
        var min = _pressedTab!.IsPinned ? 0 : pinnedCount;
        var max = _pressedTab.IsPinned ? pinnedCount - 1 : _slots.Length - 1;
        var left = Math.Clamp(point.X + offset - _grip, _slots[min].Left,
            Math.Max(_slots[min].Left, _slots[max].Right - _width));
        if (_container is not null)
        {
            var draggedTransform = _container.RenderTransform as TranslateTransform ?? new TranslateTransform();
            _container.RenderTransform = draggedTransform;
            draggedTransform.X = left - _slots[oldIndex].Left;
        }
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
    }

    private void UpdateEdgeScroll(double x)
    {
        var scroll = TabStripController.GetScrollViewer(_listBox);
        if (scroll == null || scroll.ScrollableWidth <= 0) return;
        var step = ComputeEdgeScroll(x, _listBox.ActualWidth);
        if (step == 0) return;
        scroll.ScrollToHorizontalOffset(Math.Clamp(scroll.HorizontalOffset + step, 0, scroll.ScrollableWidth));
        _listBox.UpdateLayout();
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
        var tab = _pressedTab;
        var wasDragging = _dragging;
        var screen = TabDragGeometry.CursorPosition();
        var oldIndex = _oldIndex;
        var target = _target;
        var transfer = Transfer;
        var strip = new Rect(_listBox.RenderSize);
        var local = _listBox.PointFromScreen(screen);

        End(false);

        if (!hadPress) return;
        e.Handled = true;
        if (!wasDragging || tab is null || transfer is null) return;

        // Released over this strip: reorder. Released over another window's strip: dock
        // there. Released anywhere else: tear the tab off.
        if (strip.Contains(local))
        {
            if (oldIndex >= 0 && target >= 0 && oldIndex != target)
            {
                _moveTab(oldIndex, target);
                _listBox.UpdateLayout();
            }
            return;
        }

        var foreign = transfer.StripUnderCursor(screen);
        if (foreign is not null)
        {
            var centers = new List<double>();
            foreach (var item in foreign.Items)
            {
                if (foreign.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container) return;
                centers.Add(container.TranslatePoint(new Point(container.ActualWidth / 2, 0), foreign).X);
            }
            var index = TabDragGeometry.InsertionIndex(centers, foreign.PointFromScreen(screen).X);
            _ = transfer.DockAt(foreign, tab, index, screen);
            return;
        }

        if (transfer.Owns(tab) && TabDragGeometry.ShouldDetach(strip, local, false, false))
            _ = transfer.Detach(tab, screen);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _dragging) { End(false); e.Handled = true; }
    }

    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        // Selecting a tab rebuilds the containers and WPF drops the capture it granted us.
        // That is not a cancelled drag, so only end the press if the button is really up.
        if (_pressedTab != null && !_ending && !_dragging && e.LeftButton != MouseButtonState.Pressed) End(false);
    }

    public void Cancel() => End(false);

    private void End(bool commit)
    {
        if (_ending) return;
        _ending = true;
        try
        {
            if (_container != null)
            {
                Panel.SetZIndex(_container, _originalZIndex);
                _container.Background = _originalBackground ?? Brushes.Transparent;
            }
            if (_cardBorder is not null) _cardBorder.ClearValue(Border.BackgroundProperty);
            _cardBorder = null;
            _originalBackground = null;
            foreach (var item in _listBox.Items)
                if (_listBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem c)
                    c.ClearValue(UIElement.RenderTransformProperty);
            _dragging = false;
            _pressedTab = null;
            _container = null;
            _oldIndex = -1;
            _target = -1;
            if (_listBox.IsMouseCaptured) _listBox.ReleaseMouseCapture();
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
    }
}