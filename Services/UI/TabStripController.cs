using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UmberBrowser.Engine;
using UmberBrowser.Models;

namespace UmberBrowser.Services.UI;

/// <summary>
/// Manages the tab strip UI: selection changes, mouse-wheel scrolling,
/// context menu interactions (pin/duplicate/close), tab cycling, and index navigation.
/// </summary>
public sealed class TabStripController : IDisposable
{
    private readonly ListBox _tabList;
    private readonly TabEngine _engine;
    private readonly Dispatcher _dispatcher;
    private readonly Action _onNewTabOpened;
    private readonly DispatcherTimer _scrollTimer;
    private ScrollViewer? _scrollViewer;
    private long _scrollStarted;
    private double _scrollFrom;
    private double _scrollTarget;
    private readonly TabStripDragDropBehavior _drag;
    private readonly FrameworkElement _layout;

    /// <summary>Prevents re-entrant tab selection changes when programmatically updating the ListBox selection.</summary>
    public bool IsUpdatingSelection { get; set; }

    /// <summary>The drag behavior this controller owns, so the window can hand it a transfer target.</summary>
    public TabStripDragDropBehavior Drag => _drag;

    public TabStripController(
        ListBox tabList,
        TabEngine engine,
        Dispatcher dispatcher,
        Action onNewTabOpened)
    {
        _tabList = tabList;
        _engine = engine;
        _dispatcher = dispatcher;
        _onNewTabOpened = onNewTabOpened;
        _scrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, OnScrollTick, dispatcher);
        _scrollTimer.Stop();
        _drag = new TabStripDragDropBehavior(tabList, (oldIndex, newIndex) =>
        {
            IsUpdatingSelection = true;
            try
            {
                _engine.Tabs.Move(oldIndex, newIndex);
                _tabList.SelectedItem = _engine.ActiveTab;
            }
            finally { IsUpdatingSelection = false; }
            if (_engine.ActiveTab != null) EnsureVisible(_engine.ActiveTab);
        }, () => _scrollTimer.Stop());
        tabList.PreviewMouseDown += OnMouseDown;
        tabList.PreviewMouseUp += OnMouseUp;
        tabList.MouseRightButtonUp += OnRightButtonUp;
        tabList.MouseLeave += OnMouseLeave;
        _layout = tabList.Parent as FrameworkElement ?? tabList;
        _layout.SizeChanged += OnSizeChanged;
        tabList.RequestBringIntoView += OnRequestBringIntoView;
        tabList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        engine.ActiveTabChanged += OnActiveTabChanged;
        engine.TabCreated += OnTabCreated;
    }

    public void SyncTabsBinding()
    {
        _tabList.ItemsSource = _engine.Tabs;
    }

    public void HandleTabSelection(BrowserTab tab)
    {
        IsUpdatingSelection = true;
        try
        {
            if (_tabList.SelectedItem != tab)
                _tabList.SelectedItem = tab;
        }
        finally
        {
            IsUpdatingSelection = false;
        }
    }

    public void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsUpdatingSelection) return;

        var listBox = (ListBox)sender;
        if (listBox.SelectedItem is not BrowserTab tab || tab == _engine.ActiveTab) return;

        IsUpdatingSelection = true;
        try
        {
            _engine.SwitchTo(tab);
        }
        finally
        {
            IsUpdatingSelection = false;
        }

        _dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await _engine.ActivateAsync(tab);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"TabList selection activation failed: {ex}");
            }
        }, DispatcherPriority.Loaded);
    }

    public void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollWheel(e.Delta);
        e.Handled = true;
    }

    public void ScrollWheel(int delta)
    {
        var scroll = GetScrollViewer(_tabList);
        if (scroll != null) ScrollBy(-delta / 120.0 * Math.Clamp(scroll.ViewportWidth * 0.5, 72, 240));
    }

    public void ApplyLayout()
    {
        var panel = FindVisualChild<TabShrinkPanel>(_tabList);
        panel?.ReleaseWidths();
        panel?.InvalidateMeasure();
        if (_engine.ActiveTab != null) EnsureVisible(_engine.ActiveTab);
    }

    public bool CancelDrag()
    {
        if (!_drag.IsDragging) return false;
        _drag.Cancel();
        return true;
    }

    public void ScrollBy(double delta)
    {
        var scroll = GetScrollViewer(_tabList);
        if (scroll == null || scroll.ScrollableWidth <= 0) return;
        var start = _scrollTimer.IsEnabled ? _scrollTarget : scroll.HorizontalOffset;
        AnimateScroll(scroll, start + delta, Math.Abs(delta) >= 40);
    }


    private void AnimateScroll(ScrollViewer scroll, double target, bool animate = true)
    {
        target = Math.Clamp(target, 0, scroll.ScrollableWidth);
        if (_scrollTimer.IsEnabled && ReferenceEquals(scroll, _scrollViewer) && Math.Abs(target - _scrollTarget) < 0.5) return;
        var current = scroll.HorizontalOffset;
        _scrollTimer.Stop();
        _scrollViewer = scroll;
        _scrollTarget = target;
        if (animate && SystemParameters.ClientAreaAnimation && Math.Abs(current - target) > 0.5)
        {
            _scrollFrom = current;
            _scrollStarted = Stopwatch.GetTimestamp();
            _scrollTimer.Start();
        }
        else scroll.ScrollToHorizontalOffset(target);
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        if (_scrollViewer == null) { _scrollTimer.Stop(); return; }
        var progress = Math.Min(1, Stopwatch.GetElapsedTime(_scrollStarted).TotalMilliseconds / 160);
        var eased = 1 - (1 - progress) * (1 - progress);
        _scrollViewer.ScrollToHorizontalOffset(_scrollFrom + (_scrollTarget - _scrollFrom) * eased);
        if (progress >= 1) _scrollTimer.Stop();
    }

    public static double ComputeVisibleOffset(double current, double viewport, double extent, Rect tab)
    {
        var target = current;
        if (tab.Left < current + 4) target = tab.Left - 4;
        else if (tab.Right > current + viewport - 4) target = tab.Right - viewport + 4;
        return Math.Clamp(target, 0, Math.Max(0, extent - viewport));
    }

    public void EnsureVisible(BrowserTab tab)
    {
        _dispatcher.InvokeAsync(() =>
        {
            if (!_engine.Tabs.Contains(tab)) return;
            _tabList.UpdateLayout();
            var scroll = GetScrollViewer(_tabList);
            var panel = FindVisualChild<TabShrinkPanel>(_tabList);
            if (scroll == null || panel == null || panel.IsDragging) return;
            if (_tabList.ItemContainerGenerator.ContainerFromItem(tab) is not ListBoxItem container) return;
            var bounds = panel.GetTargetBounds(container);
            AnimateScroll(scroll, ComputeVisibleOffset(scroll.HorizontalOffset, scroll.ViewportWidth, scroll.ExtentWidth, bounds));
        }, DispatcherPriority.Loaded);
    }

    private void OnActiveTabChanged(BrowserTab? tab)
    {
        FindVisualChild<TabShrinkPanel>(_tabList)?.InvalidateMeasure();
        if (tab != null) EnsureVisible(tab);
    }
    private void OnTabCreated(BrowserTab tab) => ApplyLayout();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _drag.Cancel();
        ApplyLayout();
    }
    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_drag.IsDragging && e.LeftButton != MouseButtonState.Pressed)
            FindVisualChild<TabShrinkPanel>(_tabList)?.ReleaseWidths();
    }
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if ((e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0) && _engine.ActiveTab != null)
            EnsureVisible(_engine.ActiveTab);
    }
    private void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // Selection scrolling runs through EnsureVisible, so WPF cannot jump ahead of the animation.
        if (ItemsControl.ContainerFromElement(_tabList, e.TargetObject) is ListBoxItem) e.Handled = true;
    }
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right) e.Handled = true;
    }
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || TabStripDragDropBehavior.IsButton(e.OriginalSource as DependencyObject)) return;
        if (ItemsControl.ContainerFromElement(_tabList, e.OriginalSource as DependencyObject) is ListBoxItem { DataContext: BrowserTab tab })
            CloseTab(tab);
        e.Handled = true;
    }

    private void OnRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(_tabList, e.OriginalSource as DependencyObject) is ListBoxItem item)
            OnTabItemRightClick(item, e);
    }

    public static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    public async Task CreateNewTabAsync()
    {
        try
        {
            var tab = _engine.CreateTab();
            _engine.SwitchTo(tab);
            await _engine.ActivateAsync(tab);
            _onNewTabOpened();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"CreateNewTab failed: {ex}");
        }
    }

    public async Task OpenDroppedInputAsync(string url, BrowserTab? target)
    {
        var tab = target ?? _engine.CreateTab(url);
        _engine.SwitchTo(tab);
        await _engine.ActivateAsync(tab);
        _engine.Navigate(tab, url);
        EnsureVisible(tab);
    }

    public async Task ActivateTabAsync(BrowserTab tab)
    {
        if (!_engine.Tabs.Contains(tab)) return;
        _engine.SwitchTo(tab);
        await _engine.ActivateAsync(tab);
    }

    public void CloseTab(BrowserTab tab)
    {
        if (tab.IsPinned) return;
        FindVisualChild<TabShrinkPanel>(_tabList)?.HoldWidths(tab);
        _engine.CloseTab(tab);
    }

    public void OnTabItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not BrowserTab tab) return;

        var menu = new ContextMenu();

        var pinItem = new MenuItem
        {
            Header = tab.IsPinned ? "Unpin Tab" : "Pin Tab",
        };
        pinItem.Click += (_, _) =>
        {
            var wasPinned = tab.IsPinned;
            tab.IsPinned = !wasPinned;
            var currentIndex = _engine.Tabs.IndexOf(tab);
            if (tab.IsPinned)
            {
                var targetIndex = _engine.Tabs.Take(currentIndex).Count(t => t.IsPinned);
                if (currentIndex != targetIndex)
                {
                    _engine.Tabs.Move(currentIndex, targetIndex);
                }
            }
            else
            {
                var targetIndex = _engine.Tabs.Count(t => t.IsPinned);
                if (currentIndex != targetIndex)
                {
                    _engine.Tabs.Move(currentIndex, targetIndex);
                }
            }
            ApplyLayout();
        };
        menu.Items.Add(pinItem);

        var dupeItem = new MenuItem
        {
            Header = "Duplicate Tab",
        };
        dupeItem.Click += async (_, _) =>
        {
            try
            {
                var newTab = _engine.CreateTab(tab.Url, blockDuplicates: false);
                _engine.SwitchTo(newTab);
                await _engine.ActivateAsync(newTab);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Duplicate tab error: {ex.Message}");
            }
        };
        menu.Items.Add(dupeItem);

        var muteItem = new MenuItem { Header = tab.IsMuted ? "Unmute tab" : "Mute tab" };
        muteItem.Click += (_, _) => _engine.SetTabMuted(tab, !tab.IsMuted);
        menu.Items.Add(muteItem);
        var hibernateItem = new MenuItem
        {
            Header = tab.IsActive ? "Hibernate other tabs" : "Hibernate tab",
            IsEnabled = TabHibernationManager.GetManualHibernateTargets(_engine.Tabs, tab).Length > 0
        };
        hibernateItem.Click += (_, _) =>
        {
            foreach (var target in TabHibernationManager.GetManualHibernateTargets(_engine.Tabs, tab))
                _engine.HibernateTab(target);
        };
        menu.Items.Add(hibernateItem);

        if (!tab.IsPinned)
        {
            menu.Items.Add(new Separator());
            var closeItem = new MenuItem
            {
                Header = "Close Tab",
            };
            closeItem.Click += (_, _) => CloseTab(tab);
            menu.Items.Add(closeItem);
        }

        var copyItem = new MenuItem { Header = "Copy tab URL" };
        copyItem.Click += (_, _) => Clipboard.SetText(tab.Url);
        menu.Items.Add(copyItem);
        menu.Items.Add(new Separator());
        var restoreItem = new MenuItem { Header = "Reopen closed tab", InputGestureText = "Ctrl+Shift+T" };
        restoreItem.Click += async (_, _) =>
        {
            var restored = _engine.RestoreClosedTab();
            if (restored != null) await _engine.ActivateAsync(restored);
        };
        menu.Items.Add(restoreItem);
        var others = new MenuItem { Header = "Close other tabs" };
        others.Click += (_, _) =>
        {
            foreach (var other in _engine.Tabs.Where(t => t != tab && !t.IsPinned).ToArray()) _engine.CloseTab(other);
        };
        menu.Items.Add(others);
        var left = new MenuItem { Header = "Close tabs to the left" };
        left.Click += (_, _) => CloseSide(tab, true);
        menu.Items.Add(left);
        var right = new MenuItem { Header = "Close tabs to the right" };
        right.Click += (_, _) => CloseSide(tab, false);
        menu.Items.Add(right);

        element.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void CloseSide(BrowserTab tab, bool left)
    {
        var index = _engine.Tabs.IndexOf(tab);
        foreach (var other in _engine.Tabs.Where((t, i) => !t.IsPinned && (left ? i < index : i > index)).ToArray())
            _engine.CloseTab(other);
    }

    public void Dispose()
    {
        _drag.Dispose();
        _scrollTimer.Stop();
        _scrollTimer.Tick -= OnScrollTick;
        _tabList.PreviewMouseDown -= OnMouseDown;
        _tabList.PreviewMouseUp -= OnMouseUp;
        _tabList.MouseRightButtonUp -= OnRightButtonUp;
        _tabList.MouseLeave -= OnMouseLeave;
        _layout.SizeChanged -= OnSizeChanged;
        _tabList.RequestBringIntoView -= OnRequestBringIntoView;
        _tabList.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        _engine.ActiveTabChanged -= OnActiveTabChanged;
        _engine.TabCreated -= OnTabCreated;
    }

    public async Task CycleTabAsync(bool reverse)
    {
        if (_engine.Tabs.Count <= 1 || _engine.ActiveTab is null) return;

        var index = _engine.Tabs.IndexOf(_engine.ActiveTab);
        var next = reverse
            ? (index - 1 + _engine.Tabs.Count) % _engine.Tabs.Count
            : (index + 1) % _engine.Tabs.Count;

        _engine.SwitchTo(_engine.Tabs[next]);
        await _engine.ActivateAsync(_engine.ActiveTab!);
    }

    public async Task SwitchToTabByIndex(int tabIndex)
    {
        if (tabIndex >= 0 && tabIndex < _engine.Tabs.Count)
        {
            _engine.SwitchTo(_engine.Tabs[tabIndex]);
            await _engine.ActivateAsync(_engine.Tabs[tabIndex]);
        }
    }

    public static ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}
