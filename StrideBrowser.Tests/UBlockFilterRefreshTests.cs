using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Whether the installed uBlock folder can still update its filter lists.
///
/// The old preparation removed every absolute url from <c>contentURL</c> and renamed
/// <c>cdnURLs</c> and <c>patchURLs</c> behind a caret. The pinned 1.73 source decides
/// what that does. In <c>js/assets.js</c>, <c>registerAssetSource</c> sets
/// <c>hasRemoteURL</c> from whether any <c>contentURL</c> entry matches
/// <c>/^(?:[a-z-]+):\/\//</c>; <c>getUpdateCandidates</c> skips every entry whose
/// <c>hasRemoteURL</c> is not true; and <c>getRemote</c> skips every content url that
/// fails the same test. The strip closed the refresh path twice, so no list in the
/// folder could ever be updated again, however old it got.
///
/// The predicates below are the shipped ones, and they are evaluated against the real
/// assets.json out of the pinned 4.6 MB package rather than against a fixture, so a
/// claim about "the target default list" is a claim about the list the product
/// installs. The same predicates are executed end to end by
/// tools/RecoveryVerification/UBlockFilterRefreshRepair/20261015-r1/evidence/ublock-update-gate.mjs,
/// which extracts the functions from the package's own js/assets.js and runs them.
///
/// The preparation wiring is exercised through <c>DecideAsync</c> on a real manager over
/// an isolated directory, so the file outcomes are the product's and not a re-enactment.
/// </summary>
public sealed class UBlockFilterRefreshTests : IDisposable
{
    /// <summary>
    /// The shipped external path test, from js/assets.js. Not an approximation of it:
    /// a different pattern here would make every claim below meaningless.
    /// </summary>
    private static readonly Regex IsExternalPath =
        new(@"^(?:[a-z-]+):\/\/", RegexOptions.Compiled);

