using System.Diagnostics;
using System.IO;
using System.Text.Json;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// Persists the closed tab history to disk so Ctrl+Shift+T reopens the newest
/// closure even across restarts.
///
/// The file holds a JSON array in chronological order, oldest first. Keeping
/// order in the array rather than sorting on read means the ordering is a
/// property of the stored bytes, so a reader that never saw the writes still
/// gets the right sequence.
/// <para>
/// A write that fails does not throw and does not lose the tab the user closed. The
/// closure stays in memory as a pending entry so this process can reopen it, while
/// the store keeps answering false, because false means the file does not hold it.
/// Nothing pretends a pending entry is saved: it is gone after a restart, and the
/// next write that lands saves it with everything else. The store is one per
/// process, so those pending entries are available to every window in the run.
/// </para>
///
/// This store is separate from <see cref="SessionStore"/>. The session file is
/// the open-tabs snapshot used for restore on startup and is rewritten from
/// scratch on exit. This file is only appended to when the user closes a tab,
/// and one entry is removed per reopen.
/// </summary>
public sealed class ClosedTabStore : IClosedTabStore
{
    /// <summary>
    /// How many closures to remember. Matches the in memory cap the tab engine
    /// used before the history became persistent. Oldest entries fall off the
    /// front so the file cannot grow without limit.
    /// </summary>
    public const int DefaultCapacity = 50;

    /// <summary>
    /// How far a stored field may grow, and what happens when it does.
    ///
    /// A URL is never clipped. Clipping one silently changes where the reopened
    /// tab navigates to, which turns a remembered tab into a different tab, and
    /// legitimate URLs can be well past 4096 characters. Anything that cannot be
    /// parsed as an absolute http, https, file or internal URL is not remembered
    /// at all, because reopening it could not produce the tab the user closed.
    ///
    /// A title is clipped at this length. A title only labels the reopened tab, so
    /// an over long one is cosmetic rather than a change of destination.
    /// </summary>
    public const int MaxTitleLength = 512;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly int _capacity;
    private readonly object _gate = new();
    private List<ClosedTabRecord>? _cache;

    /// <summary>
    /// How many of the newest entries in <see cref="_cache"/> are known only to
    /// this process, because the write that should have saved them never landed.
    /// They are always the tail of the cache, so the merged list reads oldest
    /// first the way the file does.
    ///
    /// A pending entry is a real closure the user can still reopen with
    /// Ctrl+Shift+T until the process ends. It is not a saved one. A restart loses
    /// it, because the file never received it, and the store never claims
    /// otherwise: <see cref="Record"/> still answers false, because false means
    /// the file does not hold the entry. The next write that does land saves these
    /// entries along with everything else, in the order they were closed.
    /// </summary>
    private int _pendingCount;

    /// <summary>
    /// Replaces the durable write when set. Null, the default, writes for real.
    /// This exists so a test can make a write fail, or fail once and then succeed,
    /// without needing a read-only file, a locked handle or a full disk. The
    /// product never sets it and no production behaviour depends on it.
    /// </summary>
    private readonly Action<string, string>? _write;

    public ClosedTabStore() : this(null, DefaultCapacity) { }

    /// <summary>Test seam: pass an explicit path and capacity.</summary>
    public ClosedTabStore(string? filePath, int capacity)
        : this(filePath, capacity, null) { }

    /// <summary>Full test seam, including a controllable writer fault.</summary>
    public ClosedTabStore(string? filePath, int capacity, Action<string, string>? write)
    {
        _filePath = filePath ?? AppPaths.ClosedTabsFile;
        _capacity = Math.Max(1, capacity);
        _write = write;
    }

    public int Count
    {
        get { lock (_gate) { return Working().Count; } }
    }

    /// <summary>
    /// The remembered closures, oldest first. Reads only. This method never writes
    /// the file, so inspecting the history cannot change it, and it hands back a
    /// copy so the caller cannot reach into the store's own list.
    ///
    /// The list includes closures this process holds only in memory because their
    /// write failed. They are real reopenable tabs for this run, and they are not
    /// on disk.
    /// </summary>
    public IReadOnlyList<ClosedTabRecord> Load()
    {
        lock (_gate) { return new List<ClosedTabRecord>(Working()); }
    }

