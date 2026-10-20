using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// Bounded, persistent history of tabs the user closed, oldest first so the
/// newest closure sits at the end and is what Ctrl+Shift+T reopens next.
/// Writes are atomic: one complete file replace, never a partial list, so a
/// crash mid-close cannot leave a half written history that loses real tabs.
///
/// Durability contract. The file is the record. A change counts as durable only
/// once the file holds it, so a change that fails to reach the file is rolled back
/// in memory and reported as not done. A failed write is recoverable and never
/// throws, because the caller is a tab being closed.
///
/// A closure whose write failed is not thrown away. It is kept as a pending entry,
/// so this process can reopen it, and the next write that lands saves it with the
/// rest. Nothing reports it as saved: <see cref="Record"/> still answers false, and
/// false means the file does not hold the entry. A pending entry is lost on
/// restart, because the file never received it.
/// </summary>
public interface IClosedTabStore
{
    /// <summary>
    /// Closed tabs oldest first, so the newest closure is last. Read only: this
    /// never writes, so reading the history cannot alter it, and the returned list
    /// is a copy. It includes closures that never reached the file and are held
    /// only in this process.
    /// </summary>
    IReadOnlyList<ClosedTabRecord> Load();

    /// <summary>
    /// Appends a closure to the newest end and trims the oldest past the cap.
    /// A URL that is not an absolute navigable address is ignored, because it
    /// could not be reopened. URLs are stored whole, never clipped.
    ///
    /// False covers both a URL that could not be reopened, which is remembered
    /// nowhere, and a write that failed, which leaves the closure remembered only
    /// in this process.
    /// </summary>
    /// <returns>True when the closure is on disk.</returns>
    bool Record(string url, string title, bool wasPinned);

    /// <summary>
    /// Removes and returns the newest closure, or null when there is nothing to
    /// take. Only that one entry is consumed.
    ///
    /// A closure that was on disk is consumed only once the file no longer holds
    /// it, so null also covers a removal whose write failed: reporting it would
    /// open the same tab again on the next launch. A closure that was never on
    /// disk is consumed with no write at all and is returned, because there is
    /// nothing durable to double open and a later write will not bring it back.
    /// </summary>
    ClosedTabRecord? TakeNewest();

    /// <summary>Number of remembered closures.</summary>
    int Count { get; }

    /// <summary>
    /// True when the last read found a file it could not parse. While this is
    /// set, writes and deletes are refused so the unreadable bytes survive rather
    /// than being replaced by a shorter list built without them.
    /// </summary>
    bool LastReadFailed { get; }

    /// <summary>True when the most recent write or delete did not reach the file.</summary>
    bool LastWriteFailed { get; }

    /// <summary>
    /// Forgets every remembered closure, on disk and in memory alike. False means
    /// the file still holds some, so they replay on the next launch.
    /// </summary>
    /// <returns>True when the file no longer holds any closure.</returns>
    bool Clear();

    /// <summary>
    /// Forgets every remembered closure and erases the backing file, for a
    /// clear-on-exit request that should leave nothing behind on disk. False means
    /// the file is still there, and so are the closures in it.
    /// </summary>
    /// <returns>True when the file is gone.</returns>
    bool ClearAndDelete();
}