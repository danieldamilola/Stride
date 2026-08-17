using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StrideBrowser.Models;

namespace StrideBrowser.Services.UI;

/// <summary>
/// Encapsulates drag-and-drop tab reordering for a ListBox.
/// </summary>
public sealed class TabStripDragDropBehavior
{
    private Point _dragStartPoint;
    private bool _isDragging;
    private readonly ListBox _listBox;
    private readonly Action<int, int> _moveTab;

    public TabStripDragDropBehavior(ListBox listBox, Action<int, int> moveTab)
    {
        _listBox = listBox;
        _moveTab = moveTab;
        
        _listBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        _listBox.PreviewMouseMove += OnPreviewMouseMove;
        _listBox.Drop += OnDrop;
        _listBox.AllowDrop = true;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;
        
        var pos = e.GetPosition(null);
        var diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (_listBox.SelectedItem is BrowserTab tab)
            {
                _isDragging = true;
                DragDrop.DoDragDrop(_listBox, tab, DragDropEffects.Move);
                _isDragging = false;
            }
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(BrowserTab)) is not BrowserTab droppedTab) return;
        
        var targetElement = e.OriginalSource as FrameworkElement;
        while (targetElement != null && targetElement != _listBox)
        {
            if (targetElement.DataContext is BrowserTab targetTab && targetTab != droppedTab)
            {
                var oldIndex = _listBox.Items.IndexOf(droppedTab);
                var newIndex = _listBox.Items.IndexOf(targetTab);
                if (oldIndex >= 0 && newIndex >= 0)
                {
                    _moveTab(oldIndex, newIndex);
                }
                break;
            }
            targetElement = VisualTreeHelper.GetParent(targetElement) as FrameworkElement;
        }
    }
}