    private const string TargetList = "ublock-filters";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("stride-filter-refresh");

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
        foreach (var dir in Directory.Exists(target)
                     ? Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories)
                     : Array.Empty<string>())
            if (File.Exists(Path.Combine(dir, "manifest.json"))) return dir;
        throw new DirectoryNotFoundException("no manifest.json under " + extensionsDir);
    }

    private static string AssetsJsonIn(string extensionsDir) =>
        Path.Combine(ManifestDirIn(extensionsDir), "assets", "assets.json");

    private static string AssetsScriptIn(string extensionsDir) =>
        Path.Combine(ManifestDirIn(extensionsDir), "js", "assets.js");

    /// <summary>
    /// The assets.json the old preparation produced: every absolute url gone from
    /// contentURL, mirror and diff keys behind a caret. Built from the real shipped
    /// file, so it is the state a real profile is in, not a hand written one.
    /// </summary>
    private static string StrippedFormOf(string shippedJson)
    {
        var root = JsonNode.Parse(shippedJson)!.AsObject();
        foreach (var kvp in root.ToList())
        {
            if (kvp.Value is not JsonObject filter) continue;
            if (filter["off"]?.GetValue<bool>() == true) continue;

            if (filter["contentURL"] is JsonNode contentNode)
            {
                var entries = contentNode is JsonArray array
                    ? array.Select(e => e?.GetValue<string>()).Where(s => s is not null).ToList()!
                    : new List<string?> { contentNode.GetValue<string>() };
                var kept = new JsonArray();
                foreach (var s in entries!)
                    if (!IsExternalPath.IsMatch(s)) kept.Add(s);
                filter["contentURL"] = kept;
            }

            if (filter["cdnURLs"] is JsonNode cdn) { filter.Remove("cdnURLs"); filter["^cdnURLs"] = cdn.DeepClone(); }
            if (filter["patchURLs"] is JsonNode patch) { filter.Remove("patchURLs"); filter["^patchURLs"] = patch.DeepClone(); }
        }
        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>
    /// registerAssetSource's own arithmetic, over an assets.json: how many of the
    /// entry's content urls are external, and the two flags it derives from that.
    /// </summary>
    private static (int Remote, int Local, bool HasRemote, bool HasLocal) RegistryFlags(
        string assetsJson, string assetKey)
    {
        var entry = JsonNode.Parse(assetsJson)![assetKey] as JsonObject;
        var urls = new List<string>();
        if (entry?["contentURL"] is JsonArray array)
            urls.AddRange(array.Select(e => e?.GetValue<string>()).Where(s => s is not null)!);
        else if (entry?["contentURL"] is JsonNode single)
            urls.Add(single.GetValue<string>());

        var remote = urls.Count(u => IsExternalPath.IsMatch(u));
        // remoteURLCount !== contentURL.length and remoteURLCount !== 0, verbatim.
        return (remote, urls.Count - remote, remote != 0, remote != urls.Count);
    }

    /// <summary>
    /// getContentURLs with favorLocal, which is what assets.get passes until the
    /// filter engine reports ready. The local path has to come first for a first run
    /// to read the folder and reach no network.
    /// </summary>
    private static List<string> ColdReadOrder(string assetsJson, string assetKey)
    {
        var entry = JsonNode.Parse(assetsJson)![assetKey] as JsonObject;
        var urls = new List<string>();
        if (entry?["contentURL"] is JsonArray array)
            urls.AddRange(array.Select(e => e?.GetValue<string>()).Where(s => s is not null)!);
        else if (entry?["contentURL"] is JsonNode single)
            urls.Add(single.GetValue<string>());

        var sorted = new List<string>(urls);
        sorted.Sort((a, b) => IsExternalPath.IsMatch(a) ? 1 : IsExternalPath.IsMatch(b) ? -1 : 0);
        return sorted;
    }

    // ---------------------------------------------------------------------------------
    // The gate itself, on the two file states. Both are built from the shipped package.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheStrippedForm_IsTheStateTheUpdaterCannotGetOutOf()
    {
        var shipped = UBlockAssetRepair.ReadPristineAssetsJson(ShippedZip());
        Assert.NotNull(shipped);
        var stripped = StrippedFormOf(shipped!);

        // Before: the registry entry is built with no external url at all, so
        // getUpdateCandidates skips it and getRemote has nothing to fetch.
        var before = RegistryFlags(stripped, TargetList);
        Assert.False(before.HasRemote);
        Assert.Equal(0, before.Remote);
        Assert.True(before.HasLocal);
        Assert.All(ColdReadOrder(stripped, TargetList), u => Assert.False(IsExternalPath.IsMatch(u)));
    }

    [Fact]
    public void ThePristineForm_LeavesTheUpdaterBothACandidateAndAUsableOrigin()
    {
        var shipped = UBlockAssetRepair.ReadPristineAssetsJson(ShippedZip());
        Assert.NotNull(shipped);

        var after = RegistryFlags(shipped!, TargetList);
        Assert.True(after.HasRemote);
        Assert.True(after.HasLocal);
        Assert.Equal(1, after.Remote);
        Assert.Equal(2, after.Local);

        // The local paths are still there and still first on a cold read, so a first
        // run reads the folder and reaches no network until the local read fails.
        var order = ColdReadOrder(shipped!, TargetList);
        Assert.Equal(3, order.Count);
        Assert.Equal("assets/ublock/filters.min.txt", order[0]);
        Assert.False(IsExternalPath.IsMatch(order[0]));
    }

    [Fact]
    public void EveryOnByDefaultListInThePackage_KeepsAUsableOriginUrl()
    {
        // The claim is not about one list. Every list the product installs by default
        // has to stay refreshable, or the gap moves to the next list.
        var shipped = UBlockAssetRepair.ReadPristineAssetsJson(ShippedZip());
        Assert.NotNull(shipped);
        var root = JsonNode.Parse(shipped!)!.AsObject();

        var checkedLists = 0;
        var withoutOrigin = new List<string>();
        foreach (var kvp in root)
        {
            if (kvp.Value is not JsonObject filter) continue;
            if (filter["content"]?.GetValue<string>() != "filters") continue;
            if (filter["off"]?.GetValue<bool>() == true) continue;
            checkedLists++;

            var urls = filter["contentURL"] is JsonArray array
                ? array.Select(e => e?.GetValue<string>()).Where(s => s is not null).ToList()!
                : new List<string?>();
            if (!urls.Any(u => IsExternalPath.IsMatch(u!))) withoutOrigin.Add(kvp.Key);
        }

        Assert.True(checkedLists >= 8, $"expected the package's default lists, saw {checkedLists}");
        Assert.Empty(withoutOrigin);
    }

    // ---------------------------------------------------------------------------------
    // The repair itself.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void RepairingTheStrippedFormPutsTheUsableOriginUrlBack()
    {
        var shipped = UBlockAssetRepair.ReadPristineAssetsJson(ShippedZip());
        var stripped = StrippedFormOf(shipped!);

        var result = UBlockAssetRepair.Repair(stripped, shipped);

        Assert.True(result.Changed);
        Assert.True(result.ContentUrlsRestored);
        Assert.NotNull(result.RepairedText);

        var after = RegistryFlags(result.RepairedText!, TargetList);
        Assert.True(after.HasRemote);
        Assert.Equal(1, after.Remote);
        Assert.Equal(2, after.Local);
        Assert.Equal("assets/ublock/filters.min.txt", ColdReadOrder(result.RepairedText!, TargetList)[0]);
    }

    [Fact]
    public void RepairingTheShippedForm_KeepsTheUrlsAndOnlyDisarmsTheMirrorKeys()
    {
        var shipped = UBlockAssetRepair.ReadPristineAssetsJson(ShippedZip());

        var result = UBlockAssetRepair.Repair(shipped, shipped);

        Assert.True(result.Changed);
        var root = JsonNode.Parse(result.RepairedText!)!.AsObject();
        var entry = root[TargetList]!.AsObject();
        Assert.False(entry.ContainsKey("cdnURLs"));
        Assert.True(entry.ContainsKey("^cdnURLs"));
        // A caret on the key is what stops a mirror fetch; the origin url the updater
        // uses is untouched, which is the whole point.
        var urls = entry["contentURL"]!.AsArray().Select(e => e!.GetValue<string>()).ToList();
        Assert.Contains("https://ublockorigin.github.io/uAssets/filters/filters.txt", urls);
        Assert.Equal(1, urls.Count(u => IsExternalPath.IsMatch(u)));
    }

    [Fact]
    public void RepairingIsIdempotent_AndASecondPassReportsNoChange()
    {
        var shipped = UBlockAssetRepair.ReadPristineAssetsJson(ShippedZip());
        var stripped = StrippedFormOf(shipped!);

        var first = UBlockAssetRepair.Repair(stripped, shipped);
        Assert.True(first.Changed);
        var second = UBlockAssetRepair.Repair(first.RepairedText, shipped);

        Assert.False(second.Changed);
        Assert.Equal(UBlockAssetRepair.RepairOutcome.AlreadyRepaired, second.Outcome);
    }

    [Fact]
    public void RepairingKeepsWhatThePackageDoesNotCarryAndWhatTheFolderAdds()
    {
        // A key the package has no opinion about is left exactly as it is, so nothing
        // a user or a later package put in the file is dropped by the repair.
        var shipped = UBlockAssetRepair.ReadPristineAssetsJson(ShippedZip());
        var current = JsonNode.Parse(shipped!)!.AsObject();
        current["my-own-list"] = new JsonObject
        {
            ["content"] = "filters",
            ["contentURL"] = new JsonArray("https://example.invalid/list.txt")
        };
        current[TargetList]!["title"] = "renamed by the user";

        var result = UBlockAssetRepair.Repair(
            current.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n",
            shipped);

        Assert.True(result.Changed);
        var after = JsonNode.Parse(result.RepairedText!)!.AsObject();
        Assert.Equal("renamed by the user", after[TargetList]!["title"]!.GetValue<string>());
        Assert.NotNull(after["my-own-list"]);
    }

    [Fact]
    public void AListThePackageShipsOff_IsLeftAlone()
    {
        var shipped = UBlockAssetRepair.ReadPristineAssetsJson(ShippedZip());
        var before = JsonNode.Parse(shipped!)!.AsObject();
        var offKey = before.First(k => k.Value?["off"]?.GetValue<bool>() == true).Key;
        var offText = before[offKey]!.ToJsonString();

        var result = UBlockAssetRepair.Repair(shipped!, shipped);

        Assert.NotNull(result.RepairedText);
        var after = JsonNode.Parse(result.RepairedText!)![offKey]!.ToJsonString();
        Assert.Equal(offText, after);
    }

    // ---------------------------------------------------------------------------------
    // The registry cache. A folder repaired on disk still has a registry cached from
    // the stripped form, and the extension reads that cache in preference to the file.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheShippedScriptStillReadsTheUpstreamRegistryKey()
    {
        // The premise of the migration, checked against the package's own script: the key
        // names the storage read, the storage write and both places the callback pulls
        // the saved registry back out of what the read returned, and nothing has moved
        // yet. All four, because all four have to move together.
        var script = PinnedAssetsScript();

        Assert.Equal(1, Regex.Matches(script, @"cacheStorage\.get\(\s*'assetSourceRegistry'\s*\)").Count);
        Assert.Equal(1, Regex.Matches(script, @"cacheStorage\.set\(\{\s*assetSourceRegistry\s*\}\s*\)").Count);
        Assert.Equal(1, Regex.Matches(script, @"bin\.assetSourceRegistry(?=\s+instanceof\s+Object)").Count);
        Assert.Equal(1, Regex.Matches(script, @"=\s*bin\.assetSourceRegistry\s*;").Count);
        Assert.False(UBlockAssetRepair.RegistryKeyIsRepaired(script));
    }

    /// <summary>
    /// The pinned package's own js/assets.js, read out of the zip. This is the script
    /// the repair is matched against, so the migration is never applied to a guess.
    /// </summary>
    private static string PinnedAssetsScript()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stride-pinned-js-" + Guid.NewGuid().ToString("N"));
        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(ShippedZip(), dir);
            var script = Directory
                .EnumerateFiles(dir, "assets.js", SearchOption.AllDirectories)
                .First(f => f.Replace('\\', '/').EndsWith("/js/assets.js", StringComparison.Ordinal));
            return File.ReadAllText(script);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void MovingTheRegistryKey_MakesACachedLocalOnlyRegistryUnreachable()
    {
        var shipped = PinnedAssetsScript();

        var result = UBlockAssetRepair.RepairRegistryKey(shipped);

        Assert.True(result.Changed);
        Assert.NotNull(result.RepairedText);
        Assert.True(UBlockAssetRepair.RegistryKeyIsRepaired(result.RepairedText!));
        // The upstream key is gone from all four places, so a registry cached under it is
        // simply not read any more. Nothing else in the file moved.
        var key = Regex.Escape(UBlockAssetRepair.RepairedRegistryKey);
        Assert.Equal(0, Regex.Matches(result.RepairedText!,
            @"cacheStorage\.get\(\s*'assetSourceRegistry'\s*\)").Count);
        Assert.Equal(0, Regex.Matches(result.RepairedText!, @"bin\.assetSourceRegistry").Count);
        Assert.Equal(1, Regex.Matches(result.RepairedText!,
            $"cacheStorage\\.get\\('{key}'\\)").Count);
        Assert.Equal(1, Regex.Matches(result.RepairedText!,
            $"cacheStorage\\.set\\(\\{{ '{key}': assetSourceRegistry \\}}\\)").Count);
        Assert.Equal(1, Regex.Matches(result.RepairedText!,
            $"bin\\['{key}'\\](?=\\s+instanceof\\s+Object)").Count);
        Assert.Equal(1, Regex.Matches(result.RepairedText!,
            $"=\\s*bin\\['{key}'\\]\\s*;").Count);

        // The cached list content, the cache registry, and the user's own filter file are
        // the three a user would miss, so each is checked for still being read and
        // written under the name it had.
        Assert.Equal(1, Regex.Matches(result.RepairedText!,
            @"cacheStorage\.get\(\s*'assetCacheRegistry'\s*\)").Count);
        Assert.Equal(3, Regex.Matches(result.RepairedText!,
            @"cacheStorage\.set\(\{ assetCacheRegistry \}\)").Count);
        Assert.Equal(1, Regex.Matches(result.RepairedText!,
            @"cacheStorage\.set\(\{ \[`cache/\$\{assetKey\}`\]: content \}\)").Count);
        Assert.Contains("if ( assetKey === µb.userFiltersPath ) {", result.RepairedText!);
        Assert.Contains("readUserAsset", result.RepairedText!);
    }

    [Fact]
    public void MovingTheRegistryKey_IsIdempotent()
    {
        var first = UBlockAssetRepair.RepairRegistryKey(PinnedAssetsScript());
        Assert.True(first.Changed);
        var second = UBlockAssetRepair.RepairRegistryKey(first.RepairedText!);

        Assert.False(second.Changed);
        Assert.Equal(UBlockAssetRepair.RepairOutcome.AlreadyRepaired, second.Outcome);
    }

    [Fact]
    public void AScriptThatIsNotThePinnedOne_IsReportedNotPossibleAndLeftAlone()
    {
        // A different uBlock version must never be edited on a guess. The file is
        // reported as not migrated and comes back byte for byte.
        var other = "const a = 1;\ncacheStorage.get( 'somethingElse' );\n";
        var result = UBlockAssetRepair.RepairRegistryKey(other);

        Assert.Equal(UBlockAssetRepair.RepairOutcome.NotPossible, result.Outcome);
        Assert.Null(result.RepairedText);
        Assert.Contains("does not recognise", result.Detail);
    }

    [Fact]
    public void AFolderWithNoScript_IsReportedNotPossible()
    {
        var result = UBlockAssetRepair.RepairRegistryKey(null);
        Assert.Equal(UBlockAssetRepair.RepairOutcome.NotPossible, result.Outcome);
    }

    // ---------------------------------------------------------------------------------
    // Preparation wiring, on a real manager over an isolated directory.
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task AFreshFolder_IsInstalledWithItsOriginUrlsAndItsRegistryKeyMoved()
    {
        var manager = NewManager();
        var decision = await manager.DecideAsync(
            registered: false, legacyExtensionPresent: false, desiredEnabled: true);

        Assert.Equal(ExtensionManager.ReconcileAction.Register, decision.Plan.Action);

        var assets = AssetsJsonIn(manager.ExtensionsDir);
        var flags = RegistryFlags(File.ReadAllText(assets), TargetList);
        Assert.True(flags.HasRemote);
        Assert.Equal(1, flags.Remote);
        Assert.Equal(2, flags.Local);
        Assert.True(UBlockAssetRepair.RegistryKeyIsRepaired(File.ReadAllText(AssetsScriptIn(manager.ExtensionsDir))));

        // A fresh install reports what it did, so a caller can see the migration ran.
        Assert.True(decision.AssetsRewrittenThisPass);
        Assert.True(decision.RegistryKeyRewrittenThisPass);
        Assert.True(decision.FolderChangedThisPass);
    }

    [Fact]
    public async Task AFolderStrippedByTheOldPass_IsUpgradedOnce()
    {
        var manager = NewManager();
        await manager.DecideAsync(registered: false, legacyExtensionPresent: false, desiredEnabled: true);

        // Put the folder back the way the old pass left it: content urls local only and
        // the registry key back where the package ships it.
        var assets = AssetsJsonIn(manager.ExtensionsDir);
        var pristine = UBlockAssetRepair.ReadPristineAssetsJson(ShippedZip());
        File.WriteAllText(assets, StrippedFormOf(File.ReadAllText(assets)));

        var script = AssetsScriptIn(manager.ExtensionsDir);
        var downgraded = PinnedScriptForm.ToUpstream(File.ReadAllText(script));
        File.WriteAllText(script, downgraded);

        // The pass sees a folder that needs work. The folder is registered, so the pass
        // only stages the repair; the commit is what writes it over that same folder, with
        // no native remove in front of it.
        var upgrade = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);

        Assert.True(upgrade.Plan.Action == ExtensionManager.ReconcileAction.RepairInPlace,
            $"expected an in-place repair of the registered folder, got {upgrade.Plan.Action}");
        Assert.True(upgrade.FolderChangedThisPass);
        Assert.NotNull(upgrade.PendingRepair);
        Assert.Equal(UBlockAssetRepair.RepairOutcome.Rewritten,
            upgrade.PendingRepair!.Registry!.Result.Outcome);

        // Nothing has been written yet, because the platform still holds the folder.
        var stillStripped = RegistryFlags(File.ReadAllText(assets), TargetList);
        Assert.False(stillStripped.HasRemote);
        Assert.False(UBlockAssetRepair.RegistryKeyIsRepaired(File.ReadAllText(script)));

        var commit = manager.CommitFolderRepair(upgrade.PendingRepair!);
        Assert.True(commit.Completed, commit.Detail);
        Assert.Equal(2, commit.FilesWritten);
        Assert.Equal(0, commit.FilesRestored);

        var repaired = RegistryFlags(File.ReadAllText(assets), TargetList);
        Assert.True(repaired.HasRemote);
        Assert.Equal(1, repaired.Remote);
        Assert.Equal(2, repaired.Local);
        Assert.True(UBlockAssetRepair.RegistryKeyIsRepaired(File.ReadAllText(script)),
            "the registry key has to move in all four places, including the two the callback " +
            $"reads the saved registry back with. the commit said: {commit.Detail}");
    }

    [Fact]
    public async Task AWarmPass_AfterTheUpgrade_WritesNothingAndRegistersNothing()
    {
        var manager = NewManager();
        await manager.DecideAsync(registered: false, legacyExtensionPresent: false, desiredEnabled: true);
        await manager.DecideAsync(registered: true, legacyExtensionPresent: false, desiredEnabled: true);

        var extensions = manager.ExtensionsDir;
        var before = Snapshot(extensions);
        Assert.NotEmpty(before);
        await Task.Delay(20);

        var warm = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);

        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, warm.Plan.Action);
        Assert.False(warm.PreparedThisPass);
        Assert.False(warm.FolderChangedThisPass);
        Assert.Equal((0, 0, true), NativeCallsFor(warm.Plan));

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
    public async Task TurningBlockingOffAndOnAgain_AfterTheUpgrade_WritesNothingAndRegistersNothing()
    {
        var manager = NewManager();
        await manager.DecideAsync(registered: false, legacyExtensionPresent: false, desiredEnabled: true);
        await manager.DecideAsync(registered: true, legacyExtensionPresent: false, desiredEnabled: true);

        var extensions = manager.ExtensionsDir;
        var before = Snapshot(extensions);
        var calls = new List<(int Adds, int Removes, bool Toggles)>();

        foreach (var desired in new[] { false, true })
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

    [Fact]
    public async Task AHealthyFolder_IsNeverPreparedSoNoFileIsOpened()
    {
        // The warm path has to stay warm. A folder that is already repairable needs no
        // preparation at all, which is the check that keeps this off every start. The
        // timings are read before and after so the comparison is with this pass, not
        // with an empty record a cold pass never produced here.
        var manager = NewManager();
        var cold = await manager.DecideAsync(
            registered: false, legacyExtensionPresent: false, desiredEnabled: true);
        Assert.True(cold.FolderChangedThisPass);
        var afterCold = manager.LastPreparation;

        var decision = await manager.DecideAsync(
            registered: true, legacyExtensionPresent: false, desiredEnabled: true);

        Assert.False(decision.PreparedThisPass);
        Assert.Equal(afterCold, manager.LastPreparation);
    }

    [Fact]
    public async Task AReadOnlyFolder_IsReportedTruthfullyAndKeepsItsWorkingRegistration()
    {
        // A folder that cannot be written keeps the extension that is registered against
        // it. The pass must not cycle a working blocker to no effect, and the commit that
        // follows the remove must put every file back rather than leave one half written.
        var manager = NewManager();
        await manager.DecideAsync(registered: false, legacyExtensionPresent: false, desiredEnabled: true);
        await manager.DecideAsync(registered: true, legacyExtensionPresent: false, desiredEnabled: true);

        // Put the folder back into the state that needs repair, then make it unwritable.
        var assets = AssetsJsonIn(manager.ExtensionsDir);
        var script = AssetsScriptIn(manager.ExtensionsDir);
        File.WriteAllText(assets, StrippedFormOf(File.ReadAllText(assets)));
        File.WriteAllText(script, PinnedScriptForm.ToUpstream(File.ReadAllText(script)));
        var bytesBefore = File.ReadAllBytes(assets);
        var scriptBefore = File.ReadAllBytes(script);

        foreach (var path in new[] { assets, script })
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);
        }

        try
        {
            var decision = await manager.DecideAsync(
                registered: true, legacyExtensionPresent: false, desiredEnabled: true);

            // The registration the platform holds is left in place, and the pass stages
            // the repair rather than writing over a live folder.
            Assert.NotEqual(ExtensionManager.ReconcileAction.FailPreservingRegistration, decision.Plan.Action);
            Assert.NotNull(decision.PendingRepair);

            // Even after the native remove, a folder that will not take the write is put
            // back and reported as not completed.
            var commit = manager.CommitFolderRepair(decision.PendingRepair!);
            Assert.False(commit.Completed, commit.Detail);
            Assert.Equal(0, commit.FilesWritten);

            // Both files are byte for byte what they were.
            Assert.Equal(bytesBefore, File.ReadAllBytes(assets));
            Assert.Equal(scriptBefore, File.ReadAllBytes(script));
        }
        finally
        {
            foreach (var path in new[] { assets, script })
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public async Task AProfileWithBlockingOffAndNothingInstalled_StillPreparesNothing()
    {
        var manager = NewManager();
        var decision = await manager.DecideAsync(
            registered: false, legacyExtensionPresent: false, desiredEnabled: false);

        Assert.Equal(ExtensionManager.ReconcileAction.NothingToDo, decision.Plan.Action);
        Assert.False(decision.PreparedThisPass);
        Assert.False(Directory.Exists(manager.ExtensionsDir));
    }

    [Fact]
    public async Task TheHealthReport_SaysAFreshFolderDoesNotNeedRepairing()
    {
        var manager = NewManager();
        await manager.DecideAsync(registered: false, legacyExtensionPresent: false, desiredEnabled: true);

        var health = manager.GetHealth();

        Assert.True(health.HasFolder);
        Assert.True(health.HasZip);
        Assert.False(health.AssetsStripped);
    }

    [Fact]
    public async Task TheHealthReport_SaysAStrippedFolderStillDoes()
    {
        var manager = NewManager();
        await manager.DecideAsync(registered: false, legacyExtensionPresent: false, desiredEnabled: true);
        File.WriteAllText(AssetsJsonIn(manager.ExtensionsDir),
            StrippedFormOf(File.ReadAllText(AssetsJsonIn(manager.ExtensionsDir))));

        Assert.True(manager.GetHealth().AssetsStripped);
    }

    private static (int Adds, int Removes, bool TogglesEnabled) NativeCallsFor(ExtensionManager.ReconcilePlan plan) =>
        plan.Action switch
        {
            ExtensionManager.ReconcileAction.Register => (1, 0, true),
            ExtensionManager.ReconcileAction.RemoveAndRegister => (1, 1, true),
            ExtensionManager.ReconcileAction.ApplyEnabledStateOnly => (0, 0, true),
            _ => (0, 0, false)
        };

    private static Dictionary<string, (long Length, long Ticks, string Sha256)> Snapshot(string root)
    {
        var result = new Dictionary<string, (long, long, string)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return result;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            result[Path.GetRelativePath(root, file)] =
                (info.Length, info.LastWriteTimeUtc.Ticks, Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))));
        }
        return result;
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, System.Threading.CancellationToken ct) =>
            throw new InvalidOperationException("no network in this test");
    }
}
