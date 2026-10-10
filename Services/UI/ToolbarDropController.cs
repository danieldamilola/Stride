using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using StrideBrowser.Models;

namespace StrideBrowser.Services.UI;

/// <summary>Opens dropped links and files in the tab under the pointer, or in a new tab.</summary>
public sealed class ToolbarDropController : IDisposable
{
    private readonly ListBox _tabs;
    private readonly FrameworkElement _addressBar;
    private readonly Button _newTab;
    private readonly Func<string, BrowserTab?, Task> _open;
    private readonly Func<BrowserTab?> _activeTab;
    private readonly Func<BrowserTab, Task> _activate;
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _scrollTimer;
    private BrowserTab? _hoverTab;
    private double _scrollStep;

    public ToolbarDropController(ListBox tabs, FrameworkElement addressBar, Button newTab,
        Func<string, BrowserTab?, Task> open, Func<BrowserTab?> activeTab, Func<BrowserTab, Task> activate)
    {
        _tabs = tabs;
        _addressBar = addressBar;
        _newTab = newTab;
        _open = open;
        _activeTab = activeTab;
        _activate = activate;
        _hoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Input, OnHoverTick, tabs.Dispatcher);
        _scrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, OnScrollTick, tabs.Dispatcher);
        StopFeedback();
        foreach (var element in new UIElement[] { tabs, addressBar, newTab })
        {
            element.AllowDrop = true;
            element.PreviewDragOver += OnDragOver;
            element.PreviewDrop += OnDrop;
            element.PreviewDragLeave += OnDragLeave;
        }
        newTab.MouseRightButtonUp += OnNewTabRightClick;
    }

    public static string[] ReadInputs(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            return files.Where(System.IO.File.Exists).Select(path => new Uri(path).AbsoluteUri).ToArray();
        if (data.GetData(DataFormats.UnicodeText) is not string text &&
            data.GetData(DataFormats.Text) is not string) return Array.Empty<string>();
        text = (data.GetData(DataFormats.UnicodeText) ?? data.GetData(DataFormats.Text)) as string ?? "";
        text = text.Trim();
        return text.Length == 0 ? Array.Empty<string>() : new[] { text };
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var accepted = ReadInputs(e.Data).Length > 0;
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        if (accepted && ReferenceEquals(sender, _tabs))
        {
            var tab = (ItemsControl.ContainerFromElement(_tabs, e.OriginalSource as DependencyObject) as ListBoxItem)?.DataContext as BrowserTab;
            if (tab != _hoverTab)
            {
                _hoverTimer.Stop();
                _hoverTab = tab;
                if (tab != null && tab != _activeTab()) _hoverTimer.Start();
            }
            _scrollStep = TabStripDragDropBehavior.ComputeEdgeScroll(e.GetPosition(_tabs).X, _tabs.ActualWidth);
            if (_scrollStep != 0 && TabStripController.GetScrollViewer(_tabs)?.ScrollableWidth > 0) _scrollTimer.Start();
            else _scrollTimer.Stop();
        }
        else StopFeedback();
        e.Handled = true;
    }

    private async void OnHoverTick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (_hoverTab == null) return;
        try { await _activate(_hoverTab); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Tab drop hover failed: {ex}"); }
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        var scroll = TabStripController.GetScrollViewer(_tabs);
        scroll?.ScrollToHorizontalOffset(scroll.HorizontalOffset + _scrollStep);
    }
    private void OnDragLeave(object sender, DragEventArgs e) => StopFeedback();
    private void StopFeedback()
    {
        _hoverTimer.Stop();
        _scrollTimer.Stop();
        _hoverTab = null;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var inputs = ReadInputs(e.Data);
        StopFeedback();
        e.Handled = true;
        var target = ReferenceEquals(sender, _addressBar) ? _activeTab()
            : ItemsControl.ContainerFromElement(_tabs, e.OriginalSource as DependencyObject) is ListBoxItem { DataContext: BrowserTab tab } ? tab : null;
        try
        {
            foreach (var input in inputs)
            {
                await _open(input, target);
                target = null;
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Toolbar drop failed: {ex}"); }
    }

    private void OnNewTabRightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        var paste = new MenuItem { Header = "Paste and go in a new tab", IsEnabled = Clipboard.ContainsText() };
        paste.Click += async (_, _) =>
        {
            try { await _open(Clipboard.GetText(), null); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Paste into new tab failed: {ex}"); }
        };
        menu.Items.Add(paste);
        _newTab.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    public void Dispose()
    {
        StopFeedback();
        _hoverTimer.Tick -= OnHoverTick;
        _scrollTimer.Tick -= OnScrollTick;
        foreach (var element in new UIElement[] { _tabs, _addressBar, _newTab })
        {
            element.PreviewDragOver -= OnDragOver;
            element.PreviewDrop -= OnDrop;
            element.PreviewDragLeave -= OnDragLeave;
        }
        _newTab.MouseRightButtonUp -= OnNewTabRightClick;
    }
}