    /// <summary>
    /// Records a closure.
    ///
    /// Durability: the return value is about the file and nothing else. True means
    /// the file holds the entry. False means it does not, for one of two reasons.
    /// Either the address could never be reopened, so nothing is remembered, or the
    /// write failed. A failed write keeps the closure in memory, so this process
    /// can still reopen it, and it is flushed by the next write that lands. What
    /// it is not is saved: a restart loses it, because the file never took it.
    ///
    /// The failure is recoverable and never throws. The caller is a tab being
    /// closed, and a write fault must not turn into a fault in the close itself.
    /// </summary>
    /// <returns>True when the closure is on disk.</returns>
    public bool Record(string url, string title, bool wasPinned)
    {
        var entry = ClosedTabRecord.Create(url, title, wasPinned, DateTime.UtcNow);
        if (entry is null) return false;

        lock (_gate)
        {
            // Work on a copy so a failed write cannot leave the cache holding a
            // closure the file never received without the store knowing which
            // ones those are.
            var working = new List<ClosedTabRecord>(Working()) { entry };
            // Oldest fall off the front, so the newest closure is always last.
            var trimmed = TrimTo(working);
            return Commit(trimmed, PendingTailOf(trimmed, _pendingCount + 1), previous: null);
        }
    }

    /// <summary>
    /// Removes and returns the newest closure.
    ///
    /// Durability has two cases. A closure that reached the file is only consumed
    /// once the file no longer contains it. If that write fails the in memory
    /// state is rolled back, so the entry stays available and the next launch
    /// replays it. Returning an entry that is still on disk would open it twice, so
    /// on failure this returns null and the history is left exactly as it was.
    ///
    /// A closure that never reached the file, because its own write failed, is
    /// only in this process, so dropping it needs no write at all and works even
    /// while the writer is still broken. It is returned, it is consumed for good,
    /// and a later successful write does not put it back, because it was never
    /// saved in the first place.
    /// </summary>
    public ClosedTabRecord? TakeNewest()
    {
        lock (_gate)
        {
            var working = new List<ClosedTabRecord>(Working());
            if (working.Count == 0) return null;

            var newest = working[^1];
            working.RemoveAt(working.Count - 1);

            if (_pendingCount > 0)
            {
                // The newest closure is pending, so the file says nothing about
                // it and a write is not owed. The disk still holds exactly the
                // entries in front of it.
                _cache = working;
                _pendingCount--;
                return newest;
            }

            // On failure Commit restores the entry into the cache itself, so
            // nothing is left to undo here. Reporting null is what keeps a removal
            // that never reached the disk from replaying on the next launch.
            return Commit(working, [], previous: newest) ? newest : null;
        }
    }

    /// <summary>
    /// The state this process is working from: the cache, or the file on first
    /// use. Callers hold the gate and must not hand the returned list out.
    /// </summary>
    private List<ClosedTabRecord> Working()
    {
        return _cache is null ? _cache = ReadUnsafe() : _cache;
    }

    /// <summary>
    /// The last <paramref name="count"/> entries of <paramref name="items"/>, which
    /// is the closure tail: the entries already pending plus whatever this call is
    /// adding. Callers hold the gate.
    /// </summary>
    private static List<ClosedTabRecord> PendingTailOf(List<ClosedTabRecord> items, int count)
    {
        var take = Math.Min(count, items.Count);
        return take <= 0 ? [] : items.GetRange(items.Count - take, take);
    }

    /// <summary>
    /// The closures this process holds only in memory, oldest first. Callers hold
    /// the gate.
    /// </summary>
    private List<ClosedTabRecord> PendingTail() => PendingTailOf(Working(), _pendingCount);

    /// <summary>
    /// The part of the cache the file is known to hold, oldest first, which is
    /// everything in front of the pending tail. Callers hold the gate.
    /// </summary>
    private List<ClosedTabRecord> DurablePart()
    {
        var current = Working();
        var keep = Math.Min(_pendingCount, current.Count);
        return current.GetRange(0, current.Count - keep);
    }

