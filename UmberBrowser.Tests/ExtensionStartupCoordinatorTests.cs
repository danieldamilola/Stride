using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// The startup and toggle coordinator, with no WebView2 and no disk anywhere.
///
/// What it has to get right is the shape of the sharing: one run per profile,
/// waiters that can leave without taking the run with them, a retry when the
/// window that owned a run disappears, and a preference change that lands while
/// a run is in flight being applied rather than dropped. Each test drives the
/// run through a fake reconcile delegate, which is the same seam WebViewFactory
/// fills with the real WebView2 calls.
/// </summary>
[Collection("DeadlineChecks")]
public sealed class ExtensionStartupCoordinatorTests
{
    private const string ProfileA = @"C:\umber-test\profile-a";

    private static ExtensionStartupRequest Request(
        string profile, string owner, ExtensionReconcileCore reconcile, bool desired = true) =>
        new(profile, desired, owner, reconcile);

    [Fact]
    public async Task TwoWaitersOnOneProfile_ShareOneRun()
    {
        var coordinator = new ExtensionStartupCoordinator();
        var profile = new FakeProfile();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        profile.Hold = gate;

        var first = coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", profile.Reconcile));
        await profile.Entered.Task;
        var second = coordinator.EnsureReadyAsync(Request(ProfileA, "window-2", profile.Reconcile));

        gate.SetResult(true);
        var results = await Task.WhenAll(first, second);

        Assert.All(results, r =>
        {
            Assert.Equal(ExtensionStartupStatus.Ready, r.Status);
            Assert.True(r.Succeeded);
            Assert.True(r.Registered);
            Assert.True(r.Enabled);
        });
        // One reconciliation, not one per window.
        Assert.Equal(1, profile.Calls);
        Assert.Equal(1, coordinator.AttemptCount(ProfileA));
    }

    [Fact]
    public async Task TwoProfiles_StayIndependent()
    {
        var coordinator = new ExtensionStartupCoordinator();
        var a = new FakeProfile();
        var b = new FakeProfile();

        var results = await Task.WhenAll(
            coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", a.Reconcile)),
            coordinator.EnsureReadyAsync(Request(@"C:\umber-test\profile-b", "window-1", b.Reconcile)));

        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Equal(1, a.Calls);
        Assert.Equal(1, b.Calls);
        Assert.Equal(1, coordinator.AttemptCount(ProfileA));
        Assert.Equal(1, coordinator.AttemptCount(@"C:\umber-test\profile-b"));
    }

