using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Models;

namespace StrideBrowser.Engine.Handlers;

public static class TabContextMenuHandler
{
    public static void Wire(
        CoreWebView2 core, 
        Dispatcher dispatcher, 
        BrowserSettings settings, 
        Action<string> openInNewTab,
        Action toggleDarkMode)
    {
        core.ContextMenuRequested += (_, e) =>
        {
            var menuItems = e.MenuItems;
            RemoveUnwantedItems(menuItems);
            
            if (e.ContextMenuTarget.HasLinkUri)
            {
                AddLinkItems(core, menuItems, e.ContextMenuTarget.LinkUri, dispatcher, openInNewTab);
            }

            if (e.ContextMenuTarget.HasSelection)
            {
                AddSearchItem(core, menuItems, e.ContextMenuTarget.SelectionText, dispatcher, settings, openInNewTab);
            }

            menuItems.Add(core.Environment.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator));
            AddDarkModeItem(core, menuItems, dispatcher, settings, toggleDarkMode);
        };
    }

    private static void RemoveUnwantedItems(IList<CoreWebView2ContextMenuItem> menuItems)
    {
        var toRemove = new List<CoreWebView2ContextMenuItem>();
        foreach (var item in menuItems)
        {
            if (item.Name == "collections" || item.Name == "webSelect" || 
                item.Name == "webCapture" || item.Name == "searchWebFor" || 
                item.Name == "readAloud" || item.Name == "share")
            {
                toRemove.Add(item);
            }
        }
        foreach (var item in toRemove) menuItems.Remove(item);
    }

    private static void AddLinkItems(CoreWebView2 core, IList<CoreWebView2ContextMenuItem> menuItems, string linkUri, Dispatcher dispatcher, Action<string> openInNewTab)
    {
        var openInNewTabItem = core.Environment.CreateContextMenuItem("Open Link in New Tab", null, CoreWebView2ContextMenuItemKind.Command);
        openInNewTabItem.CustomItemSelected += (_, _) =>
        {
            _ = dispatcher.InvokeAsync(() =>
            {
                try { openInNewTab(linkUri); }
                catch (Exception ex) { Trace.WriteLine($"Open in new tab failed: {ex.Message}"); }
            });
        };
        menuItems.Insert(0, openInNewTabItem);

        var copyLink = core.Environment.CreateContextMenuItem("Copy Link Address", null, CoreWebView2ContextMenuItemKind.Command);
        copyLink.CustomItemSelected += (_, _) =>
        {
            dispatcher.Invoke(() =>
            {
                try { System.Windows.Clipboard.SetText(linkUri); } catch (Exception ex) { Trace.WriteLine(ex); }
            });
        };
        menuItems.Insert(1, copyLink);
        menuItems.Insert(2, core.Environment.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator));
    }

    private static void AddSearchItem(CoreWebView2 core, IList<CoreWebView2ContextMenuItem> menuItems, string selection, Dispatcher dispatcher, BrowserSettings settings, Action<string> openInNewTab)
    {
        if (string.IsNullOrWhiteSpace(selection)) return;

        var displaySelection = selection.Length > 20 ? selection.Substring(0, 17) + "..." : selection;
        var searchItem = core.Environment.CreateContextMenuItem($"Search Stride for '{displaySelection}'", null, CoreWebView2ContextMenuItemKind.Command);
        
        searchItem.CustomItemSelected += (_, _) =>
        {
            _ = dispatcher.InvokeAsync(() =>
            {
                var url = $"https://duckduckgo.com/?q={Uri.EscapeDataString(selection)}";
                if (settings.SearchEngine == "Google") url = $"https://www.google.com/search?q={Uri.EscapeDataString(selection)}";
                else if (settings.SearchEngine == "Bing") url = $"https://www.bing.com/search?q={Uri.EscapeDataString(selection)}";
                openInNewTab(url);
            });
        };
        menuItems.Add(searchItem);
    }

    private static void AddDarkModeItem(CoreWebView2 core, IList<CoreWebView2ContextMenuItem> menuItems, Dispatcher dispatcher, BrowserSettings settings, Action toggleDarkMode)
    {
        var darkModeItem = core.Environment.CreateContextMenuItem(
            settings.ForceDarkMode ? "Disable Dark Mode" : "Enable Dark Mode", null, CoreWebView2ContextMenuItemKind.Command);
        darkModeItem.CustomItemSelected += (_, _) => dispatcher.Invoke(() => toggleDarkMode());
        menuItems.Add(darkModeItem);
    }
}
