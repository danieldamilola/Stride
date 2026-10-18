using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StrideBrowser.Services;

/// <summary>
/// Puts one uBlock Origin folder into the state where the extension's own updater can
/// still run, without any of the network machinery Stride does not have.
///
/// The pinned 1.73 source decides this, not preference. In <c>js/assets.js</c>:
/// <c>registerAssetSource</c> sets <c>hasRemoteURL</c> from whether any
/// <c>contentURL</c> entry matches <c>/^(?:[a-z-]+):\/\//</c>; <c>getUpdateCandidates</c>
/// skips every entry whose <c>hasRemoteURL</c> is not true; and <c>getRemote</c> skips
/// every <c>contentURL</c> that fails the same test. Removing the absolute urls from
/// <c>contentURL</c> therefore removed the refresh path twice over, and no filter list
/// shipped in the folder could ever be updated again.
///
/// Keeping the urls is enough, and is the whole repair. The bundled local paths stay in
/// <c>contentURL</c> exactly as upstream ships them, and <c>assets.get</c> already
/// passes <c>favorLocal</c> until the filter engine is ready, which sorts the local path
/// ahead of the remote one. A first run therefore still reads the files in the folder and
/// still reaches no network until the local read fails.
///
/// Two things are deliberately left as they were. <c>cdnURLs</c> and <c>patchURLs</c>
/// keep their leading caret, which means uBlock's own updater does not read them and
/// works the entry from the origin url in <c>contentURL</c> instead. That is the
/// extension's normal way of updating a list, not something the browser interferes
/// with, and it is not a block on diff requests: <c>diff-updater.js</c> still resolves a
/// patch path, deriving the cdn hosts from those origin urls when the patch urls are not
/// read. What the caret does remove is the mirror and diff hosts the package happened to
/// name, which is why they are kept rather than deleted.
///
/// A folder that an older pass already stripped is migrated rather than left broken:
/// the remote urls are taken back from the pinned package, not invented here.
///
/// Pure file and string work. No WebView2, no COM, no UI thread, so it is directly
/// testable, and <see cref="ExtensionManager"/> keeps the decision of when to run it.
/// </summary>
public static class UBlockAssetRepair
{
    /// <summary>
    /// The cache key the extension stores its asset source registry under. Upstream
    /// reads it with the literal below and writes it with the property shorthand
    /// <c>cacheStorage.set({ assetSourceRegistry })</c>, so the key name appears in two
    /// places and both have to move together.
    /// </summary>
    private const string UpstreamRegistryKey = "assetSourceRegistry";

    /// <summary>
    /// The key this build stores the registry under. Moving it is what makes a
    /// registry that was cached while the folder was stripped update-capable again.
    /// The extension rebuilds the registry from the repaired assets.json on its next
    /// start, and nothing else moves: a user's list selection lives in
    /// <c>selectedFilterLists</c>, custom lists are still reachable by their url, and
    /// every other storage key keeps its name, so no user configuration is discarded.
    /// The old value is left where it is rather than deleted.
    /// </summary>
    public const string RepairedRegistryKey = "assetSourceRegistry.stride.refresh.v1";

    private static readonly Regex RegistryReadCall = new(
        @"cacheStorage\.get\(\s*'" + UpstreamRegistryKey + @"'\s*\)",
        RegexOptions.Compiled);

    /// <summary>
    /// The write side, matched on the key rather than on the shipped syntax. Upstream
    /// writes it with the property shorthand <c>{ assetSourceRegistry }</c>, which is the
    /// same key with no name of its own in the call. A folder this build already touched
    /// once carries the quoted form, so accepting both means a partly patched file still
    /// completes instead of being reported as a package it is not.
    /// </summary>
    private static readonly Regex RegistryWriteCall = new(
        @"cacheStorage\.set\(\{\s*'?" + UpstreamRegistryKey + @"'?(\s*:\s*assetSourceRegistry)?\s*\}\s*\)",
        RegexOptions.Compiled);

