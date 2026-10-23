using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// The registered-folder repair, and what it must never do.
///
/// A registration is a live reference to a folder path. The repair works out the bytes it
/// wants by staging, and then writes them over that same folder in place, once. It never
/// removes the extension and adds the same folder back: that is a reinstall, and a
/// reinstall discards everything the extension stored for itself, measured on a real
/// profile where a durable storage key and a non-default list selection were both gone
/// after a remove and add and both survived an in-place write. A write that fails part way
/// puts every file back from the bytes captured at staging time, so nothing lands half
/// applied.
///
/// These drive <see cref="ExtensionManager.DecideAsync"/> and the staging and commit
/// pair against a real manager over an isolated directory, so what is asserted is the
/// product's file handling rather than a re-enactment of it.
/// </summary>
public sealed class UBlockRegisteredMigrationTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("umber-migration");

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

    private const string TargetList = "ublock-filters";

    private static readonly Regex IsExternalPath = new(@"^(?:[a-z-]+):\/\/");

    private static string ShippedZip()
    {
        var name = "uBlock0_1.73.0.chromium.zip";
        var candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Extensions", name);
        if (File.Exists(candidate)) return candidate;
        throw new FileNotFoundException(
            $"the shipped uBlock zip is not beside the test binary: {candidate}", name);
    }

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

    private static string ManifestDirIn(string extensionsDir)
    {
        var target = Path.Combine(extensionsDir, "uBlock0_1.73.0");
        if (File.Exists(Path.Combine(target, "manifest.json"))) return target;
        foreach (var dir in Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories))
            if (File.Exists(Path.Combine(dir, "manifest.json"))) return dir;
        throw new DirectoryNotFoundException("no manifest.json under " + extensionsDir);
    }

    private static string AssetsJsonIn(string extensionsDir) =>
        Path.Combine(ManifestDirIn(extensionsDir), "assets", "assets.json");

    private static string AssetsScriptIn(string extensionsDir) =>
        Path.Combine(ManifestDirIn(extensionsDir), "js", "assets.js");

    /// <summary>
    /// The state an older build left: the whole package installed, origin urls removed
    /// from contentURL, registry key back where the package ships it. This is what a real
    /// profile looks like when the browser asks for a repair, and it is built from the
    /// package so it is the real state.
    /// </summary>
    private void InstallAsTheOldBuildLeftIt(ExtensionManager manager)
    {
        var zip = Path.Combine(Path.GetTempPath(), "umber-seed-" + Guid.NewGuid().ToString("N") + ".zip");
        File.Copy(ShippedZip(), zip, true);
        try
        {
            var extensions = manager.ExtensionsDir;
            Directory.CreateDirectory(extensions);
            var staging = Path.Combine(extensions, ".seed");
            ZipFile.ExtractToDirectory(zip, staging);
            var target = Path.Combine(extensions, "uBlock0_1.73.0");
            foreach (var inner in Directory.EnumerateDirectories(staging))
                Directory.Move(inner, target);
            Directory.Delete(staging, true);
        }
        finally { File.Delete(zip); }

        var pristine = UBlockAssetRepair.ReadPristineAssetsJson(ShippedZip())!;
        var root = System.Text.Json.Nodes.JsonNode.Parse(pristine)!.AsObject();
        foreach (var kvp in root.ToList())
        {
            if (kvp.Value is not System.Text.Json.Nodes.JsonObject filter) continue;
            if (filter["off"]?.GetValue<bool>() == true) continue;
            if (filter["contentURL"] is System.Text.Json.Nodes.JsonArray array)
            {
                var kept = new System.Text.Json.Nodes.JsonArray();
                foreach (var entry in array)
                {
                    var s = entry?.GetValue<string>();
                    if (s is not null && !IsExternalPath.IsMatch(s)) kept.Add(s);
                }
                filter["contentURL"] = kept;
            }
            if (filter["cdnURLs"] is System.Text.Json.Nodes.JsonNode cdn)
            { filter.Remove("cdnURLs"); filter["^cdnURLs"] = cdn.DeepClone(); }
            if (filter["patchURLs"] is System.Text.Json.Nodes.JsonNode patch)
            { filter.Remove("patchURLs"); filter["^patchURLs"] = patch.DeepClone(); }
        }
        File.WriteAllText(AssetsJsonIn(manager.ExtensionsDir),
            root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");

        var script = AssetsScriptIn(manager.ExtensionsDir);
        File.WriteAllText(script, PinnedScriptForm.ToUpstream(File.ReadAllText(script)));
    }

    private static (int Remote, int Local) ContentUrls(string assetsJson, string assetKey)
    {
        var entry = System.Text.Json.Nodes.JsonNode.Parse(assetsJson)![assetKey] as System.Text.Json.Nodes.JsonObject;
        var urls = entry?["contentURL"] is System.Text.Json.Nodes.JsonArray array
            ? array.Select(e => e?.GetValue<string>()).Where(s => s is not null).ToList()!
            : new List<string?>();
        var remote = urls.Count(u => IsExternalPath.IsMatch(u!));
        return (remote, urls.Count - remote);
    }

    private static Dictionary<string, (long Ticks, string Sha256)> Snapshot(string root)
    {
        var result = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return result;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            result[Path.GetRelativePath(root, file)] =
                (new FileInfo(file).LastWriteTimeUtc.Ticks,
                 Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))));
        return result;
    }

    // ---------------------------------------------------------------------------------
    // The order, which is the whole point of the correction.
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task APassOverARegisteredFolder_ComputesTheRepairWithoutWritingIt()
    {
        var manager = NewManager();
        InstallAsTheOldBuildLeftIt(manager);
        var assets = AssetsJsonIn(manager.ExtensionsDir);
        var script = AssetsScriptIn(manager.ExtensionsDir);
        var before = Snapshot(manager.ExtensionsDir);
        var beforeUrls = ContentUrls(File.ReadAllText(assets), TargetList);

        var decision = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);

        // The pass worked out what has to change, and is going to write it in place. It
        // does not cycle the registration, because re-adding the same folder is a reinstall
        // and a reinstall discards the extension's own storage.
        Assert.Equal(ExtensionManager.ReconcileAction.RepairInPlace, decision.Plan.Action);
        Assert.True(decision.FolderChangedThisPass);
        Assert.NotNull(decision.PendingRepair);
        Assert.True(decision.PendingRepair!.FolderChanged);

        // And the folder the platform is holding was not touched while it decided.
        Assert.Equal(0, beforeUrls.Remote);
        Assert.Equal(beforeUrls, ContentUrls(File.ReadAllText(assets), TargetList));
        Assert.False(UBlockAssetRepair.RegistryKeyIsRepaired(File.ReadAllText(script)));
        var after = Snapshot(manager.ExtensionsDir);
        Assert.Equal(before.Count, after.Count);
        foreach (var entry in before)
        {
            Assert.True(after.ContainsKey(entry.Key));
            Assert.Equal(entry.Value.Sha256, after[entry.Key].Sha256);
            Assert.Equal(entry.Value.Ticks, after[entry.Key].Ticks);
        }
    }

    [Fact]
    public async Task TheCommitThatFollowsTheRemove_WritesTheRepairAtTheSamePath()
    {
        var manager = NewManager();
        InstallAsTheOldBuildLeftIt(manager);
        var folder = ManifestDirIn(manager.ExtensionsDir);
        var assets = AssetsJsonIn(manager.ExtensionsDir);
        var script = AssetsScriptIn(manager.ExtensionsDir);

        var decision = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);
        var staged = decision.PendingRepair!;

        // The native remove happens here, outside this test, and the commit is the only
        // thing left to do. It lands on the path the registration named.
        var commit = manager.CommitFolderRepair(staged);

        Assert.True(commit.Completed);
        Assert.Equal(2, commit.FilesWritten);
        Assert.Equal(0, commit.FilesRestored);
        Assert.Equal(assets, staged.Assets!.Path);
        Assert.Equal(script, staged.Registry!.Path);
        Assert.StartsWith(folder, staged.Assets.Path);

        var urls = ContentUrls(File.ReadAllText(assets), TargetList);
        Assert.Equal(1, urls.Remote);
        Assert.Equal(2, urls.Local);
        Assert.True(UBlockAssetRepair.RegistryKeyIsRepaired(File.ReadAllText(script)));
    }

    [Fact]
    public async Task ACommitThatFails_PutsBothFilesBackByteForByte()
    {
        // The second write is made to fail, so the first has to be undone. What is left
        // has to be the folder the platform last accepted, not a half repaired one.
        var manager = NewManager();
        InstallAsTheOldBuildLeftIt(manager);
        var assets = AssetsJsonIn(manager.ExtensionsDir);
        var script = AssetsScriptIn(manager.ExtensionsDir);

        var decision = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);
        var staged = decision.PendingRepair!;
        var assetsBefore = File.ReadAllBytes(assets);
        var scriptBefore = File.ReadAllBytes(script);

        // Order the commit writes assets.json first, so failing the script leaves the
        // assets write to be undone.
        Assert.NotNull(staged.Assets);
        Assert.NotNull(staged.Registry);

        // Make the script unwritable without touching its bytes.
        new FileInfo(script).IsReadOnly = true;
        ExtensionManager.CommitResult commit;
        try
        {
            commit = manager.CommitFolderRepair(staged);
        }
        finally
        {
            new FileInfo(script).IsReadOnly = false;
        }

        Assert.False(commit.Completed);
        Assert.True(commit.FilesRestored >= 1, $"nothing was put back: {commit.Detail}");

        // assets.json went back to the bytes the old build left, so the folder is the one
        // the platform already accepted.
        Assert.Equal(assetsBefore, File.ReadAllBytes(assets));
        Assert.Equal(scriptBefore, File.ReadAllBytes(script));
        Assert.Equal(0, ContentUrls(File.ReadAllText(assets), TargetList).Remote);
    }

    [Fact]
    public async Task ASecondCommit_IsANoOpSoAFailedPassDoesNotLoop()
    {
        var manager = NewManager();
        InstallAsTheOldBuildLeftIt(manager);

        var decision = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);
        Assert.NotNull(decision.PendingRepair);
        // Land the first repair, the way ReconcileAsync does after the native remove.
        Assert.True(manager.CommitFolderRepair(decision.PendingRepair!).Completed);

        // The pass that follows finds the folder repaired, so there is nothing to commit
        // and nothing to cycle. A folder that cannot take a write is not retried, and a
        // folder that took the write is not written a second time.
        var after = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);
        var stagedAfter = manager.StageFolderRepair(ManifestDirIn(manager.ExtensionsDir));
        var commit = manager.CommitFolderRepair(stagedAfter);

        Assert.True(commit.Completed);
        Assert.Equal(0, commit.FilesWritten);
        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, after.Plan.Action);
        Assert.False(after.FolderChangedThisPass);
    }

    // ---------------------------------------------------------------------------------
    // Honest reporting of the states that are not healthy.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AMalformedAssetsFile_IsReportedNotPossibleRatherThanHealthy()
    {
        var result = UBlockAssetRepair.Repair("{ this is not json", null);

        Assert.Equal(UBlockAssetRepair.RepairOutcome.NotPossible, result.Outcome);
        Assert.Null(result.RepairedText);
        Assert.Contains("malformed", result.Detail);
    }

    [Fact]
    public void AMissingAssetsFile_IsReportedNotPossible()
    {
        var result = UBlockAssetRepair.Repair(null, null);

        Assert.Equal(UBlockAssetRepair.RepairOutcome.NotPossible, result.Outcome);
        Assert.Contains("missing", result.Detail);
    }

    [Fact]
    public void AMissingScript_IsReportedNotPossible()
    {
        var result = UBlockAssetRepair.RepairRegistryKey(null);

        Assert.Equal(UBlockAssetRepair.RepairOutcome.NotPossible, result.Outcome);
        Assert.Contains("missing", result.Detail);
    }

    [Fact]
    public void AHalfMigratedScript_IsNotCalledRepaired()
    {
        // Read and write moved, the callback did not. The extension would read a bin
        // keyed by the new name, look inside it for the old property, find nothing and
        // rebuild from the bundled file on every start. Calling that repaired is worse
        // than calling it broken, so the check has to look at all four sites.
        var pinned = PinnedScript();
        var half = PinnedScriptForm.StorageKeyOnly(pinned);

        Assert.False(UBlockAssetRepair.RegistryKeyIsRepaired(half));
        var reason = UBlockAssetRepair.RegistryKeyNotRepairedReason(half);
        Assert.NotNull(reason);
        Assert.Contains("saved-registry", reason);
        Assert.Contains("partly migrated", reason);
    }

    [Fact]
    public void AScriptTheRepairCannotUnderstand_IsReportedUnassessable()
    {
        // A script that is neither the pinned form nor the repaired one is a fault, not a
        // folder that needs no work. The pass has to be able to say so about a folder
        // WebView2 is holding, without writing to it.
        var manager = NewManager();
        InstallAsTheOldBuildLeftIt(manager);
        File.WriteAllText(AssetsScriptIn(manager.ExtensionsDir),
            "async function getAssetSourceRegistry() { return {}; }\n");

        var staged = manager.StageFolderRepair(ManifestDirIn(manager.ExtensionsDir));

        Assert.False(staged.Assessable);
        Assert.False(staged.Registry!.Assessable);
        Assert.Contains("does not recognise", staged.Detail);
    }

    [Fact]
    public async Task AnUnreadableFolder_IsReportedUnassessableAndDoesNotClaimSuccess()
    {
        var manager = NewManager();
        InstallAsTheOldBuildLeftIt(manager);
        var assets = AssetsJsonIn(manager.ExtensionsDir);
        File.WriteAllText(assets, "{ broken");

        var decision = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);

        // Reported as not assessable, and the pass will not claim the repair landed.
        Assert.False(decision.FolderAssessable);
        Assert.Equal("{ broken", File.ReadAllText(assets));
    }

    [Fact]
    public async Task AnUnreadableFolder_StillPlansToCycleRatherThanClaimAHealthyRepair()
    {
        // A folder whose state cannot be established is not a folder that needs no work,
        // so the pass still acts on it rather than reporting a healthy one.
        var manager = NewManager();
        InstallAsTheOldBuildLeftIt(manager);
        File.Delete(AssetsScriptIn(manager.ExtensionsDir));

        var decision = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);

        Assert.NotEqual(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, decision.Plan.Action);
    }

    [Fact]
    public void TheHealthReport_SaysAFolderItCouldNotReadStillNeedsRepairing()
    {
        var manager = NewManager();
        InstallAsTheOldBuildLeftIt(manager);
        File.Delete(AssetsScriptIn(manager.ExtensionsDir));

        Assert.True(manager.GetHealth().AssetsStripped);
    }

    // ---------------------------------------------------------------------------------
    // The first install is unchanged: nothing holds the folder, so it is written at once.
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task AFirstInstall_WritesTheRepairImmediatelyBecauseNothingHoldsTheFolder()
    {
        var manager = NewManager();

        var decision = await manager.DecideAsync(
            registered: false, legacyExtensionPresent: false, desiredEnabled: true);

        Assert.Equal(ExtensionManager.ReconcileAction.Register, decision.Plan.Action);
        // No commit is deferred, because there was no registration to protect.
        Assert.Null(decision.PendingRepair);
        Assert.Equal(1, ContentUrls(File.ReadAllText(AssetsJsonIn(manager.ExtensionsDir)), TargetList).Remote);
        Assert.True(UBlockAssetRepair.RegistryKeyIsRepaired(
            File.ReadAllText(AssetsScriptIn(manager.ExtensionsDir))));
    }

    private static string PinnedScript()
    {
        var dir = Path.Combine(Path.GetTempPath(), "umber-pin-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(ShippedZip(), dir);
            return File.ReadAllText(Directory.EnumerateFiles(dir, "assets.js", SearchOption.AllDirectories)
                .First(f => f.Replace('\\', '/').EndsWith("/js/assets.js", StringComparison.Ordinal)));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, System.Threading.CancellationToken ct) =>
            throw new InvalidOperationException("no network in this test");
    }
}
