using System.Text.RegularExpressions;
using UmberBrowser.Services;

namespace UmberBrowser.Tests;

/// <summary>
/// Builds the form of js/assets.js the pinned package ships, from the form the repair
/// produces.
///
/// The name has to go back on the two callback reads as a plain property access, not as
/// a bracketed one. Putting the old name back inside the brackets the repair added
/// leaves <c>bin['assetSourceRegistry']</c>, which is neither the upstream script nor the
/// repaired one, and a fixture built that way tests a state the repair never creates.
/// </summary>
internal static class PinnedScriptForm
{
    public static string ToUpstream(string repaired) =>
        repaired
            .Replace($"bin['{UBlockAssetRepair.RepairedRegistryKey}']", "bin.assetSourceRegistry")
            .Replace(UBlockAssetRepair.RepairedRegistryKey, "assetSourceRegistry");

    /// <summary>The upstream form with only the storage read and write moved.</summary>
    public static string StorageKeyOnly(string upstream) =>
        new Regex(@"cacheStorage\.get\(\s*'assetSourceRegistry'\s*\)")
            .Replace(upstream, $"cacheStorage.get('{UBlockAssetRepair.RepairedRegistryKey}')", 1)
            .Replace("cacheStorage.set({ assetSourceRegistry });",
                $"cacheStorage.set({{ '{UBlockAssetRepair.RepairedRegistryKey}': assetSourceRegistry }})");
}