    /// <summary>
    /// The two places the callback reads the saved registry back out of what
    /// <c>cacheStorage.get</c> returned. Moving the storage key without moving these is
    /// the bug that makes the repair look like it worked and is not: the get would return
    /// <c>bin</c> keyed by the new name, the callback would look for the old property
    /// name inside it, find nothing, and rebuild the registry from the bundled file on
    /// every single start, discarding whatever the previous start had saved.
    /// </summary>
    private static readonly Regex RegistryBinGuard = new(
        @"bin\." + UpstreamRegistryKey + @"(?=\s+instanceof\s+Object)",
        RegexOptions.Compiled);

    private static readonly Regex RegistryBinRead = new(
        @"=\s*bin\." + UpstreamRegistryKey + @"\s*;",
        RegexOptions.Compiled);

    /// <summary>
    /// The same four sites, named on the repaired key rather than the upstream one. A
    /// file that has none of these is not a repaired file, it is a file this repair does
    /// not recognise, and the two have to stay distinguishable.
    /// </summary>
    private static bool[] RepairedSitesPresent(string assetsJs) =>
        new[]
        {
            new Regex(@"cacheStorage\.get\(\s*'" + RepairedRegistryKey + @"'\s*\)").IsMatch(assetsJs),
            new Regex(@"bin\['" + RepairedRegistryKey + @"'\](?=\s+instanceof\s+Object)").IsMatch(assetsJs),
            new Regex(@"=\s*bin\['" + RepairedRegistryKey + @"'\]\s*;").IsMatch(assetsJs),
            new Regex(@"cacheStorage\.set\(\{\s*'" + RepairedRegistryKey + @"'\s*:\s*assetSourceRegistry\s*\}\s*\)")
                .IsMatch(assetsJs)
        };

    /// <summary>
    /// Which of the four sites still carry the upstream name. Every one of them has to
    /// move together, and a file that moved some of them is reported as partly patched
    /// rather than repaired.
    /// </summary>
    private static bool[] UpstreamSitesLeft(string assetsJs) =>
        new[]
        {
            RegistryReadCall.IsMatch(assetsJs),
            RegistryBinGuard.IsMatch(assetsJs),
            RegistryBinRead.IsMatch(assetsJs),
            RegistryWriteCall.IsMatch(assetsJs)
        };

    private static readonly JsonSerializerOptions AssetsJsonWrite = new() { WriteIndented = true };

    /// <summary>What a repair did to one file.</summary>
    public enum RepairOutcome
    {
        /// <summary>The file already matched the repaired form and was not written.</summary>
        AlreadyRepaired,

        /// <summary>The file can be repaired, and the repaired bytes are carried.</summary>
        Rewritten,

        /// <summary>
        /// The file could not be assessed or could not be repaired, and was left alone.
        /// Missing, unreadable, malformed and unsupported all land here, because none of
        /// them is a reason to call a folder healthy.
        /// </summary>
        NotPossible
    }

    /// <summary>
    /// The repaired text and what happened, so a caller can report a migration that
    /// could not run instead of implying it did.
    /// </summary>
    public sealed record AssetsRepairResult(
        RepairOutcome Outcome,
        string? RepairedText,
        bool ContentUrlsRestored,
        string Detail)
    {
        public bool Changed => Outcome == RepairOutcome.Rewritten;

        public static AssetsRepairResult Unchanged(string detail) =>
        new(RepairOutcome.AlreadyRepaired, null, false, detail);

        public static AssetsRepairResult CouldNotRepair(string detail) =>
            new(RepairOutcome.NotPossible, null, false, detail);
    }

    /// <summary>
    /// True only when all four sites of the registry access name the repaired key, and no
    /// site is left on the upstream name.
    ///
    /// Both halves matter. Naming the repaired key at four sites is what a finished
    /// migration looks like. Naming it nowhere is what a different uBlock build looks
    /// like, and calling that migrated is how an unrecognised script gets reported as a
    /// healthy folder. The read key, the two places the callback pulls the saved
    /// registry back out of what the read returned, and the write key are one mechanism
    /// and they move together: move the read and the write but not the callback and the
    /// extension reads a key nothing writes, then rebuilds from the bundled file on
    /// every start. Move the callback but not the write and the saved registry is never
    /// read back at all. Each of those is worse than not migrating.
    /// </summary>
    public static bool RegistryKeyIsRepaired(string assetsJs) =>
        RepairedSitesPresent(assetsJs).All(present => present)
            && !UpstreamSitesLeft(assetsJs).Any(left => left);

