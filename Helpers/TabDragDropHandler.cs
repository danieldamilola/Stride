using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SpurBrowser.Models;

namespace SpurBrowser.Helpers;

/// <summary>
/// Encapsulates tab drag-and-drop reordering logic for a ListBox.
/// </summary>
public sealed class TabDragDropHandler
{
    private readonly ObservableCollection<BrowserTab> _tabs;
    private readonly Func<bool> _getIsUpdatingSelection;
    private readonly Action<bool> _setIsUpdatingSelection;

    private Point _dragStartPoint;
    private bool _isDragging;

    public TabDragDropHandler(
        ObservableCollection<BrowserTab> tabs,
        Func<bool> getIsUpdatingSelection,
        Action<bool> setIsUpdatingSelection)
    {
        _tabs = tabs;
        _getIsUpdatingSelection = getIsUpdatingSelection;
        _setIsUpdatingSelection = setIsUpdatingSelection;
    }

    public void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    public void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;

        var pos = e.GetPosition(null);
        var diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var listBox = sender as ListBox;
            if (listBox?.SelectedItem is BrowserTab tab)
            {
                _isDragging = true;
                DragDrop.DoDragDrop(listBox, tab, DragDropEffects.Move);
                _isDragging = false;
            }
        }
    }

    public void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(BrowserTab)) is not BrowserTab droppedTab) return;

        var listBox = sender as ListBox;
        if (listBox is null) return;

        var targetElement = e.OriginalSource as FrameworkElement;
        while (targetElement != null && targetElement != listBox)
        {
            if (targetElement.DataContext is BrowserTab targetTab && targetTab != droppedTab)
            {
                var oldIndex = _tabs.IndexOf(droppedTab);
                var newIndex = _tabs.IndexOf(targetTab);
                if (oldIndex >= 0 && newIndex >= 0)
                {
                    _setIsUpdatingSelection(true);
                    try { _tabs.Move(oldIndex, newIndex); }
                    finally { _setIsUpdatingSelection(false); }
                }
                break;
            }
            targetElement = VisualTreeHelper.GetParent(targetElement) as FrameworkElement;
        }
    }
}
