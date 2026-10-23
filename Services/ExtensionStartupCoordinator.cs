using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UmberBrowser.Helpers;

namespace UmberBrowser.Services;

/// <summary>
/// Runs the extension registration once per real WebView2 user data folder and
/// shares that one run across every window in the process.
///
/// The old code kept a flag per <c>WebViewFactory</c>, which is per window, while
/// the thing it was guarding is per profile. Two windows therefore each kicked off
/// their own registration against one shared folder. The key here is the normalized
/// user data folder, so two windows on one profile share the run and two isolated
/// profiles stay independent.
///
/// The run is serialized per user data folder, which is what the WebView2
/// environments in one process share. A cached answer is additionally tied to the
/// native profile path the caller's control reported, so two profiles inside one
/// folder cannot share a ready answer even if a future version creates a second one.
/// The product itself only ever uses the default profile of its environment, so the
/// folder key and the native profile are the same thing today.
///
/// Four rules the callers depend on:
/// one run at a time per profile, so nothing installs twice;
/// a wait that gives up or is cancelled does not touch that run, because other
/// windows are still waiting on it;
/// the newest preference wins before a run reports completion, and a run that could
/// not settle on the newest preference reports a failure rather than a ready state
/// for the older one; and
/// the whole wait, retries included, is inside one budget, so an owner failure that
/// costs a full budget cannot double it.
/// </summary>
public sealed class ExtensionStartupCoordinator
{
    /// <summary>
    /// How long one caller may wait in total, retries included. Long enough for a cold
    /// extract plus the WebView2 registration, short enough that a failure shows a
    /// page rather than a blank tab. It bounds waiting; it decides nothing.
    /// </summary>
    public static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Preference passes inside one run before it stops chasing the newest value.</summary>
    private const int MaxPreferencePasses = 3;

    private readonly object _profilesLock = new();
    private readonly Dictionary<string, ProfileState> _profiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The canonical key for a WebView2 user data folder. Two spellings of the same
    /// folder have to produce one key or two windows would each own a run.
    /// </summary>
    public static string ProfileKeyFor(string dataDirectory) =>
        AppPaths.NormalizeDirectoryPath(dataDirectory);

    /// <summary>
    /// Records the preference for a profile without running anything. This is how a
    /// toggle that arrives with no live control still reaches the next run.
    /// </summary>
    public void RecordPreference(string profileKey, bool desiredEnabled)
    {
        var state = StateFor(profileKey);
        lock (state.Sync)
        {
            state.DesiredEnabled = desiredEnabled;
            if (desiredEnabled != state.ReadyDesired) state.HasReady = false;
        }
        Trace.WriteLine($"ExtensionStartup: preference profile={state.Key} desired={desiredEnabled}");
    }

