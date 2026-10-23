using System.Diagnostics;
using System.Windows;
using UmberBrowser.Models;

namespace UmberBrowser.Engine;

public sealed partial class TabEngine
{
    public bool CanTransfer(BrowserTab tab) => !_disposed && Tabs.Contains(tab) && !_creatingTabs.Contains(tab.Id);

    /// <summary>Moves the existing control without navigating or closing the page.</summary>
    public bool TransferTo(TabEngine destination, BrowserTab tab, int index)
    {
        _dispatcher.VerifyAccess();
        if (destination == this || !CanTransfer(tab) || destination._disposed ||
            destination._webViewHost is null || destination.Tabs.Contains(tab)) return false;

        var oldIndex = Tabs.IndexOf(tab);
        _webViews.TryGetValue(tab.Id, out var view);
        if (view is not null)
        {
            var element = (FrameworkElement)view;
            var previousWindow = Window.GetWindow(element);
            // The control stays bound to the window it was built in, so that window is
            // still what the tab depends on once the tab has moved.
            var hostWindow = _controlHosts.GetValueOrDefault(tab.Id);
            var destinationWindow = Window.GetWindow(destination._webViewHost);
            UnwireTabEvents(tab.Id);
            _webViewHost!.Children.Remove(element);
            _webViews.Remove(tab.Id);
            try
            {
                destination._webViewHost.Children.Add(element);
                destination._webViews.Add(tab.Id, view);
                // A rebind that reports failure leaves the control tied to the old window,
                // so the host handle has to keep saying so. Otherwise the tab would be
                // skipped by RecycleTabsHostedBy and go blank when that window closes.
                var rebound = WebView2WindowBinding.Rebind(element, previousWindow, destinationWindow);
                destination._controlHosts[tab.Id] = rebound
                    ? WebView2WindowBinding.HandleOf(destinationWindow)
                    : hostWindow;
                Trace.WriteLine($"TabEngine.TransferTo tab={tab.Id} control={element.GetType().Name} " +
                    $"host={WebView2WindowBinding.HandleOf(previousWindow)}->{WebView2WindowBinding.HandleOf(destinationWindow)} " +
                    $"rebound={rebound} recorded={destination._controlHosts[tab.Id]}");
                destination.WireTabEvents(view, tab);
            }
            catch
            {
                destination.UnwireTabEvents(tab.Id);
                destination._webViews.Remove(tab.Id);
                destination._controlHosts.Remove(tab.Id);
                destination._webViewHost.Children.Remove(element);
                _webViewHost.Children.Add(element);
                _webViews.Add(tab.Id, view);
                _controlHosts[tab.Id] = hostWindow;
                WebView2WindowBinding.Rebind(element, Window.GetWindow(_webViewHost), previousWindow);
                WireTabEvents(view, tab);
                throw;
            }
        }

        var wasActive = ActiveTab == tab;
        tab.IsActive = false;
        if (wasActive) ActiveTab = null;
        Tabs.RemoveAt(oldIndex);
        destination.Tabs.Insert(Math.Clamp(index, 0, destination.Tabs.Count), tab);
        if (wasActive)
        {
            if (Tabs.Count > 0) SwitchTo(Tabs[Math.Min(oldIndex, Tabs.Count - 1)]);
            else ActiveTabChanged?.Invoke(null);
        }
        // The caller moves reader state before activating the destination.
        return true;
    }

    private void WireTabEvents(dynamic view, BrowserTab tab)
    {
        var subscriptions = new WebViewSubscriptions();
        _subscriptions.Add(tab.Id, subscriptions);
        WireInlinedContextMenu(view.CoreWebView2, tab, subscriptions);
        WireNavigationEvents(view, tab, subscriptions);
        WireTitleAndSourceEvents(view, tab, subscriptions);
        _ipcBridge.Wire(view, tab, subscriptions);
        HandleProcessFailure(view, tab, subscriptions);
    }

    private void UnwireTabEvents(Guid tabId)
    {
        if (_subscriptions.Remove(tabId, out var subscriptions)) subscriptions.Dispose();
    }
}