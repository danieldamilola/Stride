using System;
using System.Threading.Tasks;

namespace UmberBrowser.Services;

/// <summary>How one extension startup, gate or toggle ended.</summary>
public enum ExtensionStartupStatus
{
    /// <summary>The profile holds uBlock and the native enable state is what was asked for.</summary>
    Ready,

    /// <summary>Nothing is installed and ad blocking is off, so no unpack was needed.</summary>
    NotInstalled,

    /// <summary>No live WebView2 was available to run the profile calls on.</summary>
    NoCore,

    /// <summary>
    /// The wait ended before the registration finished. Nothing is marked ready,
    /// and the shared work is still running for whoever asks next.
    /// </summary>
    TimedOut,

    /// <summary>
    /// The caller's own wait was cancelled, which usually means the window is going
    /// away. The shared work keeps running for the other windows.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The reconciliation threw, or the native state never reached what the profile's
    /// current preference asks for. A registered folder that is not an active extension
    /// lands here.
    /// </summary>
    Failed
}

/// <summary>
/// Whether filtering has been observed, which is a different thing from being
/// registered. WebView2 has no API that reports it, so production code only ever
/// says Unverified. A controlled run that sees the shipped filters block a known
/// harmless request is the only thing allowed to set BlockingVerified.
/// </summary>
public enum FilterReadiness
{
    Unverified,
    BlockingVerified
}

/// <summary>
/// What WebView2 reports about uBlock in one profile, read back from the profile
/// after the calls rather than assumed from a folder on disk.
/// </summary>
public sealed record ExtensionNativeState(
    bool Registered,
    bool Enabled,
    string? ExtensionId = null,
    string? ExtensionFolder = null,
    string? ProfilePath = null,
    string? Detail = null)
{
    public static ExtensionNativeState None { get; } = new(false, false);
}

/// <summary>
/// The outcome of asking for the profile to be ready.
///
/// <see cref="Succeeded"/> means the native state satisfies
/// <see cref="DesiredEnabled"/>, which is the profile's authoritative preference.
/// <see cref="DesiredRequested"/> is what this caller asked for. The two differ when a
/// newer toggle landed while this caller was waiting, and that difference is the
/// honest answer rather than a success for a state nobody wants.
/// </summary>
public sealed record ExtensionStartupResult(
    ExtensionStartupStatus Status,
    bool Succeeded,
    ExtensionNativeState Native,
    bool DesiredEnabled,
    bool DesiredRequested,
    bool Retried,
    string ProfileKey,
    string? Detail,
    TimeSpan Duration,
    FilterReadiness Readiness = FilterReadiness.Unverified)
{
    public bool Registered => Native.Registered;

    public bool Enabled => Native.Enabled;

    /// <summary>
    /// True only when a controlled test observed the filters blocking. Native
    /// registration and a stable IsEnabled never set this. A status of Ready means the
    /// registration and the current enabled state, and nothing about filtering.
    /// </summary>
    public bool BlockingVerified => Readiness == FilterReadiness.BlockingVerified;

    /// <summary>True when this caller's own request is what the profile now holds.</summary>
    public bool MatchesRequest => DesiredEnabled == DesiredRequested;

    public static ExtensionStartupResult NoCore(string profileKey, bool desiredEnabled, string detail) =>
        new(ExtensionStartupStatus.NoCore, false, ExtensionNativeState.None, desiredEnabled, desiredEnabled,
            false, profileKey, detail, TimeSpan.Zero);

    public override string ToString() =>
        $"{Status} registered={Registered} enabled={Enabled} desired={DesiredEnabled} " +
        $"requested={DesiredRequested} retried={Retried} profile={ProfileKey} in {Duration.TotalMilliseconds:0}ms" +
        (string.IsNullOrEmpty(Detail) ? "" : $" ({Detail})");
}

/// <summary>
/// Does the work for one profile. Supplied by the caller because only the caller
/// knows which thread owns the WebView2 control and where the files live.
/// </summary>
public delegate Task<ExtensionNativeState> ExtensionReconcileCore(
    bool desiredEnabled,
    System.Threading.CancellationToken cancellationToken);

/// <summary>
/// One caller asking for its profile to be ready. <see cref="OwnerId"/> names the
/// window, so a registration that dies with its window can be retried on another.
/// <see cref="NativeProfilePath"/> is the profile the caller's control actually talks
/// to when the caller can read it, and two profiles inside one user data folder must
/// not share a ready answer.
/// </summary>
public sealed record ExtensionStartupRequest(
    string ProfileKey,
    bool DesiredEnabled,
    string OwnerId,
    ExtensionReconcileCore Reconcile,
    string? NativeProfilePath = null);