    /// <summary>
    /// Returns once this profile holds the requested native state, or with an honest
    /// failure when it could not be proven inside the caller's budget.
    /// </summary>
    public async Task<ExtensionStartupResult> EnsureReadyAsync(
        ExtensionStartupRequest request,
        TimeSpan? waitTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProfileKey))
            throw new ArgumentException("A profile key is required.", nameof(request));
        if (request.Reconcile is null)
            throw new ArgumentException("A reconcile delegate is required.", nameof(request));

        // One budget for the whole call. A retry after an owner failure gets what is
        // left of it, never a fresh one, so the worst case stays bounded.
        var budget = waitTimeout ?? DefaultWaitTimeout;
        var budgetWatch = Stopwatch.StartNew();
        var state = StateFor(request.ProfileKey);
        var retried = false;

        lock (state.Sync)
        {
            // This call is the newest expression of the preference, whether it is a
            // settings toggle or the startup gate reading the same setting.
            state.DesiredEnabled = request.DesiredEnabled;
            if (request.DesiredEnabled != state.ReadyDesired) state.HasReady = false;
        }

        while (true)
        {
            if (TryCached(state, request.DesiredEnabled, request.NativeProfilePath, out var cached))
                return Build(ExtensionStartupStatus.Ready, cached, request, retried, budgetWatch.Elapsed,
                    "already reconciled for this profile, preference and native profile");

            Task<AttemptResult> attempt;
            string attemptOwner;
            lock (state.Sync)
            {
                if (state.InFlight is null || state.InFlight.IsCompleted)
                {
                    attemptOwner = request.OwnerId;
                    state.InFlightOwner = attemptOwner;
                    state.InFlight = RunAttemptAsync(state, request.Reconcile, attemptOwner);
                    attempt = state.InFlight;
                }
                else
                {
                    attemptOwner = state.InFlightOwner ?? string.Empty;
                    attempt = state.InFlight;
                }
            }

            AttemptResult outcome;
            try
            {
                var remaining = budget - budgetWatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    return Build(ExtensionStartupStatus.TimedOut, ExtensionNativeState.None, request,
                        retried, budgetWatch.Elapsed, "wait budget spent before the registration finished");

                outcome = await attempt.WaitAsync(remaining, cancellationToken);
            }
            catch (TimeoutException)
            {
                // Truthful failure: not marked ready, shared run untouched.
                return Build(ExtensionStartupStatus.TimedOut, ExtensionNativeState.None, request,
                    retried, budgetWatch.Elapsed, "wait ended before the registration finished");
            }
            catch (OperationCanceledException)
            {
                // This waiter's own wait was cancelled. The shared run keeps going for
                // the windows that are still waiting on it.
                return Build(ExtensionStartupStatus.Cancelled, ExtensionNativeState.None, request,
                    retried, budgetWatch.Elapsed, "wait cancelled; the shared registration is still running");
            }

            // The preference may have moved again while this caller waited. The answer
            // reported is about the profile's authoritative preference now, not about
            // the one this caller happened to ask for.
            bool authoritative;
            lock (state.Sync) authoritative = state.DesiredEnabled;

            var settled = outcome.Settled
                          && outcome.DesiredApplied == authoritative
                          && ProfileCompatible(request.NativeProfilePath, outcome.Native.ProfilePath);

            if (settled && IsSatisfied(outcome.Native, authoritative))
                return Build(StatusFor(outcome.Native, authoritative), outcome.Native, request,
                    retried, budgetWatch.Elapsed, outcome.Detail, authoritative);

            // The run failed, or could not settle on the newest preference. The usual
            // cause is the window that owned it closing mid registration, so one caller
            // retries on the core it does have, inside what is left of its budget.
            if (!retried
                && !string.Equals(attemptOwner, request.OwnerId, StringComparison.Ordinal)
                && budgetWatch.Elapsed < budget)
            {
                retried = true;
                ReleaseAttempt(state, attempt);
                Trace.WriteLine(
                    $"ExtensionStartup: run owned by {attemptOwner} did not settle, retrying on {request.OwnerId}: {outcome.Detail}");
                continue;
            }

            return Build(ExtensionStartupStatus.Failed, outcome.Native, request, retried, budgetWatch.Elapsed,
                AfterDesired(outcome, authoritative), authoritative);
        }
    }

    /// <summary>
    /// The cached native state for a profile and preference, or null when nothing has
    /// been proven for it yet. Exposed for diagnostics and for the verification runs.
    /// </summary>
    public ExtensionStartupResult? Cached(string profileKey, bool desiredEnabled, string? nativeProfilePath = null)
    {
        var state = StateFor(profileKey);
        if (!TryCached(state, desiredEnabled, nativeProfilePath, out var cached)) return null;
        return Build(ExtensionStartupStatus.Ready, cached,
            new ExtensionStartupRequest(state.Key, desiredEnabled, "cached", NoOpReconcile, nativeProfilePath),
            false, TimeSpan.Zero, "already reconciled for this profile, preference and native profile");
    }

    /// <summary>Number of runs started for a profile, for tests and traces.</summary>
    public int AttemptCount(string profileKey) => StateFor(profileKey).Attempts;

    private static Task<ExtensionNativeState> NoOpReconcile(bool desiredEnabled, CancellationToken cancellationToken) =>
        Task.FromResult(ExtensionNativeState.None);

    private ProfileState StateFor(string profileKey)
    {
        var key = (profileKey ?? string.Empty).Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        lock (_profilesLock)
        {
            if (!_profiles.TryGetValue(key, out var state))
            {
                state = new ProfileState(key);
                _profiles[key] = state;
            }
            return state;
        }
    }

    private static bool TryCached(
        ProfileState state,
        bool desiredEnabled,
        string? nativeProfilePath,
        out ExtensionNativeState native)
    {
        lock (state.Sync)
        {
            native = state.ReadyNative;
            return state.HasReady
                   && state.ReadyDesired == desiredEnabled
                   && IsSatisfied(state.ReadyNative, desiredEnabled)
                   && ProfileCompatible(nativeProfilePath, state.ReadyProfilePath);
        }
    }

    /// <summary>
    /// Two profile paths are compatible when they are the same folder, or when one of
    /// them is unknown. A caller that cannot report its profile gets the run's answer,
    /// because the run read the state from the profile it actually used. Two known
    /// and different paths are never compatible, which is what keeps one profile's
    /// ready answer from being handed to another inside the same user data folder.
    /// </summary>
    private static bool ProfileCompatible(string? requested, string? other) =>
        string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(other)
        || SameProfile(requested, other);

    private static bool SameProfile(string? left, string? right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return AppPaths.NormalizeDirectoryPath(path); }
        catch (Exception) { return path.Trim().TrimEnd(Path.DirectorySeparatorChar); }
    }

    /// <summary>
    /// One registration run. Serialized per profile so a toggle cannot land in the
    /// middle of an install, and never cancelled by a waiter.
    /// </summary>
    private async Task<AttemptResult> RunAttemptAsync(
        ProfileState state,
        ExtensionReconcileCore reconcile,
        string ownerId)
    {
        await state.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            state.Attempts++;
            ExtensionNativeState native = ExtensionNativeState.None;
            var desired = true;

            for (var pass = 0; pass < MaxPreferencePasses; pass++)
            {
                lock (state.Sync) desired = state.DesiredEnabled;

                var passWatch = Stopwatch.StartNew();
                native = await reconcile(desired, CancellationToken.None);
                var satisfied = IsSatisfied(native, desired);
                Trace.WriteLine(
                    $"ExtensionStartup: pass {pass + 1} profile={state.Key} owner={ownerId} desired={desired} " +
                    $"registered={native.Registered} enabled={native.Enabled} satisfied={satisfied} {passWatch.ElapsedMilliseconds}ms");

                bool latest;
                lock (state.Sync) latest = state.DesiredEnabled;

                if (latest == desired)
                {
                    lock (state.Sync)
                    {
                        state.HasReady = satisfied;
                        state.ReadyDesired = desired;
                        state.ReadyNative = native;
                        state.ReadyProfilePath = native.ProfilePath;
                    }
                    return new AttemptResult(satisfied, desired, native, DetailFor(native, satisfied));
                }

                // The preference moved while the work was running. Apply the newer one
                // before reporting completion, so the final state is the newest ask.
                Trace.WriteLine(
                    $"ExtensionStartup: preference moved during pass {pass + 1}, reapplying profile={state.Key} " +
                    $"{desired} -> {latest}");
            }

            // Out of passes with the preference still moving. Nothing is cached, and the
            // answer is a failure: reporting the state reached for the older preference
            // as a ready state for the profile would be a lie.
            lock (state.Sync) state.HasReady = false;
            return new AttemptResult(false, desired, native,
                "the ad blocking preference kept changing while the extension was being reconciled");
        }
        catch (Exception ex)
        {
            // A closed core lands here. The run fails for everyone waiting, the profile
            // is not marked ready, and the next caller with a live core retries.
            lock (state.Sync) state.HasReady = false;
            Trace.WriteLine($"ExtensionStartup: run failed profile={state.Key} owner={ownerId}: {ex}");
            return new AttemptResult(false, false, ExtensionNativeState.None,
                $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private static string DetailFor(ExtensionNativeState native, bool satisfied) =>
        satisfied
            ? native.Detail ?? "the native state matches the current preference"
            : native.Detail ??
              $"uBlock is registered but not in the requested state: registered={native.Registered} " +
              $"enabled={native.Enabled}";

    private static string AfterDesired(AttemptResult outcome, bool authoritative) =>
        outcome.Settled && outcome.DesiredApplied != authoritative
            ? $"the profile is now on {authoritative} but the reconciled state is for {outcome.DesiredApplied}: {outcome.Detail}"
            : outcome.Detail;

    private static ExtensionStartupStatus StatusFor(ExtensionNativeState native, bool desired) =>
        IsSatisfied(native, desired)
            ? (native.Registered ? ExtensionStartupStatus.Ready : ExtensionStartupStatus.NotInstalled)
            : ExtensionStartupStatus.Failed;

    private static bool IsSatisfied(ExtensionNativeState native, bool desired) =>
        desired ? native.Registered && native.Enabled : !native.Registered || !native.Enabled;

    private static void ReleaseAttempt(ProfileState state, Task<AttemptResult> attempt)
    {
        lock (state.Sync)
        {
            if (ReferenceEquals(state.InFlight, attempt)) state.InFlight = null;
        }
    }

    /// <summary>
    /// Builds the reported answer. Success is recomputed from the native state and the
    /// preference being reported, so a mismatch between what was asked and what is
    /// there can never be handed back labelled as a success.
    /// </summary>
    private static ExtensionStartupResult Build(
        ExtensionStartupStatus status,
        ExtensionNativeState native,
        ExtensionStartupRequest request,
        bool retried,
        TimeSpan duration,
        string? detail,
        bool? desiredEnabled = null)
    {
        var desired = desiredEnabled ?? request.DesiredEnabled;
        var asked = request.DesiredEnabled;

        // TimedOut, Cancelled and NoCore describe what happened to the wait and keep
        // their own status. Only a claimed success is re-checked, so a mismatch between
        // the state and the preference being reported can never be handed back as one.
        if (status is not (ExtensionStartupStatus.Ready or ExtensionStartupStatus.NotInstalled))
            return new ExtensionStartupResult(status, false, native, desired, asked, retried,
                request.ProfileKey, detail, duration);

        var succeeded = IsSatisfied(native, desired);
        return new ExtensionStartupResult(
            succeeded ? status : ExtensionStartupStatus.Failed, succeeded, native, desired, asked,
            retried, request.ProfileKey, detail, duration);
    }

    private sealed record AttemptResult(bool Settled, bool DesiredApplied, ExtensionNativeState Native, string Detail);

    private sealed class ProfileState
    {
        public ProfileState(string key) => Key = key;

        public string Key { get; }

        public object Sync { get; } = new();

        /// <summary>Serializes every mutation of this profile.</summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public bool DesiredEnabled { get; set; } = true;

        public Task<AttemptResult>? InFlight { get; set; }

        public string? InFlightOwner { get; set; }

        public bool HasReady { get; set; }

        public bool ReadyDesired { get; set; }

        public ExtensionNativeState ReadyNative { get; set; } = ExtensionNativeState.None;

        /// <summary>The native profile the cached state was read from.</summary>
        public string? ReadyProfilePath { get; set; }

        public int Attempts { get; set; }
    }
}