    [Theory]
    [InlineData(@"C:\umber-test\profile-a")]
    [InlineData(@"C:\umber-test\profile-a\")]
    [InlineData(@"C:\umber-test\profile-a/")]
    public async Task ProfileKeysThatNameTheSameFolder_ShareOneRun(string spelling)
    {
        // A trailing separator is the same folder, and two windows disagreeing about
        // it would each run their own registration over one profile.
        var coordinator = new ExtensionStartupCoordinator();
        var profile = new FakeProfile();

        var first = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", profile.Reconcile));
        var second = await coordinator.EnsureReadyAsync(Request(spelling, "window-2", profile.Reconcile));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(1, profile.Calls);
    }

    [Fact]
    public async Task AClosedOwnerCore_FailsHonestAndAnotherCoreRetries()
    {
        var coordinator = new ExtensionStartupCoordinator();
        var dying = new FakeProfile { Throw = new ObjectDisposedException("CoreWebView2") };
        var healthy = new FakeProfile();
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        dying.Hold = hold;

        var owner = coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", dying.Reconcile));
        await dying.Entered.Task;
        var other = coordinator.EnsureReadyAsync(Request(ProfileA, "window-2", healthy.Reconcile));

        hold.SetResult(true);
        var ownerResult = await owner;
        var otherResult = await other;

        // The window that owned the run gets the truth about its own failure.
        Assert.Equal(ExtensionStartupStatus.Failed, ownerResult.Status);
        Assert.False(ownerResult.Succeeded);
        Assert.Contains(nameof(ObjectDisposedException), ownerResult.Detail);

        // The window that still has a live core retries on it and gets a real answer.
        Assert.Equal(ExtensionStartupStatus.Ready, otherResult.Status);
        Assert.True(otherResult.Succeeded);
        Assert.True(otherResult.Retried);
        Assert.Equal(1, healthy.Calls);
    }

    [Fact]
    public async Task AFailedRunIsNotCached_SoALaterCoreCanTryAgain()
    {
        var coordinator = new ExtensionStartupCoordinator();
        var dying = new FakeProfile { Throw = new InvalidOperationException("browser process gone") };
        var healthy = new FakeProfile();

        var failed = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", dying.Reconcile));
        var retried = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-2", healthy.Reconcile));

        Assert.Equal(ExtensionStartupStatus.Failed, failed.Status);
        Assert.True(retried.Succeeded);
        Assert.Equal(1, healthy.Calls);
    }

    [Fact]
    public async Task ACancelledWaiterLeaves_TheSharedRunKeepsGoing()
    {
        var coordinator = new ExtensionStartupCoordinator();
        var profile = new FakeProfile();
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        profile.Hold = hold;
        using var cts = new CancellationTokenSource();

        var owner = coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", profile.Reconcile));
        await profile.Entered.Task;
        var leaving = coordinator.EnsureReadyAsync(Request(ProfileA, "window-2", profile.Reconcile), null, cts.Token);

        cts.Cancel();
        var leavingResult = await leaving;

        Assert.Equal(ExtensionStartupStatus.Cancelled, leavingResult.Status);
        Assert.False(leavingResult.Succeeded);

        // The waiter that went away did not cancel the work the other window needs.
        hold.SetResult(true);
        var ownerResult = await owner;
        Assert.True(ownerResult.Succeeded);
        Assert.Equal(1, profile.Calls);
    }

    [Fact]
    public async Task ATimedOutWaitIsNotMarkedReady()
    {
        var coordinator = new ExtensionStartupCoordinator();
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new FakeProfile { Hold = hold };

        var timedOut = await coordinator.EnsureReadyAsync(
            Request(ProfileA, "window-1", slow.Reconcile), TimeSpan.FromMilliseconds(150));

        Assert.Equal(ExtensionStartupStatus.TimedOut, timedOut.Status);
        Assert.False(timedOut.Succeeded);
        Assert.Null(coordinator.Cached(ProfileA, desiredEnabled: true));

        // Giving up did not cancel the run or mark the profile ready. It finishes on
        // its own, and the next caller picks the same run up rather than starting a
        // second registration.
        hold.SetResult(true);
        var later = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-2", slow.Reconcile));

        Assert.True(later.Succeeded);
        Assert.Equal(1, slow.Calls);
        Assert.Equal(1, coordinator.AttemptCount(ProfileA));
    }

    [Fact]
    public async Task APreferenceThatMovesMidRun_IsAppliedBeforeCompletion()
    {
        var coordinator = new ExtensionStartupCoordinator();
        var profile = new FakeProfile();
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        profile.Hold = hold;

        var running = coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", profile.Reconcile));
        await profile.Entered.Task;

        // The settings page turns ad blocking off while the first registration is
        // still installing. The run has to finish on the newer value.
        coordinator.RecordPreference(ProfileA, desiredEnabled: false);
        hold.SetResult(true);
        var result = await running;

        Assert.Equal(ExtensionStartupStatus.Ready, result.Status);
        Assert.False(result.Enabled);
        Assert.Equal(new[] { true, false }, profile.Requested.ToArray());
    }

    [Fact]
    public async Task OffThenOn_ReusesTheRegisteredExtension()
    {
        var coordinator = new ExtensionStartupCoordinator();
        var profile = new FakeProfile();

        var on = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", profile.Reconcile, desired: true));
        var off = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", profile.Reconcile, desired: false));
        var back = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", profile.Reconcile, desired: true));

        Assert.True(on.Enabled);
        Assert.Equal(ExtensionStartupStatus.Ready, off.Status);
        Assert.False(off.Enabled);
        Assert.True(back.Enabled);
        // Still the same registration: an enable and a disable, never a reinstall.
        Assert.Equal(new[] { true, false, true }, profile.Requested.ToArray());
        Assert.True(profile.Installed);
    }

    [Fact]
    public async Task OffWithNothingInstalled_ReportsNotInstalledAndSucceeds()
    {
        var coordinator = new ExtensionStartupCoordinator();
        var profile = new FakeProfile { Installed = false };

        var result = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", profile.Reconcile, desired: false));

        Assert.Equal(ExtensionStartupStatus.NotInstalled, result.Status);
        Assert.True(result.Succeeded);
        Assert.False(result.Registered);
        Assert.Equal(1, profile.Calls);
    }

    [Fact]
    public async Task ASecondCallWithTheSamePreference_DoesNotRunTheWorkAgain()
    {
        // A tab switch or a reload must not wait on anything. The profile is already
        // reconciled, so the answer comes from what was proven last time.
        var coordinator = new ExtensionStartupCoordinator();
        var profile = new FakeProfile();

        await coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", profile.Reconcile));
        var second = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-2", profile.Reconcile));

        Assert.True(second.Succeeded);
        Assert.Equal(1, profile.Calls);
        Assert.True(second.Duration < TimeSpan.FromSeconds(1));
        Assert.NotNull(coordinator.Cached(ProfileA, desiredEnabled: true));
    }

    [Fact]
    public async Task RegisteredButNotActive_IsNotSuccess()
    {
        // The exact case the old code called loaded: a folder exists, so the profile
        // reports HasFolder, while nothing is registered or running.
        var coordinator = new ExtensionStartupCoordinator();
        var profile = new FakeProfile { Installed = false };

        var result = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", profile.Reconcile, desired: true));

        Assert.Equal(ExtensionStartupStatus.Failed, result.Status);
        Assert.False(result.Succeeded);
        Assert.False(result.Registered);
    }

    [Fact]
    public async Task NativeStateIsReportedFromTheProfile()
    {
        var coordinator = new ExtensionStartupCoordinator();
        var profile = new FakeProfile();

        var result = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", profile.Reconcile));

        Assert.Equal("ublock-id", result.Native.ExtensionId);
        Assert.Equal(@"C:\umber-test\extensions\uBlock0_1.73.0", result.Native.ExtensionFolder);
        Assert.Equal(ProfileA, result.Native.ProfilePath);
        Assert.Equal(ProfileA, result.ProfileKey);
    }

    [Fact]
    public void ReadinessIsUnverifiedUntilSomethingObservesFiltering()
    {
        // Registration and a stable IsEnabled are not proof that the filter engine
        // ever ran. Only a controlled observation may say otherwise.
        var coordinator = new ExtensionStartupCoordinator();
        var result = ExtensionStartupResult.NoCore(ProfileA, true, "no core");

        Assert.False(result.BlockingVerified);
        Assert.False(result.Succeeded);
        Assert.Equal(ExtensionStartupStatus.NoCore, result.Status);

        var observed = result with { Readiness = FilterReadiness.BlockingVerified };
        Assert.True(observed.BlockingVerified);
        Assert.NotNull(coordinator);
    }

    [Fact]
    public async Task APreferenceThatMovesOnEveryPass_NeverReportsReadyForTheOlderOne()
    {
        // The last pass returning a satisfied state for an older preference used to be
        // handed back as Ready, because the pass limit was treated as "this is the
        // final answer" even when the preference had moved again after it.
        var coordinator = new ExtensionStartupCoordinator();
        var flipper = new FlipProfile(coordinator, ProfileA);

        var result = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", flipper.Reconcile));

        Assert.Equal(ExtensionStartupStatus.Failed, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(3, flipper.Calls);
        // Nothing is cached for either preference, because neither was settled.
        Assert.Null(coordinator.Cached(ProfileA, true));
        Assert.Null(coordinator.Cached(ProfileA, false));
    }

    [Fact]
    public async Task APreferenceThatMovesOnceSettlesOnTheNewestOneAndSaysSo()
    {
        var coordinator = new ExtensionStartupCoordinator();
        var profile = new LateToggleProfile(coordinator, ProfileA);

        // This caller asked for blocking on before the toggle existed.
        var result = await coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", profile.Reconcile));

        // The run applied the newer preference, so that is what the answer reports.
        Assert.Equal(ExtensionStartupStatus.Ready, result.Status);
        Assert.True(result.Succeeded);
        Assert.False(result.DesiredEnabled);
        Assert.True(result.DesiredRequested);
        Assert.False(result.MatchesRequest);
        Assert.False(result.Enabled);
        Assert.Equal(new[] { true, false }, profile.Requested.ToArray());
    }

    [Fact]
    public async Task ARetryAfterALateOwnerFailureSharesTheOriginalBudget()
    {
        // One budget for the whole call. A retry that got a fresh full budget would
        // make the worst case twice as long as the caller was promised.
        var coordinator = new ExtensionStartupCoordinator();
        var budget = TimeSpan.FromMilliseconds(600);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dying = new FakeProfile { Throw = new InvalidOperationException("owner core went away"), Release = release };
        var held = new FakeProfile { Hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };

        var owner = coordinator.EnsureReadyAsync(Request(ProfileA, "window-1", dying.Reconcile), budget);
        await dying.Entered.Task;
        var waiting = coordinator.EnsureReadyAsync(Request(ProfileA, "window-2", held.Reconcile), budget);

        release.SetResult(true);
        await owner;
        var result = await waiting;

        Assert.Equal(ExtensionStartupStatus.TimedOut, result.Status);
        Assert.False(result.Succeeded);
        Assert.True(result.Retried);
        Assert.True(result.Duration >= budget * 0.8,
            $"the waiter must actually wait for its budget, took {result.Duration.TotalMilliseconds:0}ms");
        Assert.True(result.Duration < budget * 1.6,
            $"the retry must not start a second budget, took {result.Duration.TotalMilliseconds:0}ms");
    }

    [Fact]
    public async Task TwoNativeProfilesInsideOneFolder_DoNotShareAReply()
    {
        // The run is serialized on the user data folder, because that is what the
        // environments in one process share. A ready answer is still tied to the
        // profile the caller's control reported, so a second profile cannot be told
        // the first one is ready.
        var coordinator = new ExtensionStartupCoordinator();
        const string folder = @"C:\umber-test\shared-folder";
        var defaultProfile = new FakeProfile { ProfilePathValue = @"C:\umber-test\shared-folder\EBWebView\Default" };
        var second = new FakeProfile { ProfilePathValue = @"C:\umber-test\shared-folder\EBWebView\Profile 2" };

        var first = await coordinator.EnsureReadyAsync(
            new ExtensionStartupRequest(folder, true, "window-1", defaultProfile.Reconcile, defaultProfile.ProfilePathValue));
        var alsoFirst = await coordinator.EnsureReadyAsync(
            new ExtensionStartupRequest(folder, true, "window-2", defaultProfile.Reconcile, defaultProfile.ProfilePathValue));
        var other = await coordinator.EnsureReadyAsync(
            new ExtensionStartupRequest(folder, true, "window-1", second.Reconcile, second.ProfilePathValue));

        Assert.True(first.Succeeded);
        Assert.True(alsoFirst.Succeeded);
        Assert.True(other.Succeeded);
        Assert.Equal(1, defaultProfile.Calls);
        // Same folder, so the run is serialized, but the second profile is not handed
        // the first profile's answer.
        Assert.Equal(1, second.Calls);
        Assert.Equal(2, coordinator.AttemptCount(folder));
    }

    /// <summary>Moves the preference during every pass, so no pass can ever settle.</summary>
    private sealed class FlipProfile
    {
        private readonly ExtensionStartupCoordinator _coordinator;
        private readonly string _key;
        private int _calls;

        public FlipProfile(ExtensionStartupCoordinator coordinator, string key)
        {
            _coordinator = coordinator;
            _key = key;
        }

        public int Calls => Volatile.Read(ref _calls);

        public ExtensionReconcileCore Reconcile => (desiredEnabled, _) =>
        {
            Interlocked.Increment(ref _calls);
            _coordinator.RecordPreference(_key, !desiredEnabled);
            return Task.FromResult(new ExtensionNativeState(true, desiredEnabled, "ublock-id", @"C:\ext", null));
        };
    }

    /// <summary>Moves the preference once, during the first pass.</summary>
    private sealed class LateToggleProfile
    {
        private readonly ExtensionStartupCoordinator _coordinator;
        private readonly string _key;
        private int _calls;

        public LateToggleProfile(ExtensionStartupCoordinator coordinator, string key)
        {
            _coordinator = coordinator;
            _key = key;
        }

        public List<bool> Requested { get; } = new();

        public ExtensionReconcileCore Reconcile => (desiredEnabled, _) =>
        {
            lock (Requested) Requested.Add(desiredEnabled);
            if (Interlocked.Increment(ref _calls) == 1)
                _coordinator.RecordPreference(_key, !desiredEnabled);
            return Task.FromResult(new ExtensionNativeState(true, desiredEnabled, "ublock-id", @"C:\ext", null));
        };
    }

    private sealed class FakeProfile
    {
        public bool Installed { get; set; } = true;

        public TaskCompletionSource<bool>? Hold { get; set; }

        /// <summary>Released before the fake throws, so a failure can be placed in time.</summary>
        public TaskCompletionSource<bool>? Release { get; set; }

        public Exception? Throw { get; set; }

        public string? ProfilePathValue { get; set; }

        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public List<bool> Requested { get; } = new();

        public ExtensionReconcileCore Reconcile => async (desiredEnabled, cancellationToken) =>
        {
            Interlocked.Increment(ref _calls);
            lock (Requested) Requested.Add(desiredEnabled);
            Entered.TrySetResult(true);

            if (Hold is not null) await Hold.Task;
            if (Release is not null) await Release.Task;

            if (Throw is not null) throw Throw;

            return new ExtensionNativeState(
                Installed,
                Installed && desiredEnabled,
                Installed ? "ublock-id" : null,
                Installed ? @"C:\umber-test\extensions\uBlock0_1.73.0" : null,
                ProfilePathValue ?? @"C:\umber-test\profile-a");
        };
    }
}

[CollectionDefinition("DeadlineChecks", DisableParallelization = true)]
public sealed class DeadlineChecksCollection { }
