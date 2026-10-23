using System;
using System.Threading;
using System.Threading.Tasks;

namespace UmberBrowser.Engine;

/// <summary>
/// Serializes native control setup across tabs, then runs each tab's first
/// navigation on its own.
///
/// The old creation gate wrapped both operations. A tab whose first external
/// navigation was waiting for the shared extension registration therefore held
/// the gate for up to the registration budget, and every other tab, including a
/// built in page that needs no registration, waited behind it. Setup is the part
/// that must not overlap: two WebView2 runtimes should not start at the same
/// time. The first navigation is per tab work and its wait belongs to the engine,
/// so it runs after this class releases the gate.
///
/// The caller supplies both operations. This class only owns the boundary and
/// does not look at the tab, the control or the navigation intent, which stay in
/// <see cref="TabEngine"/>. Setup and first navigation both resume on the
/// caller's synchronization context, because the control is COM and belongs to
/// the UI thread.
/// </summary>
public sealed class TabCreationCoordinator
{
    private readonly SemaphoreSlim _setupGate = new(1, 1);

    /// <summary>
    /// Runs <paramref name="setupAsync"/> while holding the setup gate, releases
    /// the gate, then runs <paramref name="firstNavigationAsync"/> for the value
    /// setup produced. A null setup result means the caller abandoned the tab and
    /// no navigation runs.
    ///
    /// <paramref name="lifetimeToken"/> covers the gate wait only, and is cancelled
    /// when the engine is disposed. A tab switch never cancels it, because the
    /// registration another tab or window is waiting on must keep running.
    /// </summary>
    public async Task<TView?> RunAsync<TView>(
        Func<Task<TView?>> setupAsync,
        Func<TView, Task> firstNavigationAsync,
        CancellationToken lifetimeToken)
        where TView : class
    {
        ArgumentNullException.ThrowIfNull(setupAsync);
        ArgumentNullException.ThrowIfNull(firstNavigationAsync);

        await _setupGate.WaitAsync(lifetimeToken);
        TView? view;
        try
        {
            view = await setupAsync();
        }
        finally
        {
            _setupGate.Release();
        }

        if (view is null) return null;

        await firstNavigationAsync(view);
        return view;
    }
}
