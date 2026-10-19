using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security;
using StrideBrowser.Helpers;

namespace StrideBrowser.Services;

/// <summary>How much crash dump history Stride keeps on disk.</summary>
/// <param name="MaxAge">A dump older than this is removed regardless of the other limits.</param>
/// <param name="MaxFiles">At most this many dumps are kept, newest first.</param>
/// <param name="MaxTotalBytes">At most this many bytes of dumps are kept, newest first.</param>
public sealed record CrashDumpLimits(TimeSpan MaxAge, int MaxFiles, long MaxTotalBytes);

/// <summary>What one pass did.</summary>
/// <param name="FolderAccepted">
/// False when the folder was refused, which is the safe outcome. Nothing was deleted in
/// that case, and nothing is deleted anywhere else either: this is the only place Stride
/// removes files.
/// </param>
/// <param name="Retained">Dumps the pass chose to keep. A dump it could not read is not counted here.</param>
/// <param name="RetainedBytes">Their total size at the time of the pass.</param>
/// <param name="Deleted">Dumps the pass actually removed.</param>
/// <param name="StillPresent">
/// Dumps the pass chose to remove but that are still on disk afterwards, usually because
/// another process held them open. They are reported separately rather than being folded
/// into <paramref name="Retained"/>, because the pass did not choose to keep them.
/// </param>
/// <param name="StillPresentBytes">Their total size, so what is actually on disk is visible.</param>
/// <param name="Unreadable">
/// Entries the pass could not inspect at all. Their size and fate are unknown, which is
/// why they are a count and not a byte total.
/// </param>
/// <param name="WithinLimits">
/// True only when what is still on disk is inside every limit. A deletion that failed can
/// leave the folder over quota, and that is reported as false rather than smoothed over.
/// </param>
public sealed record CrashDumpPruneResult(
    bool FolderAccepted,
    string ReportFolder,
    string Detail,
    int Retained,
    long RetainedBytes,
    int Deleted,
    int StillPresent,
    long StillPresentBytes,
    int Unreadable,
    bool WithinLimits);

/// <summary>
/// Bounds the crash dumps WebView2 leaves in its own report folder.
///
/// The browser is told not to send crash data to Microsoft's endpoint, and local minidumps
/// are the documented result of that, so they accumulate. This keeps them from growing
/// without bound.
///
/// What it will touch, and nothing else:
///   - the folder the WebView2 environment reports as its failure report folder,
///   - only if that folder is inside this run's own user data directory,
///   - only if no part of the path between that directory and the report folder is a
///     reparse point. A junction one level up defeats a string comparison just as well as
///     one at the folder itself, so every component is checked, including the root.
///   - only *.dmp files sitting directly in it. Subdirectories are not walked.
///   - only files that are not reparse points.
///
/// Deleting is best effort. A dump that is held open or still being written fails to
/// delete, is counted in StillPresent, and is retried on the next pass. Expected
/// filesystem failures become a result rather than an exception. Anything unexpected faults
/// the task, and the caller logs that fault instead of losing it.
///
/// The containment checks are static. They read the path, then delete through it. That is
/// enough for the case the checks exist for, a link that was already in place, and it is
/// not a claim of atomic protection against a process that swaps a component out between
/// the check and the delete. The boundary is re-checked immediately before the deletions
/// so a folder that has already become unsafe is not used, which narrows the window but
/// does not close it.
/// </summary>
public static class CrashDumpRetention
{
    /// <summary>Seven days, five dumps, 100 MiB. The limits Stride documents.</summary>
    public static readonly CrashDumpLimits DefaultLimits =
        new(TimeSpan.FromDays(7), MaxFiles: 5, MaxTotalBytes: 100L * 1024 * 1024);

    private const string DumpExtension = ".dmp";

    /// <summary>
    /// One pass per user data directory for the life of the process. Every window in the
    /// process shares one WebView2 user data folder and so one report folder, and they
    /// would otherwise each start a pass over the same files.
    ///
    /// The stored value is a Lazy created with ExecutionAndPublication, which is what
    /// makes it one execution rather than one stored task. ConcurrentDictionary.GetOrAdd
    /// runs its value factory outside any lock, so several callers racing on a new key
    /// each built and started their own work before one of them won the add. Only the
    /// winner's Lazy is ever asked for its value; a Lazy that lost the race is never read,
    /// so its factory never runs.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Lazy<Task<CrashDumpPruneResult>>> Started =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Starts the pass once per user data directory and returns the shared task. The
    /// caller does not await it on any navigation or UI path; it runs on the thread pool.
    /// </summary>
    public static Task<CrashDumpPruneResult> PruneAsync(
        string? reportFolder, string? userDataFolder, CrashDumpLimits? limits = null)
    {
        var key = string.IsNullOrWhiteSpace(userDataFolder)
            ? "<none>"
            : SafeNormalize(userDataFolder) ?? userDataFolder;

        var entry = Started.GetOrAdd(key, _ => new Lazy<Task<CrashDumpPruneResult>>(
            () => Task.Run(() => Prune(reportFolder, userDataFolder, limits ?? DefaultLimits, DateTime.UtcNow)),
            LazyThreadSafetyMode.ExecutionAndPublication));

        return entry.Value;
    }

