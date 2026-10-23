using System;
using System.Threading.Tasks;
using System.Windows;
using UmberBrowser.Engine;
using UmberBrowser.Models;

namespace UmberBrowser.Services.UI;

/// <summary>
/// Lets the tab strip hand a dragged tab to another window. Absent in tests and in any
/// window that has no peer windows yet, which is what keeps tearing off a no-op there.
/// </summary>
public sealed class TabTransferHost
{
    public TabTransferHost(TabEngine engine, BrowserWindowManager windows)
    {
        Engine = engine;
        Windows = windows;
    }

    public TabEngine Engine { get; }
    public BrowserWindowManager Windows { get; }

    /// <summary>True when this window may still move the tab, so a drag started here can tear off.</summary>
    public bool Owns(BrowserTab tab) => Windows.Owns(Engine, tab);

    /// <summary>The tab strip of another window under the cursor, if any.</summary>
    public System.Windows.Controls.ListBox? StripUnderCursor(System.Windows.Point screenPoint) =>
        Windows.StripUnderPoint(screenPoint, Engine);

    /// <summary>Opens the tab in a window of its own.</summary>
    public Task Detach(BrowserTab tab, Point screenPoint) => Windows.DetachAsync(Engine, tab, screenPoint);

    /// <summary>
    /// Docks the tab into the window that owns the given strip. Returns false when no open
    /// window owns it, which leaves the caller to tear the tab off instead.
    /// </summary>
    public Task<bool> DockAt(System.Windows.Controls.ListBox strip, BrowserTab tab, int index, Point screenPoint) =>
        Windows.DockAt(this, strip, tab, index, screenPoint);
}