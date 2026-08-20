using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StrideBrowser.Engine;
using StrideBrowser.Models;

namespace StrideBrowser.Services.UI;

/// <summary>
/// Manages the tab strip UI: selection changes, mouse-wheel scrolling,
/// context menu interactions (pin/duplicate/close), tab cycling, and index navigation.
/// </summary>
public sealed class TabStripController
{
    private readonly ListBox _tabList;
    private readonly TabEngine _engine;
    private readonly Dispatcher _dispatcher;
    private readonly Action _onNewTabOpened;

    /// <summary>Prevents re-entrant tab selection changes when programmatically updating the ListBox selection.</summary>
    public bool IsUpdatingSelection { get; set; }

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
        var scrollViewer = GetScrollViewer(_tabList);
        if (scrollViewer != null)
        {
            // Use e.Delta to support smooth precision trackpad scrolling
            // e.Delta is typically 120 for a mouse wheel notch, and smaller values for trackpads.
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - (e.Delta * 0.5));
            e.Handled = true;
        }
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

    public void CloseTab(BrowserTab tab)
    {
        if (tab.IsPinned) return;
        _engine.CloseTab(tab);
    }

    public void OnTabItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Grid grid || grid.Tag is not BrowserTab tab) return;

        var menu = new ContextMenu();

        var pinItem = new MenuItem
        {
            Header = tab.IsPinned ? "Unpin Tab" : "Pin Tab",
        };
        pinItem.Click += (_, _) =>
        {
            tab.IsPinned = !tab.IsPinned;
            if (tab.IsPinned)
            {
                var index = _engine.Tabs.IndexOf(tab);
                if (index > 0)
                {
                    _engine.Tabs.Move(index, 0);
                }
            }
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

        if (!tab.IsPinned)
        {
            menu.Items.Add(new Separator());
            var closeItem = new MenuItem
            {
                Header = "Close Tab",
            };
            closeItem.Click += (_, _) => _engine.CloseTab(tab);
            menu.Items.Add(closeItem);
        }

        grid.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
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