    /// <summary>
    /// Removes dumps that fall outside the limits. Synchronous so a test drives it
    /// directly, with no Application, no WebView2 and no clock of its own.
    ///
    /// Filesystem problems this pass expects, denied listing, a folder that disappears, a
    /// file that cannot be stat'ed, are turned into a result. Anything else is allowed to
    /// propagate, so the caller's continuation records it rather than the pass quietly
    /// reporting success.
    /// </summary>
    public static CrashDumpPruneResult Prune(
        string? reportFolder, string? userDataFolder, CrashDumpLimits limits, DateTime nowUtc)
    {
        try
        {
            return PruneCore(reportFolder, userDataFolder, limits, nowUtc);
        }
        catch (Exception ex) when (IsExpectedFilesystemFault(ex))
        {
            return Refuse(reportFolder ?? string.Empty,
                $"the failure report folder could not be read: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static CrashDumpPruneResult PruneCore(
        string? reportFolder, string? userDataFolder, CrashDumpLimits limits, DateTime nowUtc)
    {
        var folder = reportFolder ?? string.Empty;

        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(userDataFolder))
            return Refuse(folder, "no failure report folder or no user data folder");

        var normalizedRoot = SafeNormalize(userDataFolder);
        var normalizedFolder = SafeNormalize(folder);
        if (normalizedRoot is null || normalizedFolder is null)
            return Refuse(folder, "the report folder or user data folder is not a usable path");

        // The report folder has to be this Stride run's own. This is the check that stops
        // a wrong or hostile path from pointing the deletion anywhere else.
        if (!AppPaths.IsSameOrAncestor(normalizedRoot, normalizedFolder))
            return Refuse(folder, $"'{folder}' is not inside this run's user data folder '{normalizedRoot}'");

        if (!Directory.Exists(normalizedFolder))
            return Complete(folder, "no failure report folder yet", [], 0, 0, 0, 0, 0, true);

        // Every component from the user data folder down to the report folder has to be a
        // real directory here, not a link. A junction at an intermediate level such as
        // EBWebView puts the report folder physically somewhere else while every string
        // comparison still says it is inside the profile, so the chain is walked rather
        // than trusting the containment check alone.
        var link = FirstReparseComponent(normalizedRoot, normalizedFolder);
        if (link is not null)
            return Refuse(folder, $"'{link}' between the user data folder and the report folder is a reparse point");

        return PruneWithin(folder, normalizedRoot, normalizedFolder, limits, nowUtc);
    }

    private static CrashDumpPruneResult PruneWithin(
        string folder, string normalizedRoot, string normalizedFolder,
        CrashDumpLimits limits, DateTime nowUtc)
    {
        var dumps = new List<DumpFile>();
        var unreadable = 0;

        // Enumeration is inside a try. A folder that can be stat'ed but not listed, which
        // is what a deny ACE on list directory or some security software produces, throws
        // from EnumerateFiles itself rather than from any individual entry.
        string[] paths;
        try
        {
            paths = Directory.GetFileSystemEntries(normalizedFolder, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (IsExpectedFilesystemFault(ex))
        {
            return Refuse(folder,
                $"the failure report folder could not be listed: {ex.GetType().Name}: {ex.Message}");
        }

        foreach (var path in paths)
        {
            // Top level only, and dumps only. Anything else in the folder is somebody
            // else's file as far as this pass is concerned.
            if (!string.Equals(Path.GetExtension(path), DumpExtension, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) { unreadable++; continue; }
                if ((info.Attributes & FileAttributes.Directory) != 0) continue;
                if (!info.Exists) continue;
                dumps.Add(new DumpFile(info.FullName, info.LastWriteTimeUtc, info.Length));
            }
            catch (Exception ex) when (IsExpectedFilesystemFault(ex))
            {
                unreadable++;
                Trace.WriteLine($"CrashDumpRetention could not read {path}: {ex.Message}");
            }
        }

        // Newest first, and one pass decides all three limits. A dump past the age limit
        // goes no matter how much room is left. A dump past the file or byte count goes
        // in favour of a newer one. A single dump bigger than the whole byte budget is
        // removed as well: it can never fit, so keeping it would break the bound it was
        // meant to respect.
        var ordered = dumps
            .OrderByDescending(d => d.ModifiedUtc)
            .ThenBy(d => d.Path, StringComparer.Ordinal)
            .ToList();

        var retain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remove = new List<DumpFile>();
        long retainedBytes = 0;

        foreach (var dump in ordered)
        {
            var tooOld = nowUtc - dump.ModifiedUtc > limits.MaxAge;
            var tooMany = retain.Count >= limits.MaxFiles;
            var tooBig = retainedBytes + dump.Bytes > limits.MaxTotalBytes;
            if (tooOld || tooMany || tooBig) { remove.Add(dump); continue; }
            retain.Add(dump.Path);
            retainedBytes += dump.Bytes;
        }

        // Re-check the boundary immediately before anything is deleted, so a folder that
        // became unsafe while the folder was being read is not used. This narrows the
        // window between checking and deleting; it is not an atomic guarantee.
        if (remove.Count > 0)
        {
            var link = FirstReparseComponent(normalizedRoot, normalizedFolder);
            if (link is not null)
                return Refuse(folder, $"'{link}' became a reparse point before the dumps were removed");
        }

        var deleted = 0;
        var stillPresent = 0;
        long stillPresentBytes = 0;

        foreach (var dump in remove)
        {
            try
            {
                File.Delete(dump.Path);
                deleted++;
            }
            catch (Exception ex) when (IsExpectedFilesystemFault(ex))
            {
                // Held open or still being written. It stays, it is counted, and the next
                // pass tries again. Quota is reported as unmet rather than assumed.
                stillPresent++;
                stillPresentBytes += dump.Bytes;
                Trace.WriteLine($"CrashDumpRetention left {dump.Path} in place: {ex.Message}");
            }
        }

        var onDisk = retain.Count + stillPresent;
        var onDiskBytes = retainedBytes + stillPresentBytes;
        // A failed deletion means at least one configured limit was not met. An entry
        // we could not inspect also makes the on-disk count and size unknown, so this
        // result cannot certify that the folder is within limits.
        var withinLimits = unreadable == 0
            && stillPresent == 0
            && onDisk <= limits.MaxFiles
            && onDiskBytes <= limits.MaxTotalBytes;

        return Complete(folder,
            $"retained {retain.Count} dump(s) {retainedBytes} byte(s); deleted {deleted}; " +
            $"still present {stillPresent} {stillPresentBytes} byte(s); unreadable {unreadable}; " +
            $"within limits {(withinLimits ? "yes" : "no")}",
            retain, retainedBytes, deleted, stillPresent, stillPresentBytes, unreadable, withinLimits);
    }

    /// <summary>
    /// The first component of the path from the user data folder down to the report folder
    /// that carries FileAttributes.ReparsePoint, including the root and the folder itself.
    /// Returns null when every component is a plain directory.
    ///
    /// The comparison is lexical on normalized paths. A component that is a link is
    /// refused outright rather than resolved, because resolving it is what would let a
    /// deletion escape the profile.
    /// </summary>
    internal static string? FirstReparseComponent(string userDataRoot, string reportFolder)
    {
        var relative = reportFolder.StartsWith(userDataRoot, StringComparison.OrdinalIgnoreCase)
            ? reportFolder[userDataRoot.Length..]
            : string.Empty;

        var current = userDataRoot;
        try
        {
            if (IsReparsePoint(current)) return current;
        }
        catch (Exception ex) when (IsExpectedFilesystemFault(ex))
        {
            Trace.WriteLine($"CrashDumpRetention could not inspect '{current}': {ex.Message}");
            return current;
        }

        foreach (var segment in relative.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                if (IsReparsePoint(current)) return current;
            }
            catch (Exception ex) when (IsExpectedFilesystemFault(ex))
            {
                Trace.WriteLine($"CrashDumpRetention could not inspect '{current}': {ex.Message}");
                return current;
            }
        }

        return null;
    }

    private static bool IsReparsePoint(string path)
    {
        var info = new DirectoryInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static CrashDumpPruneResult Complete(
        string folder, string detail, HashSet<string> retained, long retainedBytes,
        int deleted, int stillPresent, long stillPresentBytes, int unreadable, bool withinLimits) =>
        new(true, folder, detail, retained.Count, retainedBytes, deleted,
            stillPresent, stillPresentBytes, unreadable, withinLimits);

    private static CrashDumpPruneResult Refuse(string folder, string reason)
    {
        Trace.WriteLine($"CrashDumpRetention did not prune: {reason}");
        return new CrashDumpPruneResult(false, folder, reason, 0, 0, 0, 0, 0, 0, false);
    }

    private static bool IsExpectedFilesystemFault(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException
            or NotSupportedException;

    private static string? SafeNormalize(string path)
    {
        try { return AppPaths.NormalizeDirectoryPath(path); }
        catch (Exception) { return null; }
    }

    private readonly record struct DumpFile(string Path, DateTime ModifiedUtc, long Bytes);
}