    /// <summary>
    /// Why a script is not in the repaired form, or null when it is. Split out so a
    /// caller can report a partly patched file, or one it does not recognise, as a fault
    /// rather than as a folder that needs no work.
    /// </summary>
    public static string? RegistryKeyNotRepairedReason(string? assetsJs)
    {
        if (assetsJs is null) return "there is no js/assets.js in this folder";
        if (RegistryKeyIsRepaired(assetsJs)) return null;

        var present = RepairedSitesPresent(assetsJs);
        var left = UpstreamSitesLeft(assetsJs);
        if (!present.Any(site => site))
        {
            return "js/assets.js does not carry the repaired registry key in any of its four " +
                   "places and does not carry the upstream access this repair matches on, so " +
                   "it is a script this repair does not recognise rather than one it has done";
        }

        var names = new[]
        {
            "the storage read", "the saved-registry guard", "the saved-registry read", "the storage write"
        };
        var stillUpstream = names.Where((_, i) => left[i]).ToList();
        var notYetMoved = names.Where((_, i) => !present[i]).ToList();

        var parts = new List<string>();
        if (stillUpstream.Count > 0)
            parts.Add("still on the upstream name: " + string.Join(", ", stillUpstream));
        if (notYetMoved.Count > 0)
            parts.Add("not yet on the repaired name: " + string.Join(", ", notYetMoved));

        return "js/assets.js is only partly migrated, " + string.Join("; ", parts) +
               ", so a saved registry would not be read back";
    }

