using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace UmberBrowser.Engine;

/// <summary>
/// Coordinates asynchronous initialization and readiness for tabs.
/// Publishes a shared readiness TaskCompletionSource before any asynchronous wait,
/// ensuring concurrent activations and popup adoptions await the same in-flight setup.
/// Local cancellation never cancels shared initialization or other waiters.
/// </summary>
public sealed class TabReadinessCoordinator
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<object?>> _readinessTasks = new();

    /// <summary>
    /// Gets an existing readiness task or atomically creates and registers a new one.
    /// </summary>
    public (Task<object?> task, bool isCreator) GetOrAdd(Guid tabId)
    {
        var newTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actualTcs = _readinessTasks.GetOrAdd(tabId, newTcs);
        return (actualTcs.Task, ReferenceEquals(actualTcs, newTcs));
    }

    /// <summary>
    /// Attempts to get the in-flight readiness task for a tab if one exists.
    /// </summary>
    public bool TryGet(Guid tabId, out Task<object?> task)
    {
        if (_readinessTasks.TryGetValue(tabId, out var tcs))
        {
            task = tcs.Task;
            return true;
        }

        task = Task.FromResult<object?>(null);
        return false;
    }

    /// <summary>
    /// Signals successful completion of setup and control readiness for the given tab.
    /// </summary>
    public bool TrySetResult(Guid tabId, object? result)
    {
        if (_readinessTasks.TryRemove(tabId, out var tcs))
        {
            return tcs.TrySetResult(result);
        }
        return false;
    }

    /// <summary>
    /// Signals initialization failure for the tab.
    /// </summary>
    public bool TrySetException(Guid tabId, Exception ex)
    {
        if (_readinessTasks.TryRemove(tabId, out var tcs))
        {
            return tcs.TrySetException(ex);
        }
        return false;
    }

    /// <summary>
    /// Cancels the shared readiness task, for example on tab teardown or abandonment.
    /// </summary>
    public bool TryCancel(Guid tabId)
    {
        if (_readinessTasks.TryRemove(tabId, out var tcs))
        {
            return tcs.TrySetCanceled();
        }
        return false;
    }

    /// <summary>
    /// Awaits a task with a local cancellation token without canceling the underlying task or affecting other waiters.
    /// </summary>
    public static async Task<T> WaitWithCancellationAsync<T>(Task<T> task, CancellationToken ct)
    {
        if (!ct.CanBeCanceled) return await task;
        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = ct.Register(() => tcs.TrySetResult(true));
        var completed = await Task.WhenAny(task, tcs.Task);
        if (ReferenceEquals(completed, tcs.Task))
        {
            throw new OperationCanceledException(ct);
        }
        return await task;
    }
}
