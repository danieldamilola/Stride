using System.Diagnostics;

namespace StrideBrowser.Services;

/// <summary>One writer owns each store. Failed writes stay dirty for a later mutation or flush.</summary>
internal sealed class OrderedStorePersistence<T>(object stateGate, Func<T> snapshot, Action<T> write)
{
    private readonly SemaphoreSlim _writer = new(1, 1);
    private long _requested;
    private long _persisted;
    private long _attempted;
    private Task<bool>? _running;

    // The store calls this while holding stateGate. No snapshot or serialization is done here.
    public void Changed()
    {
        lock (stateGate)
        {
            _requested++;
            StartWorker();
        }
    }

    private void StartWorker()
    {
        if (_running is not null) return;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _running = completion.Task;
        _ = Task.Run(async () =>
        {
            var success = false;
            try { success = await RunAsync().ConfigureAwait(false); }
            finally
            {
                lock (stateGate)
                {
                    _running = null;
                    if (_persisted != _requested && (success || _requested > _attempted)) StartWorker();
                }
                completion.TrySetResult(success);
            }
        });
    }

    private async Task<bool> RunAsync()
    {
        await Task.Delay(75).ConfigureAwait(false);
        var failures = 0;
        while (true)
        {
            await _writer.WaitAsync().ConfigureAwait(false);
            try
            {
                T data;
                long version;
                lock (stateGate)
                {
                    if (_persisted == _requested) return true;
                    version = _requested;
                    _attempted = version;
                    data = snapshot();
                }
                write(data);
                lock (stateGate) _persisted = version;
                failures = 0;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Store persistence failed: {ex}");
                if (++failures >= 3) return false;
            }
            finally { _writer.Release(); }

            // Coalesce mutations received during the previous write, including bulk changes.
            await Task.Delay(failures == 0 ? 25 : failures * 100).ConfigureAwait(false);
        }
    }

    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        Task<bool> pending;
        long target;
        lock (stateGate)
        {
            target = _requested;
            if (_persisted >= target) return true;
            StartWorker();
            pending = _running!;
        }
        // Cancellation stops waiting. It does not abandon an atomic write in progress.
        while (true)
        {
            var success = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (stateGate)
            {
                if (_persisted >= target) return true;
                if (!success) return false;
                StartWorker();
                pending = _running!;
            }
        }
    }

    /// <summary>Explicit durable operations share the writer gate with background saves.</summary>
    public bool WriteNow(Func<bool> operation, bool persistFailure = false)
    {
        _writer.Wait();
        try
        {
            lock (stateGate)
            {
                var success = operation();
                if (success)
                {
                    _requested++;
                    _persisted = _requested;
                }
                else
                {
                    if (persistFailure) _requested++;
                    if (_requested != _persisted) StartWorker();
                }
                return success;
            }
        }
        finally { _writer.Release(); }
    }
}
