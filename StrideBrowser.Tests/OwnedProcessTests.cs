using System.Diagnostics;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Runs an owned child process and collects what it wrote, with every wait bounded. The
/// storage denial harness and the process timeout tests both need the same thing: start a
/// child, drain both pipes, and stop the child when it runs past a deadline. Doing that in
/// two places is how a bounded wait turns into an unbounded one, so it lives here once.
///
/// Both pipe reads start while the child is running, before the wait for exit, so a child
/// that writes more than a pipe buffer never blocks waiting for the parent to read. The
/// reads return immutable strings under a cancellation token. If the child does not stop or
/// the pipes do not finish in time, the run reports Collected false with the reason rather
/// than returning a partial buffer as if it were whole.
/// </summary>
public static class OwnedProcess
{
    public sealed record OwnedProcessRun(int ProcessId, bool TimedOut, int ExitCode, string Stdout, string Stderr, bool Collected, string Error)
    {
        public string Detail => (TimedOut
            ? $"the process did not finish within its deadline and was stopped (pid {ProcessId})"
            : $"exit {ExitCode}") + (Collected ? "" : $"; not fully captured: {Error}");
    }

    public static OwnedProcessRun Run(ProcessStartInfo start, TimeSpan timeout)
    {
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"the process did not start: {start.FileName}");

        using var cts = new CancellationTokenSource();

        // Start both pipe reads WHILE the child is running, before waiting for exit.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        var waitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(timeout);
        var completed = Task.WhenAny(waitTask, timeoutTask).Result;

        if (completed == timeoutTask)
        {
            // The child ran past its deadline. Stop it, then bound the wait for it to die.
            try { process.Kill(entireProcessTree: true); } catch { }
            bool reaped;
            try
            {
                process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                reaped = true;
            }
            catch (OperationCanceledException) { reaped = false; }
            catch { reaped = false; }

            if (!reaped)
            {
                cts.Cancel();
                try { process.StandardOutput.Close(); } catch { }
                try { process.StandardError.Close(); } catch { }
                return new OwnedProcessRun(process.Id, true, -1, string.Empty, string.Empty,
                    false, "the child did not stop within 5s of being killed");
            }

            // Process is gone. Wait for the already-started reads to finish, bounded.
            return Finish(process, stdoutTask, stderrTask, cts, true);
        }

        // Process exited. Wait for the already-started reads to finish, bounded.
        return Finish(process, stdoutTask, stderrTask, cts, false);
    }

    private static OwnedProcessRun Finish(Process process, Task<string> stdoutTask, Task<string> stderrTask, CancellationTokenSource cts, bool timedOut)
    {
        // The capture-completion deadline starts after process termination, not at startup.
        using var captureCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            Task.WhenAll(stdoutTask, stderrTask).WaitAsync(captureCts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            cts.Cancel();
            try { process.StandardOutput.Close(); } catch { }
            try { process.StandardError.Close(); } catch { }
            return new OwnedProcessRun(process.Id, timedOut, timedOut ? -1 : process.ExitCode,
                string.Empty, string.Empty, false, "output capture timed out");
        }
        catch (Exception ex)
        {
            return new OwnedProcessRun(process.Id, timedOut, timedOut ? -1 : process.ExitCode,
                string.Empty, string.Empty, false, ex.Message);
        }

        if (stdoutTask.IsCompletedSuccessfully && stderrTask.IsCompletedSuccessfully)
        {
            return new OwnedProcessRun(process.Id, timedOut, timedOut ? -1 : process.ExitCode,
                stdoutTask.Result, stderrTask.Result, true, string.Empty);
        }

        var error = (stdoutTask.IsFaulted ? stdoutTask.Exception?.InnerException?.Message
            : stderrTask.Exception?.InnerException?.Message) ?? "a pipe read faulted";
        return new OwnedProcessRun(process.Id, timedOut, timedOut ? -1 : process.ExitCode,
            string.Empty, string.Empty, false, error);
    }
}

