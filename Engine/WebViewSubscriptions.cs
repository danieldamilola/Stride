using System;
using System.Diagnostics;

namespace StrideBrowser.Engine;

/// <summary>Removes window-specific page handlers before a tab changes owners.</summary>
internal sealed class WebViewSubscriptions : IDisposable
{
    private readonly List<Action> _remove = [];

    public void Add(Action remove) => _remove.Add(remove);

    public void Dispose()
    {
        // A torn-off tab can outlive the core it was wired to, and unsubscribing
        // from a disposed core throws. One failure must not strand the rest of
        // the handlers, or the tab keeps firing into a dead window.
        foreach (var remove in _remove)
        {
            try { remove(); }
            catch (Exception ex) { Trace.WriteLine($"WebView2 unsubscribe failed: {ex.Message}"); }
        }
        _remove.Clear();
    }
}