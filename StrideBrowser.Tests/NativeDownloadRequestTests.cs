using StrideBrowser.Services.Downloads;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class NativeDownloadRequestTests
{
    private static TaskCompletionSource<bool> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task CancelledBeforeNavigationCompletionCancelsPendingStartWithItsToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var started = Pending();

        await NativeDownloadRequest.CompleteNavigationAsync(started, () => false, cancellation.Token);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => started.Task);
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task CancelledDuringTheGracePeriodCompletesPendingStartAsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var started = Pending();
        var completion = NativeDownloadRequest.CompleteNavigationAsync(started, () => false, cancellation.Token);

        cancellation.Cancel();
        await completion;

        Assert.True(started.Task.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => started.Task);
    }

    [Fact]
    public async Task NavigationWithoutADownloadCompletesStartAsFalse()
    {
        var started = Pending();

        await NativeDownloadRequest.CompleteNavigationAsync(started, () => false, CancellationToken.None);

        Assert.False(await started.Task);
    }

    [Fact]
    public async Task AdoptedDownloadIsNotReplacedByLateNavigationCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var started = Pending();
        var adopted = false;
        var completion = NativeDownloadRequest.CompleteNavigationAsync(started, () => adopted, cancellation.Token);

        adopted = true;
        started.SetResult(true);
        cancellation.Cancel();
        await completion;

        Assert.True(await started.Task);
    }
}