/// <summary>
/// The owned process runner above, measured against a real child. The timeout case runs a
/// child the test owns and that outlives a short deadline, so a runner that did not actually
/// stop and collect it would hang here or leave it running. The capture case checks that a
/// child which exits on its own has both pipes read. The large-output case proves the reads
/// are concurrent: a child that writes more than both pipe buffers would deadlock a runner
/// that waits for exit before reading. The descendant case proves the drain deadline when a
/// grandchild holds a pipe open after the child is gone.
/// </summary>
[Collection("DeadlineChecks")]
public sealed class OwnedProcessTests
{
    [Fact]
    public void AnOwnedChildThatRunsPastItsDeadlineIsStoppedAndCollected()
    {
        var start = new ProcessStartInfo(YouTubeUnhookStorageDenialTests.ResolveNode())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // Writes to both pipes, then stays alive well past the deadline.
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("console.log('stdout marker'); console.error('stderr marker'); setTimeout(() => {}, 60000);");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var run = OwnedProcess.Run(start, TimeSpan.FromSeconds(3));
        sw.Stop();

        Assert.True(run.TimedOut);
        Assert.True(run.Collected, run.Detail);
        // Upper bound: 3s deadline + 5s kill-wait + 10s drain + small buffer.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(19), $"elapsed {sw.Elapsed}");
        Assert.Contains("stdout marker", run.Stdout);
        Assert.Contains("stderr marker", run.Stderr);
        // The child was stopped and reaped, not left running.
        Assert.ThrowsAny<ArgumentException>(() => Process.GetProcessById(run.ProcessId));
    }

    [Fact]
    public void AnOwnedChildsOutputIsCapturedFromBothPipes()
    {
        var start = new ProcessStartInfo(YouTubeUnhookStorageDenialTests.ResolveNode())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("process.stdout.write('out'); process.stderr.write('err');");

        var run = OwnedProcess.Run(start, TimeSpan.FromSeconds(15));

        Assert.False(run.TimedOut);
        Assert.Equal(0, run.ExitCode);
        Assert.True(run.Collected, run.Detail);
        Assert.Equal("out", run.Stdout);
        Assert.Equal("err", run.Stderr);
    }

    [Fact]
    public void AnOwnedChildWritingMoreThanBothPipeBuffersIsCapturedWhole()
    {
        var start = new ProcessStartInfo(YouTubeUnhookStorageDenialTests.ResolveNode())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // Write 1 MiB to each pipe, then exit normally. A runner that waits for exit before
        // reading would deadlock on the full pipe buffer and kill this as a false timeout.
        // set exitCode rather than process.exit so Node flushes both writes.
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("const a = 'x'.repeat(1024 * 1024); const b = 'y'.repeat(1024 * 1024); process.stdout.write(a); process.stderr.write(b); process.exitCode = 0;");

        var run = OwnedProcess.Run(start, TimeSpan.FromSeconds(30));

        Assert.False(run.TimedOut, run.Detail);
        Assert.True(run.Collected, run.Detail);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new string('x', 1024 * 1024), run.Stdout);
        Assert.Equal(new string('y', 1024 * 1024), run.Stderr);
    }

    [Fact]
    public void AnOwnedChildWhoseDescendantHoldsAPipeIsStoppedAndCollected()
    {
        var start = new ProcessStartInfo(YouTubeUnhookStorageDenialTests.ResolveNode())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // The child spawns a grandchild that inherits its stdout and stderr, so the pipes
        // stay open even after the child is gone. The tree kill has to reach the grandchild
        // for the drain to finish; if it does not, the drain deadline ends the wait.
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("const { spawn } = require('child_process'); const g = spawn(process.execPath, ['-e', 'setTimeout(() => {}, 8000)'], { stdio: ['ignore', 'inherit', 'inherit'] }); g.on('error', () => {}); setTimeout(() => {}, 60000);");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var run = OwnedProcess.Run(start, TimeSpan.FromSeconds(3));
        sw.Stop();

        Assert.True(run.TimedOut);
        // Upper bound: 3s deadline + 5s kill-wait + 10s drain + small buffer.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(19), $"elapsed {sw.Elapsed}");
        // The child was stopped and reaped, not left running.
        Assert.ThrowsAny<ArgumentException>(() => Process.GetProcessById(run.ProcessId));
    }
}
