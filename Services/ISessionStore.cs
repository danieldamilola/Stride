namespace StrideBrowser.Services;

/// <summary>Session entry for saving/restoring tabs across restarts.</summary>
public record SessionEntry(string Url, string Title, bool IsPinned, bool IsActive = false);

/// <summary>Interface for the session persistence layer.</summary>
public interface ISessionStore
{
    /// <summary>
    /// The rows the last run saved, in the order it saved them. A read only:
    /// nothing here validates, deduplicates or drops rows, so what a session
    /// holds is what comes back.
    /// </summary>
    List<SessionEntry> Load();

    /// <summary>
    /// Writes the rows and returns whether the file now holds them. The answer
    /// comes from the write itself, so a disk fault, a locked file or an
    /// unwritable path reports false rather than looking like a save.
    /// </summary>
    bool TrySave(IEnumerable<(string Url, string Title, bool IsPinned, bool IsActive)> tabs);

    /// <summary>Writes the rows. A write that fails is logged and never throws.</summary>
    void Save(IEnumerable<(string Url, string Title, bool IsPinned, bool IsActive)> tabs);

    /// <summary>
    /// Empties the startup snapshot so a later run with restore enabled cannot
    /// resurrect tabs the user already closed by exiting. Writes an empty list
    /// rather than deleting the file, and touches nothing else: history,
    /// downloads, one tab lists and the closed tab history are separate files and
    /// are left alone.
    /// </summary>
    /// <returns>True when the file now holds no rows.</returns>
    bool TryClear();

    /// <summary>
    /// Empties the startup snapshot. A write that fails is logged and never throws.
    /// </summary>
    void Clear();

    /// <summary>
    /// Empties the startup snapshot and deletes it from disk, for a clear-on-exit
    /// request that should leave nothing behind. Still touches only this file.
    /// </summary>
    void ClearAndDelete();
}
