using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// The preparation to decision wiring, driven through <c>DecideAsync</c> against real
/// files. <see cref="ExtensionManager.Plan"/> is pure and its own tests feed it
/// constants, so nothing there can catch a pass that prepares and then decides from
/// the wrong answer. Here a manager reads its own folder, prepares when it needs to,
/// and the plan is read off the result.
///
/// The failure these cover: the pass used to build its inputs from
/// <c>LastPreparation</c>, which only refreshes when a preparation runs. A cold pass
/// rewrote assets, so the next pass over a preference toggle, which needed no file
/// work, still saw <c>AssetsRewritten = true</c> and chose RemoveAndRegister. A healthy
/// uBlock was removed and re-added on a toggle. The property is also shared by every
/// window on the manager, so one profile's preparation could answer for another.
/// </summary>
public sealed class ExtensionPreparationToPlanTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("umber-ext-wire");

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root.FullName, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                if (info.IsReadOnly) info.IsReadOnly = false;
            }
        }
        catch { /* best effort */ }
        _root.Delete(true);
    }

    private static readonly Lazy<string> ShippedZipPath = new(FindShippedZip);

    /// <summary>
    /// The zip that ships in Resources\Extensions. The project copies it next to the
    /// test binary, so these run against the same 4 MB package the product unpacks and
    /// the assets normalization changes real bytes rather than a fixture.
    /// </summary>
    private static string ShippedZip() => ShippedZipPath.Value;

    private static string FindShippedZip()
    {
        var name = "uBlock0_1.73.0.chromium.zip";
        var candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Extensions", name);
        if (File.Exists(candidate)) return candidate;
        throw new FileNotFoundException(
            $"the shipped uBlock zip is not beside the test binary: {candidate}", name);
    }

    /// <summary>The assets.json as the shipped zip holds it, before any normalization.</summary>
    private static string ShippedAssetsText()
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(ShippedZip());
        var entry = zip.Entries.First(e => e.FullName.EndsWith("assets/assets.json", StringComparison.Ordinal));
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    /// <summary>
    /// A manager over an isolated directory that prepares from the shipped zip. No
    /// network is reachable: the zip is always present, so the download branch is
    /// never the one under test.
    /// </summary>
    private ExtensionManager NewManager(string extensionsSubDir = "extensions")
    {
        var bundled = Path.Combine(_root.FullName, "bundled");
        Directory.CreateDirectory(bundled);
        var zip = ShippedZip();
        File.Copy(zip, Path.Combine(bundled, Path.GetFileName(zip)), overwrite: true);
        return new ExtensionManager(
            Path.Combine(_root.FullName, extensionsSubDir),
            Path.Combine(_root.FullName, extensionsSubDir + "-sha256"),
            bundled,
            new HttpClient(new ThrowingHandler()));
    }

    private string AssetsFileIn(string extensionsDir)
    {
        var target = Path.Combine(extensionsDir, "uBlock0_1.73.0");
        foreach (var dir in Directory.Exists(target)
                     ? new[] { target }.Concat(Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories))
                     : Array.Empty<string>())
            if (File.Exists(Path.Combine(dir, "assets", "assets.json")))
                return Path.Combine(dir, "assets", "assets.json");
        throw new DirectoryNotFoundException("no assets.json under " + extensionsDir);
    }

    private static Dictionary<string, (long Length, long Ticks, string Sha256)> Snapshot(string root)
    {
        var result = new Dictionary<string, (long, long, string)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return result;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(file);
            result[Path.GetRelativePath(root, file)] =
                (info.Length, info.LastWriteTimeUtc.Ticks, Convert.ToHexString(sha.ComputeHash(stream)));
        }
        return result;
    }

    /// <summary>
    /// The single native calls a plan causes. A stable extension id does not show a
    /// reinstall, because re-adding the same folder path yields the same id, so the
    /// operation itself has to be counted.
    /// </summary>
    private static (int Adds, int Removes, bool TogglesEnabled) NativeCallsFor(ExtensionManager.ReconcilePlan plan) =>
        plan.Action switch
        {
            ExtensionManager.ReconcileAction.Register => (1, 0, true),
            ExtensionManager.ReconcileAction.RemoveAndRegister => (1, 1, true),
            ExtensionManager.ReconcileAction.RepairInPlace => (0, 0, true),
            ExtensionManager.ReconcileAction.ApplyEnabledStateOnly => (0, 0, true),
            _ => (0, 0, false)
        };

    [Fact]
    public async Task AColdPass_RegistersFromTheFolderItJustPrepared()
    {
        var manager = NewManager();
        var extensions = manager.ExtensionsDir;

        var first = await manager.DecideAsync(
            registered: false, legacyExtensionPresent: false, desiredEnabled: true);

        // The shipped assets.json carries mirror and diff keys, so the repair changes
        // real bytes here. A fixture that started repaired would prove nothing.
        Assert.Equal(ExtensionManager.ReconcileAction.Register, first.Plan.Action);
        Assert.NotNull(first.Plan.Folder);
        Assert.True(first.PreparedThisPass);
        Assert.True(first.AssetsRewrittenThisPass);
        Assert.True(manager.LastPreparation.AssetsRewritten);

        // The bytes on disk are no longer the bytes the zip carried, because the
        // mirror and diff keys are behind a caret. The origin filter urls are kept, so
        // the extension can still refresh from them.
        var assets = AssetsFileIn(extensions);
        var installed = File.ReadAllText(assets);
        Assert.NotEqual(ShippedAssetsText(), installed);
        Assert.Contains("\"^cdnURLs\"", installed);
        Assert.Contains("\"^patchURLs\"", installed);
        Assert.Equal((1, 0, true), NativeCallsFor(first.Plan));
    }

    [Fact]
    public async Task TurningBlockingOffThenOnAgain_NeverAddsOrRemovesTheExtension()
    {
        // The reported defect. Cold start rewrote the assets, so the next pass read a
        // stale AssetsRewritten = true off the diagnostic timings, chose
        // RemoveAndRegister, and reinstalled a healthy uBlock over a toggle.
        var manager = NewManager();
        var extensions = manager.ExtensionsDir;

        var cold = await manager.DecideAsync(
            registered: false, legacyExtensionPresent: false, desiredEnabled: true);
        Assert.Equal(ExtensionManager.ReconcileAction.Register, cold.Plan.Action);

        // The registration now exists, which is the state the next pass observes.
        var off = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: false);
        var on = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);

        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, off.Plan.Action);
        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, on.Plan.Action);
        Assert.Equal((0, 0, true), NativeCallsFor(off.Plan));
        Assert.Equal((0, 0, true), NativeCallsFor(on.Plan));
        Assert.False(off.PreparedThisPass);
        Assert.False(on.PreparedThisPass);
        Assert.False(off.FolderChangedThisPass);
        Assert.False(on.FolderChangedThisPass);
    }

    [Fact]
    public async Task AWarmPass_DoesNoFileWorkAndLeavesBytesAndModificationTimes()
    {
        var manager = NewManager();
        var extensions = manager.ExtensionsDir;

        await manager.DecideAsync(registered: false, legacyExtensionPresent: false, desiredEnabled: true);

        var before = Snapshot(extensions);
        Assert.NotEmpty(before);
        await Task.Delay(20);

        var warm = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);

        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, warm.Plan.Action);
        Assert.False(warm.PreparedThisPass);
        Assert.False(warm.FolderChangedThisPass);

        var after = Snapshot(extensions);
        Assert.Equal(before.Count, after.Count);
        foreach (var entry in before)
        {
            Assert.True(after.ContainsKey(entry.Key), "missing after the warm pass: " + entry.Key);
            Assert.Equal(entry.Value.Sha256, after[entry.Key].Sha256);
            Assert.Equal(entry.Value.Ticks, after[entry.Key].Ticks);
        }
    }

    [Fact]
    public async Task AGenuineRepair_WritesTheFolderInPlaceAndCyclesNothing()
    {
        // The other half of the rule. A folder whose files are not what the platform was
        // given is a different folder from the one the platform holds, so the pass that
        // repairs it has to re-register from the result.
        var manager = NewManager();
        var extensions = manager.ExtensionsDir;

        var cold = await manager.DecideAsync(
            registered: false, legacyExtensionPresent: false, desiredEnabled: true);
        Assert.Equal(ExtensionManager.ReconcileAction.Register, cold.Plan.Action);

        // Put the folder back into the state the repair exists to fix: the mirror keys
        // the shipped zip carries, without their caret.
        var assets = AssetsFileIn(extensions);
        File.WriteAllText(assets, ShippedAssetsText());
        var mtimeBefore = new FileInfo(assets).LastWriteTimeUtc.Ticks;
        await Task.Delay(20);

        var repair = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);

        Assert.True(repair.PreparedThisPass);
        Assert.True(repair.FolderChangedThisPass);
        Assert.Equal(ExtensionManager.ReconcileAction.RepairInPlace, repair.Plan.Action);
        Assert.Equal((0, 0, true), NativeCallsFor(repair.Plan));
        Assert.NotNull(repair.PendingRepair);

        // The folder is registered, so the pass only staged the repair and did not write
        // over a folder the platform holds. The mtime has not moved yet.
        Assert.Equal(mtimeBefore, new FileInfo(assets).LastWriteTimeUtc.Ticks);

        // The commit, which is what lands after the native remove, writes the repaired
        // bytes and moves the mtime.
        var commit = manager.CommitFolderRepair(repair.PendingRepair!);
        Assert.True(commit.Completed);
        Assert.NotEqual(mtimeBefore, new FileInfo(assets).LastWriteTimeUtc.Ticks);
        Assert.Contains("^cdnURLs", File.ReadAllText(assets));
    }

    [Fact]
    public async Task AStaleDiagnosticFromAnEarlierPass_CannotDecideThisOne()
    {
        // The diagnostic still says the cold pass rewrote the assets, and that is
        // exactly what it is for. What it must not do is decide a later pass. This is
        // the whole correction stated as one assertion: the stale value is present and
        // the plan ignores it.
        var manager = NewManager();

        await manager.DecideAsync(registered: false, legacyExtensionPresent: false, desiredEnabled: true);
        Assert.True(manager.LastPreparation.AssetsRewritten);

        var toggle = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: false);

        Assert.True(manager.LastPreparation.AssetsRewritten);
        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, toggle.Plan.Action);
        Assert.False(toggle.FolderChangedThisPass);
    }

    [Fact]
    public async Task OneProfilesPreparation_CannotDecideAnotherProfilesPass()
    {
        // One manager instance is the shape the product has: a single ExtensionManager
        // serves every window, so two profiles reconcile through the same object and
        // its diagnostics. Profile A prepares and rewrites; profile B then passes
        // through with a healthy registered folder and needs no file work. Under the
        // old wiring B read A's AssetsRewritten and cycled its own healthy
        // registration. Each pass is interleaved so the stale value is live at the
        // moment B decides.
        var manager = NewManager();
        var extensions = manager.ExtensionsDir;

        var a1 = await manager.DecideAsync(
            registered: false, legacyExtensionPresent: false, desiredEnabled: true);
        Assert.True(a1.FolderChangedThisPass);
        Assert.Equal(ExtensionManager.ReconcileAction.Register, a1.Plan.Action);
        Assert.True(manager.LastPreparation.AssetsRewritten);

        var b1 = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);
        Assert.False(b1.PreparedThisPass);
        Assert.False(b1.FolderChangedThisPass);
        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, b1.Plan.Action);
        Assert.Equal((0, 0, true), NativeCallsFor(b1.Plan));

        // Profile A has to repair its folder, so it stages a repair again. B's next pass
        // must still not inherit that, and A's staged bytes are committed the way
        // ReconcileAsync commits them, after the native remove.
        var assets = AssetsFileIn(extensions);
        File.WriteAllText(assets, ShippedAssetsText());
        var a2 = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);
        Assert.True(a2.FolderChangedThisPass);
        Assert.Equal(ExtensionManager.ReconcileAction.RepairInPlace, a2.Plan.Action);
        Assert.NotNull(a2.PendingRepair);
        Assert.True(manager.CommitFolderRepair(a2.PendingRepair!).Completed);

        var b2 = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: false);
        Assert.False(b2.PreparedThisPass);
        Assert.False(b2.FolderChangedThisPass);
        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, b2.Plan.Action);
        Assert.Equal((0, 0, true), NativeCallsFor(b2.Plan));
    }

    [Fact]
    public async Task AProfileWithBlockingOffAndNothingInstalled_PreparesNothing()
    {
        // The warm path stays warm: a preference that is already satisfied must not
        // unpack a 4 MB zip, and must not report that it normalized anything.
        var manager = NewManager();

        var decision = await manager.DecideAsync(
            registered: false, legacyExtensionPresent: false, desiredEnabled: false);

        Assert.Equal(ExtensionManager.ReconcileAction.NothingToDo, decision.Plan.Action);
        Assert.False(decision.PreparedThisPass);
        Assert.False(decision.FolderChangedThisPass);
        Assert.False(Directory.Exists(manager.ExtensionsDir));
        Assert.Equal(ExtensionManager.PreparationTimings.Empty, manager.LastPreparation);
    }

    [Fact]
    public async Task RepeatedTogglesOverAnInstalledFolder_NeverAddOrRemove()
    {
        // The same wiring over a longer sequence, so one lucky ordering cannot pass.
        var manager = NewManager();
        await manager.DecideAsync(registered: false, legacyExtensionPresent: false, desiredEnabled: true);
        var extensions = manager.ExtensionsDir;
        var before = Snapshot(extensions);

        var calls = new List<(int Adds, int Removes, bool TogglesEnabled)>();
        foreach (var desired in new[] { false, true, false, true, true, false })
        {
            var pass = await manager.DecideAsync(
                registered: true, legacyExtensionPresent: false, desiredEnabled: desired);
            calls.Add(NativeCallsFor(pass.Plan));
            Assert.False(pass.FolderChangedThisPass);
        }

        Assert.All(calls, c => Assert.Equal((0, 0, true), c));

        var after = Snapshot(extensions);
        Assert.Equal(before.Count, after.Count);
        foreach (var entry in before)
        {
            Assert.True(after.ContainsKey(entry.Key));
            Assert.Equal(entry.Value.Sha256, after[entry.Key].Sha256);
            Assert.Equal(entry.Value.Ticks, after[entry.Key].Ticks);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, System.Threading.CancellationToken ct) =>
            throw new InvalidOperationException("no network in this test");
    }
}