    /// <summary>
    /// Forgets every remembered closure, both the ones on disk and the pending ones.
    /// A failed write forgets nothing and answers false, because the closures are
    /// still there to be replayed.
    /// </summary>
    /// <returns>True when the file no longer holds any closure.</returns>
    public bool Clear()
    {
        lock (_gate)
        {
            // An empty array rather than a deleted file. Load already treats a
            // missing file as empty, so this only avoids the window where a
            // concurrent read sees no file at all.
            return Commit([], PendingTail(), previous: null);
        }
    }

    /// <summary>
    /// Forgets every remembered closure and erases the file from disk.
    ///
    /// Used by clear-on-exit. If the delete fails the closures stay on disk and the
    /// in memory state is reloaded, so the next launch replays them rather than
    /// silently losing tabs the user still expects to be able to reopen. Closures
    /// that were never on disk survive a failed delete for the same reason, and go
    /// only when the delete succeeds or a write does.
    /// </summary>
    /// <returns>True when the file is gone.</returns>
    public bool ClearAndDelete()
    {
        lock (_gate)
        {
            if (LastReadFailed && File.Exists(_filePath))
            {
                Trace.WriteLine(
                    $"ClosedTabStore refusing to delete while the existing history is unreadable, leaving {_filePath} untouched");
                // The requested change did not reach the disk, so this is reported
                // the same way a failed delete is.
                LastWriteFailed = true;
                return false;
            }

            try
            {
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
                var leftover = _filePath + ".tmp";
                if (File.Exists(leftover)) File.Delete(leftover);
                _cache = [];
                _pendingCount = 0;
                LastWriteFailed = false;
                return true;
            }
            catch (Exception ex)
            {
                LastWriteFailed = true;
                Trace.WriteLine($"ClosedTabStore.ClearAndDelete failed: {ex.Message}");

                // A delete that did not delete leaves the durable half exactly as
                // this process was already using it, so the file is only re-read
                // when it really is gone, where the reload is the state a restart
                // would read. Either way the closures that never reached the file
                // stay on top: they are real closures, and the next Ctrl+Shift+T
                // in this run has to find them.
                var rebuilt = File.Exists(_filePath)
                    ? DurablePart()
                    : new List<ClosedTabRecord>(ReadUnsafe());
                rebuilt.AddRange(PendingTail());
                _cache = TrimTo(rebuilt);
                _pendingCount = Math.Min(_pendingCount, _cache.Count);
                return false;
            }
        }
    }

    /// <summary>
    /// True when the last read found a file it could not parse.
    ///
    /// When that happens the entries are unavailable to this process. The file is
    /// deliberately left exactly as it is: an unreadable history may be a
    /// transient condition, a locked file or a schema this build does not know, and
    /// overwriting it would destroy real closures on the strength of a parse
    /// error. Writing is refused instead, so the original bytes survive until
    /// something can read them.
    /// </summary>
    public bool LastReadFailed { get; private set; }

