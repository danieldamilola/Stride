using System;

namespace UmberBrowser.Services.UI;

/// <summary>
/// Decides whether a TabStateChanged event needs a WebView activation, by
/// active tab identity only. The engine raises the event both for real
/// switches and for metadata-only updates such as favicon, title, theme or
/// audio changes on the already active tab. Content is never compared:
/// any metadata change on the active tab returns false, and any newly
/// active tab returns true, including A/B/A returns and wake selections.
/// The first sighting activates so cold startup and session restore work.
/// Callers must only pass the event tab through when it is active; the
/// guard rechecks IsActive itself so background tab events never activate.
/// </summary>
public sealed class TabActivationGuard
{
    private Guid? _lastActivatedTabId;

    /// <summary>True when this event moves activation to a tab that does not have it yet.</summary>
    public bool ShouldActivate(Models.BrowserTab tab)
    {
        if (!tab.IsActive) return false;
        if (_lastActivatedTabId == tab.Id) return false;
        _lastActivatedTabId = tab.Id;
        return true;
    }

    /// <summary>Drops the tracked activation when that tab goes away.</summary>
    public void Forget(Guid id)
    {
        if (_lastActivatedTabId == id) _lastActivatedTabId = null;
    }
}
