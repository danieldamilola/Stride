using System.IO;
using System.Security.AccessControl;
using System.Threading;
using UmberBrowser.Engine;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// The crash reporting control, checked against the options the product actually builds
/// and against a real isolated report folder tree.
///
/// Two separate claims live here and only one of them is a runtime observation. That the
/// options say IsCustomCrashReportingEnabled is true is a fact about the object the
/// product constructs. That Windows then sends nothing to Microsoft's endpoint is
/// Microsoft's documented effect of that property, not something these tests measure.
///
/// Every tree here is created under one temp subdirectory this test owns and is deleted
/// on Dispose. The links it creates point at folders inside that same subdirectory, so a
/// junction here cannot reach a real profile or a real user's files.
/// </summary>
public sealed class CrashReportingTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("umber-crashdumps");

    public void Dispose()
    {
        ClearReadOnlyFiles();
        // Delete links before their contents so nothing follows one out of the tree.
        RemoveLinks(_root.FullName);
        try { _root.Delete(true); } catch { /* best effort */ }
    }

    private void ClearReadOnlyFiles()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root.FullName, "*", SearchOption.AllDirectories))
            {
                try { if (new FileInfo(file).IsReadOnly) new FileInfo(file).IsReadOnly = false; } catch { }
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Removes the directory links this test created, without following them. Only links
    /// whose target resolves inside this test's own temp tree are touched, so the sweep
    /// cannot delete anything belonging to the filesystem outside it.
    /// </summary>
    private static void RemoveLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string current;
            string[] children;
            try
            {
                current = pending.Pop();
                children = Directory.GetDirectories(current);
            }
            catch { continue; }

            foreach (var child in children)
            {
                if (!IsReparsePoint(child))
                {
                    pending.Push(child);
                    continue;
                }

                try
                {
                    var info = new DirectoryInfo(child);
                    if (info.LinkTarget is { } target && IsInside(root, target))
                        info.Delete(false);
                }
                catch { /* best effort */ }
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0; }
        catch { return false; }
    }

    private static bool IsInside(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
        return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // ── The option the product sets ───────────────────────────────────────────────────

    [Fact]
    public void EveryEnvironmentIsCreatedWithCustomCrashReportingOn()
    {
        var options = WebViewFactory.BuildEnvironmentOptions(new Models.BrowserSettings());

        Assert.True(options.IsCustomCrashReportingEnabled);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TurningSmartScreenOrForcingDarkModeDoesNotDisturbIt(bool smartScreen, bool darkMode)
    {
        var options = WebViewFactory.BuildEnvironmentOptions(new Models.BrowserSettings
        {
            SmartScreenEnabled = smartScreen,
            ForceDarkMode = darkMode
        });

        Assert.True(options.IsCustomCrashReportingEnabled);
        // The existing options survive alongside it.
        Assert.True(options.AreBrowserExtensionsEnabled);
    }

    [Fact]
    public void TrackingPreventionStaysOnAndIsNotDisabledByAnySetting()
    {
        var settings = new Models.BrowserSettings { TrackingPreventionMode = Models.TrackingPreventionMode.Strict };

        Assert.True(WebViewFactory.BuildEnvironmentOptions(settings).EnableTrackingPrevention);
        Assert.True(WebViewFactory.BuildEnvironmentOptions(new Models.BrowserSettings()).EnableTrackingPrevention);
    }

    [Fact]
    public void TheFailureReportFolderIsReadOnlyOnThePinnedSdk()
    {
        // Pruning uses the folder the platform reports. If this ever gains a setter the
        // helper could point it somewhere, so the shape of the API is worth pinning.
        var property = typeof(Microsoft.Web.WebView2.Core.CoreWebView2Environment)
            .GetProperty("FailureReportFolderPath");

        Assert.NotNull(property);
        Assert.False(property!.CanWrite);
    }

    // ── Retention against a real tree ─────────────────────────────────────────────────

    private string ReportFolder(string name = "reports")
    {
        var dataDir = Path.Combine(_root.FullName, name, "WebView2");
        var reports = Path.Combine(dataDir, "Crashpad", "reports");
        Directory.CreateDirectory(reports);
        return reports;
    }

    private static string WriteDump(string folder, string name, int bytes, TimeSpan age, DateTime now)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, now - age);
        return path;
    }

    private static string[] Remaining(string folder) =>
        Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void ADumpOlderThanTheAgeLimitIsRemoved()
    {
        var reports = ReportFolder();
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        WriteDump(reports, "old.dmp", 1024, TimeSpan.FromDays(30), now);
        WriteDump(reports, "recent.dmp", 1024, TimeSpan.FromDays(1), now);

        var result = CrashDumpRetention.Prune(reports, Path.Combine(_root.FullName, "reports", "WebView2"),
            CrashDumpRetention.DefaultLimits, now);

        Assert.True(result.FolderAccepted);
        Assert.Equal(new[] { "recent.dmp" }, Remaining(reports));
        Assert.Equal(1, result.Deleted);
    }

    [Fact]
    public void OnlyTheNewestDumpsAreKeptWhenThereAreMoreThanFive()
    {
        var reports = ReportFolder();
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 8; i++)
            WriteDump(reports, $"dump{i}.dmp", 1024, TimeSpan.FromDays(i), now);

        var result = CrashDumpRetention.Prune(reports, Path.Combine(_root.FullName, "reports", "WebView2"),
            CrashDumpRetention.DefaultLimits, now);

        Assert.Equal(new[] { "dump0.dmp", "dump1.dmp", "dump2.dmp", "dump3.dmp", "dump4.dmp" }, Remaining(reports));
        Assert.Equal(5, Remaining(reports).Length);
        Assert.Equal(3, result.Deleted);
    }

    [Fact]
    public void TheTotalSizeIsBoundedAndTheOldestGoesFirst()
    {
        var reports = ReportFolder();
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var limits = new CrashDumpLimits(TimeSpan.FromDays(7), MaxFiles: 100, MaxTotalBytes: 100 * 1024);
        WriteDump(reports, "a.dmp", 40 * 1024, TimeSpan.FromDays(3), now);
        WriteDump(reports, "b.dmp", 40 * 1024, TimeSpan.FromDays(2), now);
        WriteDump(reports, "c.dmp", 40 * 1024, TimeSpan.FromDays(1), now);

        CrashDumpRetention.Prune(reports, Path.Combine(_root.FullName, "reports", "WebView2"), limits, now);

        Assert.Equal(new[] { "b.dmp", "c.dmp" }, Remaining(reports));
    }

    [Fact]
    public void ASingleDumpBiggerThanTheWholeBudgetIsRemoved()
    {
        // It can never fit, so keeping it would break the bound it was meant to respect.
        var reports = ReportFolder();
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var limits = new CrashDumpLimits(TimeSpan.FromDays(7), MaxFiles: 5, MaxTotalBytes: 1024);
        WriteDump(reports, "huge.dmp", 4096, TimeSpan.FromHours(1), now);
        WriteDump(reports, "small.dmp", 256, TimeSpan.FromHours(2), now);

        var result = CrashDumpRetention.Prune(reports, Path.Combine(_root.FullName, "reports", "WebView2"), limits, now);

        Assert.Equal(new[] { "small.dmp" }, Remaining(reports));
        Assert.Equal(1, result.Deleted);
    }

    [Fact]
    public void FilesThatAreNotDumpsAreNeverTouched()
    {
        var reports = ReportFolder();
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        WriteDump(reports, "old.dmp", 1024, TimeSpan.FromDays(90), now);
        File.WriteAllText(Path.Combine(reports, "settings.txt"), "not a dump");
        File.WriteAllText(Path.Combine(reports, "evidence.log"), "not a dump either");
        var subdir = Directory.CreateDirectory(Path.Combine(reports, "pending"));
        File.WriteAllBytes(Path.Combine(subdir.FullName, "nested.dmp"), new byte[4096]);

        CrashDumpRetention.Prune(reports, Path.Combine(_root.FullName, "reports", "WebView2"),
            CrashDumpRetention.DefaultLimits, now);

        Assert.True(File.Exists(Path.Combine(reports, "settings.txt")));
        Assert.True(File.Exists(Path.Combine(reports, "evidence.log")));
        // Subdirectories are not walked, so a nested file is left alone as well.
        Assert.True(File.Exists(Path.Combine(subdir.FullName, "nested.dmp")));
        Assert.False(File.Exists(Path.Combine(reports, "old.dmp")));
    }

    [Fact]
    public void AReportFolderOutsideThisRunsUserDataFolderIsRefusedAndNothingIsDeleted()
    {
        var outside = _root.CreateSubdirectory("someone-elses-folder").FullName;
        var victim = WriteDump(outside, "keep.dmp", 1024, TimeSpan.FromDays(365), DateTime.UtcNow);
        var now = DateTime.UtcNow;

        var result = CrashDumpRetention.Prune(outside, Path.Combine(_root.FullName, "umber", "WebView2"),
            CrashDumpRetention.DefaultLimits, now);

        Assert.False(result.FolderAccepted);
        Assert.Equal(0, result.Deleted);
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public void TraversalOutOfTheUserDataFolderIsRefused()
    {
        var dataDir = Path.Combine(_root.FullName, "traverse", "WebView2");
        Directory.CreateDirectory(dataDir);
        var outside = _root.CreateSubdirectory("traverse-target").FullName;
        var victim = WriteDump(outside, "keep.dmp", 1024, TimeSpan.FromDays(365), DateTime.UtcNow);

        var result = CrashDumpRetention.Prune(
            Path.Combine(dataDir, "..", "..", "traverse-target"), dataDir,
            CrashDumpRetention.DefaultLimits, DateTime.UtcNow);

        Assert.False(result.FolderAccepted);
        Assert.True(File.Exists(victim));
    }

    // ── Containment: a link anywhere on the path refuses the pass ─────────────────────

    /// <summary>
    /// Builds the tree the acceptance review reproduced, inside this test's own temp
    /// directory. The victim sits at the far end of the link, so a pass that follows the
    /// link instead of refusing it destroys it.
    ///
    /// <paramref name="linkTheDataRootItself"/> moves the link up to the user data
    /// folder, which is the other end of the chain the product has to check.
    /// </summary>
    private (string DataDir, string ReportFolder, string Victim, string Bystander)
        BuildLinkedTree(string name, bool linkTheDataRootItself)
    {
        if (linkTheDataRootItself)
        {
            var realProfile = _root.CreateSubdirectory($"{name}-real").FullName;
            Directory.CreateDirectory(Path.Combine(realProfile, "Crashpad", "reports"));
            var rootVictim = WriteDump(realProfile, "victim.dmp", 1024, TimeSpan.FromDays(400), DateTime.UtcNow);
            var rootBystander = Path.Combine(realProfile, "bystander.txt");
            File.WriteAllText(rootBystander, "not a dump, and outside the profile");

            var holder = _root.CreateSubdirectory($"{name}-holder").FullName;
            var linked = Path.Combine(holder, "WebView2");
            if (!CreateDirectoryLink(linked, realProfile))
                throw Xunit.Sdk.SkipException.ForSkip("this run cannot create a directory junction");

            return (linked, Path.Combine(linked, "Crashpad", "reports"), rootVictim, rootBystander);
        }

        var dataDir = Path.Combine(_root.FullName, name, "WebView2");
        var intermediate = Path.Combine(dataDir, "EBWebView");
        Directory.CreateDirectory(dataDir);

        // The junction points at a tree that is outside the profile by position, even
        // though every string comparison still places it inside.
        var target = _root.CreateSubdirectory($"{name}-ebview-target").FullName;
        var reports = Path.Combine(target, "Crashpad", "reports");
        Directory.CreateDirectory(reports);
        var victim = WriteDump(reports, "victim.dmp", 1024, TimeSpan.FromDays(400), DateTime.UtcNow);
        var bystander = Path.Combine(target, "bystander.txt");
        File.WriteAllText(bystander, "not a dump, and outside the profile");

        if (!CreateDirectoryLink(intermediate, target))
            throw Xunit.Sdk.SkipException.ForSkip("this run cannot create a directory junction");

        return (dataDir, Path.Combine(intermediate, "Crashpad", "reports"), victim, bystander);
    }

    [Fact]
    public void AJunctionOnAnAncestorOfTheReportFolderIsRefusedAndTheVictimSurvives()
    {
        var tree = BuildLinkedTree("ancestor", linkTheDataRootItself: false);

        var result = CrashDumpRetention.Prune(
            tree.ReportFolder, tree.DataDir, CrashDumpRetention.DefaultLimits, DateTime.UtcNow);

        Assert.False(result.FolderAccepted);
        Assert.Equal(0, result.Deleted);
        Assert.True(File.Exists(tree.Victim), "a dump outside the profile was deleted");
        Assert.True(File.Exists(tree.Bystander));
        Assert.Contains("reparse point", result.Detail);
    }

    [Fact]
    public void AUserDataFolderThatIsItselfALinkIsRefused()
    {
        var tree = BuildLinkedTree("rootlink", linkTheDataRootItself: true);

        var result = CrashDumpRetention.Prune(
            tree.ReportFolder, tree.DataDir, CrashDumpRetention.DefaultLimits, DateTime.UtcNow);

        Assert.False(result.FolderAccepted);
        Assert.Equal(0, result.Deleted);
        Assert.True(File.Exists(tree.Victim));
        Assert.True(File.Exists(tree.Bystander));
    }

    [Fact]
    public void AReparsePointInPlaceOfTheReportFolderIsRefused()
    {
        var dataDir = Path.Combine(_root.FullName, "linked", "WebView2");
        var realReports = _root.CreateSubdirectory("linked-real").FullName;
        var victim = WriteDump(realReports, "keep.dmp", 1024, TimeSpan.FromDays(365), DateTime.UtcNow);
        Directory.CreateDirectory(dataDir);
        var linked = Path.Combine(dataDir, "reports");

        var created = CreateDirectoryLink(linked, realReports);
        try
        {
            if (!created) throw Xunit.Sdk.SkipException.ForSkip("this run cannot create a directory junction");

            var result = CrashDumpRetention.Prune(linked, dataDir, CrashDumpRetention.DefaultLimits, DateTime.UtcNow);

            Assert.False(result.FolderAccepted);
            Assert.True(File.Exists(victim));
        }
        finally
        {
            if (created) { try { Directory.Delete(linked, false); } catch { } }
        }
    }

    [Fact]
    public void AnOrdinaryReportDirectoryWithALinkElsewhereInTheProfileStillWorks()
    {
        // The control for the containment rules. A link somewhere else in the profile must
        // not stop the pass from doing its job in the folder the engine actually named.
        var reports = ReportFolder("valid");
        var dataDir = Path.Combine(_root.FullName, "valid", "WebView2");
        var unrelated = Path.Combine(dataDir, "Extensions", "somedir");
        Directory.CreateDirectory(unrelated);
        CreateDirectoryLink(Path.Combine(dataDir, "Extensions", "linked"), unrelated);
        Assert.True(IsReparsePoint(Path.Combine(dataDir, "Extensions", "linked")),
            "the unrelated link was not created, so this case proves nothing");

        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        WriteDump(reports, "old.dmp", 1024, TimeSpan.FromDays(90), now);
        WriteDump(reports, "fresh.dmp", 1024, TimeSpan.FromDays(1), now);

        var result = CrashDumpRetention.Prune(reports, dataDir, CrashDumpRetention.DefaultLimits, now);

        Assert.True(result.FolderAccepted);
        Assert.Equal(1, result.Deleted);
        Assert.Equal(new[] { "fresh.dmp" }, Remaining(reports));
    }

    // ── Failures stay best effort and are reported honestly ──────────────────────────

    [Fact]
    public void ADumpThatCannotBeDeletedIsCountedSeparatelyAndThePassStillFinishes()
    {
        // Hold the file without delete sharing, as another process may while WebView2 is
        // still writing it. The failure has to stay local: startup cannot be taken down.
        var reports = ReportFolder();
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var locked = WriteDump(reports, "locked.dmp", 1024, TimeSpan.FromDays(90), now);
        using var blocker = HoldWithoutDelete(locked);
        WriteDump(reports, "also-old.dmp", 1024, TimeSpan.FromDays(90), now);

        var result = CrashDumpRetention.Prune(reports, Path.Combine(_root.FullName, "reports", "WebView2"),
            CrashDumpRetention.DefaultLimits, now);

        Assert.True(result.FolderAccepted);
        Assert.Equal(1, result.Deleted);
        Assert.True(File.Exists(locked));
        // The other stale dump still went, so one bad file does not stop the pass.
        Assert.False(File.Exists(Path.Combine(reports, "also-old.dmp")));
    }

#if !BEFORE_REPAIR
    [Fact]
    public void ADumpThatStayedIsReportedAsStillPresentRatherThanAsRetained()
    {
        // The numbers have to describe what is on disk, not what the pass wished for. A
        // dump that was chosen for removal and could not be deleted is neither retained
        // nor gone, and its bytes have to appear somewhere.
        var reports = ReportFolder("accounting");
        var dataDir = Path.Combine(_root.FullName, "accounting", "WebView2");
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var locked = WriteDump(reports, "locked.dmp", 65536, TimeSpan.FromDays(90), now);
        using var blocker = HoldWithoutDelete(locked);
        WriteDump(reports, "fresh.dmp", 4096, TimeSpan.FromDays(1), now);

        var result = CrashDumpRetention.Prune(reports, dataDir, CrashDumpRetention.DefaultLimits, now);

        Assert.Equal(1, result.Retained);
        Assert.Equal(4096, result.RetainedBytes);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(1, result.StillPresent);
        Assert.Equal(65536, result.StillPresentBytes);
        // The locked dump is 90 days old, so it exceeds the age limit even though its
        // count and bytes fit. The result cannot certify success after the deletion fails.
        Assert.False(result.WithinLimits);
        Assert.Contains("still present 1 65536 byte(s)", result.Detail);
    }

    [Fact]
    public void AnUninspectableReparseEntryCannotBeCertifiedWithinLimits()
    {
        var reports = ReportFolder("unknown-entry");
        var dataDir = Path.Combine(_root.FullName, "unknown-entry", "WebView2");
        var target = _root.CreateSubdirectory("unknown-entry-target").FullName;
        WriteDump(target, "victim.dmp", 128, TimeSpan.FromDays(90), DateTime.UtcNow);
        var unknown = Path.Combine(reports, "unknown.dmp");
        var created = CreateDirectoryLink(unknown, target);
        if (!created) throw Xunit.Sdk.SkipException.ForSkip("this run cannot create an owned directory junction");

        try
        {
            var now = DateTime.UtcNow;
            var result = CrashDumpRetention.Prune(reports, dataDir,
                new CrashDumpLimits(TimeSpan.FromDays(7), 5, 100 * 1024), now);

            Assert.True(result.FolderAccepted);
            Assert.Equal(1, result.Unreadable);
            Assert.False(result.WithinLimits);
            Assert.True(File.Exists(Path.Combine(target, "victim.dmp")));
        }
        finally
        {
            try { Directory.Delete(unknown, false); } catch { }
        }
    }

    [Fact]
    public void AFailedDeletionThatLeavesTheFolderOverQuotaIsNotReportedAsWithinLimits()
    {
        var reports = ReportFolder("overquota");
        var dataDir = Path.Combine(_root.FullName, "overquota", "WebView2");
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var limits = new CrashDumpLimits(TimeSpan.FromDays(7), MaxFiles: 5, MaxTotalBytes: 100 * 1024);
        var first = WriteDump(reports, "a.dmp", 40 * 1024, TimeSpan.FromDays(5), now);
        using var firstBlocker = HoldWithoutDelete(first);
        var second = WriteDump(reports, "b.dmp", 40 * 1024, TimeSpan.FromDays(4), now);
        using var secondBlocker = HoldWithoutDelete(second);
        WriteDump(reports, "c.dmp", 40 * 1024, TimeSpan.FromDays(3), now);

        var result = CrashDumpRetention.Prune(reports, dataDir, limits, now);

        Assert.Equal(2, result.Retained);
        Assert.Equal(1, result.StillPresent);
        Assert.Equal(40 * 1024, result.StillPresentBytes);
        Assert.False(result.WithinLimits);
        Assert.Contains("within limits no", result.Detail);
    }
#endif

    [Fact]
    public void ASecondPassOverTheSameTreeChangesNothingFurther()
    {
        var reports = ReportFolder();
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 7; i++)
            WriteDump(reports, $"dump{i}.dmp", 2048, TimeSpan.FromDays(i), now);
        var dataDir = Path.Combine(_root.FullName, "reports", "WebView2");

        var first = CrashDumpRetention.Prune(reports, dataDir, CrashDumpRetention.DefaultLimits, now);
        var afterFirst = Remaining(reports);
        var second = CrashDumpRetention.Prune(reports, dataDir, CrashDumpRetention.DefaultLimits, now);

        Assert.Equal(afterFirst, Remaining(reports));
        Assert.Equal(0, second.Deleted);
        Assert.Equal(5, Remaining(reports).Length);
    }

    [Fact]
    public void AMissingReportFolderIsNotAFailure()
    {
        var dataDir = Path.Combine(_root.FullName, "fresh", "WebView2");
        Directory.CreateDirectory(dataDir);

        var result = CrashDumpRetention.Prune(
            Path.Combine(dataDir, "Crashpad", "reports"), dataDir, CrashDumpRetention.DefaultLimits, DateTime.UtcNow);

        Assert.True(result.FolderAccepted);
        Assert.Equal(0, result.Deleted);
    }

    [Fact]
    public void AReportFolderThatVanishedBeforeThePassIsAcceptedAndDeletesNothing()
    {
        var dataDir = Path.Combine(_root.FullName, "vanished", "WebView2");
        var reports = Path.Combine(dataDir, "Crashpad", "reports");
        Directory.CreateDirectory(reports);
        WriteDump(reports, "old.dmp", 1024, TimeSpan.FromDays(90), DateTime.UtcNow);
        Directory.Delete(reports, true);

        var result = CrashDumpRetention.Prune(reports, dataDir, CrashDumpRetention.DefaultLimits, DateTime.UtcNow);

        Assert.True(result.FolderAccepted);
        Assert.Equal(0, result.Deleted);
    }

    [Fact]
    public void ANullReportFolderIsRefusedWithoutThrowing()
    {
        var result = CrashDumpRetention.Prune(null, Path.Combine(_root.FullName, "anything"), CrashDumpRetention.DefaultLimits, DateTime.UtcNow);

        Assert.False(result.FolderAccepted);
    }

    [Fact]
    public void AReportFolderThatCanBeStatEdButNotListedIsReportedRatherThanThrown()
    {
        // Denying list directory on a folder the process can still stat reproduces what
        // security software or a locked down profile does. The pass has to answer with a
        // result so the caller records it, because the caller's continuation is the only
        // place the outcome is written down.
        var reports = ReportFolder("denied");
        var dataDir = Path.Combine(_root.FullName, "denied", "WebView2");
        WriteDump(reports, "old.dmp", 1024, TimeSpan.FromDays(90), DateTime.UtcNow);

        var identity = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var applied = false;

        try
        {
            var deny = new FileSystemAccessRule(identity, FileSystemRights.ListDirectory, AccessControlType.Deny);
            var restricted = new DirectorySecurity();
            restricted.AddAccessRule(deny);
            FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(reports), restricted);
            applied = true;
        }
        catch (Exception ex)
        {
            // Without WRITE_DAC on this folder the case cannot be built here. The pass is
            // still required not to throw, which is what the assertions below check.
            File.WriteAllText(Path.Combine(_root.FullName, "denied-acl-unavailable.txt"), ex.Message);
        }

        try
        {
            var result = CrashDumpRetention.Prune(reports, dataDir, CrashDumpRetention.DefaultLimits, DateTime.UtcNow);

            Assert.False(result.FolderAccepted);
            Assert.Equal(0, result.Deleted);
            Assert.True(File.Exists(Path.Combine(reports, "old.dmp")));
            Assert.Contains("could not be listed", result.Detail);
        }
        finally
        {
            try
            {
                if (applied)
                {
                    var restore = new DirectorySecurity();
                    restore.SetAccessRuleProtection(true, false);
                    FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(reports), restore);
                }
            }
            catch { /* best effort: Dispose removes the whole tree anyway */ }
        }
    }

    [Fact]
    public void TheDefaultLimitsAreSevenDaysFiveFilesAndOneHundredMebibytes()
    {
        Assert.Equal(TimeSpan.FromDays(7), CrashDumpRetention.DefaultLimits.MaxAge);
        Assert.Equal(5, CrashDumpRetention.DefaultLimits.MaxFiles);
        Assert.Equal(100L * 1024 * 1024, CrashDumpRetention.DefaultLimits.MaxTotalBytes);
    }

    // ── One pass, actually one pass ───────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentWindowsSharingOneProfileRunOnePassNotSeveral()
    {
        // Assert.Same on the returned task holds for any input, so it cannot show how many
        // passes ran. What shows it is a read-only dump: the pass writes one trace line per
        // file it fails to delete, and two lines for the same path can only come from two
        // passes over the same tree.
        var trials = 40;
        var trialsWithMoreThanOnePass = 0;

        for (var trial = 0; trial < trials; trial++)
        {
            var name = $"once-{trial}";
            var dataDir = Path.Combine(_root.FullName, name, "WebView2");
            var reports = Path.Combine(dataDir, "Crashpad", "reports");
            Directory.CreateDirectory(reports);
            var locked = WriteDump(reports, "locked.dmp", 64, TimeSpan.FromDays(400), DateTime.UtcNow);
            using var blocker = HoldWithoutDelete(locked);

            var listener = new CollectingTraceListener();
            System.Diagnostics.Trace.Listeners.Add(listener);
            try
            {
                using var barrier = new Barrier(3);
                var tasks = new Task<CrashDumpPruneResult>[2];
                for (var i = 0; i < 2; i++)
                {
                    tasks[i] = Task.Run(() =>
                    {
                        barrier.SignalAndWait();
                        return CrashDumpRetention.PruneAsync(reports, dataDir);
                    });
                }

                barrier.SignalAndWait();
                var results = await Task.WhenAll(tasks);

                // Every caller gets the same result, which is the sharing half.
                Assert.All(results, r => Assert.Equal(reports, r.ReportFolder));
                Assert.Equal(1, results.Distinct().Count());

                var failures = listener.Lines.Count(l =>
                    l.Contains(locked, StringComparison.OrdinalIgnoreCase)
                    && l.Contains("left", StringComparison.OrdinalIgnoreCase));
                if (failures > 1) trialsWithMoreThanOnePass++;
            }
            finally
            {
                System.Diagnostics.Trace.Listeners.Remove(listener);
            }
        }

        Assert.Equal(0, trialsWithMoreThanOnePass);
    }

    private sealed class CollectingTraceListener : System.Diagnostics.TraceListener
    {
        public List<string> Lines { get; } = new();
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            lock (Lines) if (message is not null) Lines.Add(message);
        }
    }

    private static FileStream HoldWithoutDelete(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    private static bool CreateDirectoryLink(string linkPath, string targetPath) =>
        RunMklink($"/J \"{linkPath}\" \"{targetPath}\"", () => Directory.Exists(linkPath));

    private static bool RunMklink(string arguments, Func<bool> created)
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c mklink " + arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null || !process.WaitForExit(10_000)) return false;
            return process.ExitCode == 0 && created();
        }
        catch { return false; }
    }

}