    private List<ClosedTabRecord> ReadUnsafe()
    {
        LastReadFailed = false;
        try
        {
            if (!File.Exists(_filePath)) return [];

            var json = File.ReadAllText(_filePath);
            List<ClosedTabRecord>? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<List<ClosedTabRecord>>(json, JsonOpts);
            }
            catch (JsonException ex)
            {
                LastReadFailed = true;
                Trace.WriteLine(
                    $"ClosedTabStore.Load could not parse the history at {_filePath}, leaving it untouched: {ex.Message}");
                return [];
            }

            if (parsed is null) return [];

            // Drop anything that could not be reopened rather than handing a
            // caller a record that navigates somewhere else. URLs are carried
            // through whole. This does not set LastReadFailed: entries that do
            // parse are still real closures with real order, and skipping the
            // unusable ones is the point of normalising them.
            var clean = new List<ClosedTabRecord>(parsed.Count);
            foreach (var record in parsed)
            {
                var normalized = ClosedTabRecord.Create(
                    record?.Url ?? "", record?.Title, record?.WasPinned ?? false,
                    record?.ClosedAtUtc ?? default);
                if (normalized is not null) clean.Add(normalized);
            }
            return clean;
        }
        catch (Exception ex)
        {
            // A history that cannot be read must not take the browser down. The
            // bytes on disk are left alone for the same reason as a parse failure.
            LastReadFailed = true;
            Trace.WriteLine($"ClosedTabStore.Load failed: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Writes the given state to disk and, only on success, adopts it as the
    /// store's state.
    ///
    /// The cache is published after the file is written, never before. Publishing
    /// first would let a failed write leave this process believing a change was
    /// durable when it was not, and the next launch would disagree.
    ///
    /// <paramref name="previous"/> is what the cache held before this change, so
    /// the caller can roll its own working copy back. It is set only for a removal,
    /// where the file still holds the entry, and is null for an append or a clear,
    /// where the durable half is simply reloaded from the file because the file
    /// still holds the previous contents.
    ///
    /// <paramref name="pendingTail"/> is the closure tail, the newest entries that
    /// exist only in memory. They are kept at the newest end after a failed write
    /// so this process can still reopen them, and a later successful write saves
    /// them along with everything else.
    /// </summary>
    /// <returns>True when the file now holds exactly <paramref name="items"/>.</returns>
    private bool Commit(List<ClosedTabRecord> items, List<ClosedTabRecord> pendingTail, ClosedTabRecord? previous)
    {
        // Never overwrite a history this process could not read. Those bytes are
        // the only surviving record of real closures, so a later successful write
        // must not be allowed to replace them with a list built without them.
        if (LastReadFailed && File.Exists(_filePath))
        {
            Trace.WriteLine(
                $"ClosedTabStore refusing to write while the existing history is unreadable, leaving {_filePath} untouched");
            UndoWrite(items, pendingTail, previous);
            return false;
        }

        try
        {
            var json = JsonSerializer.Serialize(items, JsonOpts);
            if (_write is not null) _write(_filePath, json);
            else AtomicFileWriter.WriteAllText(_filePath, json);
            _cache = new List<ClosedTabRecord>(items);
            // The file now holds every entry in the list, the pending tail included,
            // so nothing is memory only any more.
            _pendingCount = 0;
            LastWriteFailed = false;
            return true;
        }
        catch (Exception ex)
        {
            LastWriteFailed = true;
            Trace.WriteLine($"ClosedTabStore write failed: {ex.Message}");
            UndoWrite(items, pendingTail, previous);
            return false;
        }
    }

    /// <summary>
    /// Puts the in memory state back to what the file still holds, after a change
    /// that did not reach it. Callers hold the gate.
    ///
    /// Nothing is read back here. The cache already is the file's contents as far as
    /// this process knows, from the read that ran on first use, and whatever stopped
    /// the write usually stops a read too: an open handle denies both. Re-reading
    /// under such a fault came back empty and threw away closures that were on disk,
    /// and it left the store believing the file was unreadable, which refuses every
    /// later write for the rest of the run. A failed write is not a failed read, so
    /// it leaves that flag alone.
    ///
    /// The pending entries go on top of the durable half, at the newest end, so
    /// Ctrl+Shift+T still finds the closures this process could not save.
    /// </summary>
    private void UndoWrite(List<ClosedTabRecord> items, List<ClosedTabRecord> pendingTail, ClosedTabRecord? previous)
    {
        if (previous is not null)
        {
            // A removal failed. The file still holds the entry, so put it back at
            // the newest end and keep the order the file has. Nothing was pending,
            // because a pending entry is never removed by a write.
            _cache = TrimTo(new List<ClosedTabRecord>(items) { previous });
            _pendingCount = 0;
            return;
        }

        // An append or a clear failed. The file still holds the list this process
        // was already using, so the durable half stays exactly as it is.
        var restored = DurablePart();
        restored.AddRange(pendingTail);
        _cache = TrimTo(restored);
        // Trimming drops from the front, so the tail survives whole.
        _pendingCount = Math.Min(pendingTail.Count, _cache.Count);
    }

    /// <summary>True when the most recent write attempt did not reach the disk.</summary>
    public bool LastWriteFailed { get; private set; }

    private List<ClosedTabRecord> TrimTo(List<ClosedTabRecord> items)
    {
        if (items.Count > _capacity)
            items.RemoveRange(0, items.Count - _capacity);
        return items;
    }
}