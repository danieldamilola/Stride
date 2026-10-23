using System.Diagnostics;
using System.Windows;
using UmberBrowser.Models;

namespace UmberBrowser.Services.UI;

/// <summary>One window's contribution to the startup snapshot.</summary>
public sealed record SnapshotSource(
    string Name,
    IReadOnlyList<(string Id, string Url, string Title, bool IsPinned)> Tabs,
    string? ActiveTabId,
    bool Closing);

/// <summary>
/// Builds the startup snapshot from the windows that are open. Split out from the window
/// manager so the grouping and the rows can be exercised with real windows and a real
/// session file, rather than only through a helper that returns a list of names.
/// </summary>
public static class SessionSnapshot
{
    /// <summary>
    /// The windows whose tabs make up the snapshot when the named window closes.
    /// A window the user already closed is an abandoned cohort and is left out, so a
    /// later restore cannot bring its tabs back. The last window to close has no open
    /// peer, so its own tabs are the snapshot.
    /// </summary>
    public static SnapshotSource[] Cohort(IReadOnlyList<SnapshotSource> windows, string closingName)
    {
        var closing = windows.FirstOrDefault(w => w.Name == closingName);
        var peers = windows.Where(w => !w.Closing && !ReferenceEquals(w, closing)).ToArray();
        return peers.Length > 0 ? peers : closing is null ? [] : [closing];
    }

    /// <summary>
    /// The rows to write, in cohort order then tab order.
    ///
    /// Internal pages are dropped because there is nothing to reopen. Repeat URLs, titles
    /// and pins are carried through unchanged: the restore path creates every row
    /// verbatim, so collapsing repeats here would shorten the list and misalign the saved
    /// active index. Exactly one row carries the active flag, taken from the last window in
    /// the cohort that has an active tab. If that tab is an internal page its row is
    /// dropped with the rest and no row carries the flag, which is the same outcome as a
    /// session that never marked one.
    /// </summary>
    public static (string Url, string Title, bool IsPinned, bool IsActive)[] Rows(IReadOnlyList<SnapshotSource> cohort)
    {
        if (cohort.Count == 0) return [];
        var owner = cohort.LastOrDefault(w => w.ActiveTabId is not null) ?? cohort[0];
        return cohort.SelectMany(w => w.Tabs)
            .Where(t => !InternalUrls.IsInternal(t.Url))
            .Select(t => (t.Url, t.Title, t.IsPinned, IsActive: t.Id == owner.ActiveTabId))
            .ToArray();
    }
}

/// <summary>What a window close managed to write. Every field is what actually happened.</summary>
public sealed record ShutdownWriteResult(
    bool SessionSnapshotWritten,
    bool ClosedTabHistoryCleared,
    bool SettingsSaved,
    bool BrowsingDataCleared,
    bool BrowsingHistoryCleared = true)
{
    /// <summary>True when nothing a later restore or a later open depends on was lost.</summary>
    public bool RecoveryStateIntact => SessionSnapshotWritten && SettingsSaved;

    /// <summary>True when every write this close owed reached its store.</summary>
    public bool Complete => RecoveryStateIntact && ClosedTabHistoryCleared && BrowsingDataCleared && BrowsingHistoryCleared;
}

/// <summary>
/// The writes a window owes on the way out, in the order they have to happen.
///
/// The browser cleanup is best effort. A three second deadline or a COM failure there must
/// not stop the closed tab history or the settings from being written, because those are
/// the parts a later launch reads. Each write reports what it actually did rather than
/// letting one failure hide the rest.
///
/// The snapshot and settings delegates return whether the store put the bytes on disk.
/// They are not "did not throw" callbacks: the real stores swallow write failures, so a
/// void callback would report success for a file that was never written. A delegate that
/// throws is still treated as a failure.
/// </summary>
public sealed class WindowShutdownWrites
{
    /// <summary>True to write the snapshot, false to empty it. Restore off or a data clearing exit empties it.</summary>
    public required bool WriteSnapshot { get; init; }

    /// <summary>True only for the last window with data clearing on.</summary>
    public required bool ClearBrowsingData { get; init; }

    public required Func<bool> WriteSnapshotRows { get; init; }
    public required Func<bool> ClearSnapshot { get; init; }
    public required Func<bool> ClearClosedTabHistory { get; init; }
    public Func<bool>? ClearHistory { get; init; }
    public required Func<bool> SaveSettings { get; init; }
    public required Func<Task> ClearBrowsingDataAsync { get; init; }

    public async Task<ShutdownWriteResult> RunAsync()
    {
        var snapshotWritten = true;
        try
        {
            snapshotWritten = WriteSnapshot ? WriteSnapshotRows() : ClearSnapshot();
            if (!snapshotWritten) Trace.WriteLine("Startup snapshot write did not reach its store");
        }
        catch (Exception ex)
        {
            snapshotWritten = false;
            Trace.WriteLine($"Startup snapshot write failed: {ex.Message}");
        }

        var browsingDataCleared = true;
        var closedHistoryCleared = true;
        var browsingHistoryCleared = true;
        if (ClearBrowsingData)
        {
            try { await ClearBrowsingDataAsync(); }
            catch (Exception ex)
            {
                browsingDataCleared = false;
                Trace.WriteLine($"Browsing data clear failed or timed out: {ex.Message}");
            }

            // The closed tab history is on disk, so it has to go too or a profile that was
            // cleared could still hand back pages the user asked to erase.
            try { closedHistoryCleared = ClearClosedTabHistory(); }
            catch (Exception ex)
            {
                closedHistoryCleared = false;
                Trace.WriteLine($"Closed tab history clear threw: {ex.Message}");
            }
            if (!closedHistoryCleared) Trace.WriteLine("Closed tab history clear did not reach its store");

            if (ClearHistory != null)
            {
                try { browsingHistoryCleared = ClearHistory(); }
                catch (Exception ex)
                {
                    browsingHistoryCleared = false;
                    Trace.WriteLine($"Browsing history clear threw: {ex.Message}");
                }
                if (!browsingHistoryCleared) Trace.WriteLine("Browsing history clear did not reach its store");
            }
        }

        var settingsSaved = true;
        try
        {
            settingsSaved = SaveSettings();
            if (!settingsSaved) Trace.WriteLine("Settings save did not reach its store");
        }
        catch (Exception ex)
        {
            settingsSaved = false;
            Trace.WriteLine($"Settings save failed: {ex.Message}");
        }

        return new ShutdownWriteResult(snapshotWritten, closedHistoryCleared, settingsSaved, browsingDataCleared, browsingHistoryCleared);
    }
}