    /// <summary>
    /// The content of assets.json as the pinned package ships it, or null when the
    /// package is absent or unreadable. This is where a previously stripped folder
    /// gets its remote urls back from, so no url is ever written that did not ship.
    /// </summary>
    public static string? ReadPristineAssetsJson(string bundledZipPath)
    {
        try
        {
            if (!File.Exists(bundledZipPath)) return null;
            using var zip = ZipFile.OpenRead(bundledZipPath);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.EndsWith("assets/assets.json", StringComparison.Ordinal)) continue;
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            }
        }
        catch (Exception)
        {
            // A package that cannot be read leaves the folder as it is. The caller
            // reports it rather than treating it as a repair.
        }
        return null;
    }

    /// <summary>
    /// The repaired assets.json for a folder.
    ///
    /// <paramref name="current"/> is what the folder holds,
    /// <paramref name="pristine"/> is what the pinned package ships. For every key both
    /// carry, the <c>contentURL</c> of the package is used, which puts the remote urls
    /// back and keeps the bundled local paths in their shipped order. Everything else
    /// in the folder's entry is kept, and a key the package does not carry is left
    /// alone, so nothing a user or a later package added is dropped.
    ///
    /// The mirror and diff keys keep their caret either way. That is the existing
    /// choice, it is not what blocked updates, and the origin url in
    /// <c>contentURL</c> is what the updater fetches.
    /// </summary>
    public static AssetsRepairResult Repair(string? current, string? pristine)
    {
        // Missing or unreadable is not a folder that needs no work. It is a folder whose
        // state could not be established, and it is reported as such.
        if (current is null) return AssetsRepairResult.CouldNotRepair("assets.json is missing or unreadable");

        JsonObject root;
        try
        {
            if (JsonNode.Parse(current) is not JsonObject parsed)
                return AssetsRepairResult.CouldNotRepair("assets.json did not parse as a JSON object");
            root = parsed;
        }
        catch (Exception ex)
        {
            return AssetsRepairResult.CouldNotRepair($"assets.json is malformed and was left alone ({ex.Message})");
        }

        JsonObject? upstream = null;
        if (pristine is not null)
        {
            try { upstream = JsonNode.Parse(pristine) as JsonObject; }
            catch (Exception) { upstream = null; }
        }

        var restored = false;
        foreach (var kvp in root.ToList())
        {
            if (kvp.Value is not JsonObject filter) continue;
            if (filter["off"]?.GetValue<bool>() == true) continue;

            if (upstream is not null
                && upstream[kvp.Key] is JsonObject upstreamFilter
                && upstreamFilter["contentURL"] is not null
                && filter["contentURL"] is not null)
            {
                var shipped = upstreamFilter["contentURL"].DeepClone();
                if (!filter["contentURL"]!.ToJsonString().Equals(shipped.ToJsonString(), StringComparison.Ordinal))
                {
                    filter["contentURL"] = shipped;
                    restored = true;
                }
            }

            // cdnURLs and patchURLs are renamed to a leading caret, which is how the
            // package ships them and how they stay. uBlock's updater does not read a
            // caret key and works the entry from the origin url in contentURL instead.
            // It is not a block on diff requests: the extension's own diff updater still
            // derives the cdn hosts from those origin urls.
            if (filter["cdnURLs"] is JsonNode cdn)
            {
                filter.Remove("cdnURLs");
                filter["^cdnURLs"] = cdn.DeepClone();
            }
            if (filter["patchURLs"] is JsonNode patch)
            {
                filter.Remove("patchURLs");
                filter["^patchURLs"] = patch.DeepClone();
            }
        }

        var text = root.ToJsonString(AssetsJsonWrite) + "\n";
        if (string.Equals(text, current, StringComparison.Ordinal))
        {
            return AssetsRepairResult.Unchanged(
                restored
                    ? "assets.json already carries the package content urls, left untouched"
                    : "assets.json is already in the repaired form, left untouched");
        }

        return new AssetsRepairResult(RepairOutcome.Rewritten, text, restored,
            restored
                ? "content urls restored from the pinned package and mirror keys disarmed"
                : "mirror and diff keys disarmed");
    }

    /// <summary>
    /// Moves every site of the registry access in the extension's own script: the storage
    /// read, the two places the callback pulls the saved registry back out of what the read
    /// returned, and the storage write. All four move together, because they are one
    /// mechanism. Moving only the storage key looks like a successful repair and is not:
    /// the callback would still look for the old property name inside the bin, find
    /// nothing, and rebuild the registry from the bundled file on every start.
    ///
    /// A file that already carries some of the four has them all completed, so a folder
    /// this build touched once is brought the rest of the way rather than being reported
    /// as a package it is not. A file that does not match the pinned shape at any site is
    /// reported as not possible and returned byte for byte, so a different uBlock version
    /// is never edited on a guess.
    /// </summary>
    public static AssetsRepairResult RepairRegistryKey(string? current)
    {
        if (current is null)
            return AssetsRepairResult.CouldNotRepair("js/assets.js is missing or unreadable");

        if (RegistryKeyIsRepaired(current))
            return AssetsRepairResult.Unchanged("the registry storage key is already in the repaired form");

        var left = UpstreamSitesLeft(current);
        if (!left.Any(site => site))
        {
            return AssetsRepairResult.CouldNotRepair(
                "js/assets.js does not carry the registry access this repair matches on, in any of " +
                "its four places; the file is one this repair does not recognise and was not edited");
        }

        var repaired = current;
        repaired = RegistryReadCall.Replace(repaired,
            $"cacheStorage.get('{RepairedRegistryKey}')", 1);
        repaired = RegistryBinGuard.Replace(repaired,
            $"bin['{RepairedRegistryKey}']", 1);
        repaired = RegistryBinRead.Replace(repaired,
            $"= bin['{RepairedRegistryKey}'];", 1);
        repaired = RegistryWriteCall.Replace(repaired,
            $"cacheStorage.set({{ '{RepairedRegistryKey}': assetSourceRegistry }})", 1);

        if (string.Equals(repaired, current, StringComparison.Ordinal))
            return AssetsRepairResult.Unchanged("the registry storage key is already in the repaired form");
        if (!RegistryKeyIsRepaired(repaired))
        {
            return AssetsRepairResult.CouldNotRepair(
                "the registry migration did not reach all four sites, so the file was not edited");
        }

        return new AssetsRepairResult(RepairOutcome.Rewritten, repaired, false,
            "the asset source registry storage key moved in all four places, so a cached " +
            "local-only registry is rebuilt once from the repaired file and read back after that");
    }
}
