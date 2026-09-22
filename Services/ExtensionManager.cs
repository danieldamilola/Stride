using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using StrideBrowser.Helpers;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// Manages WebView2 browser extensions including built-in uBlock Origin.
/// </summary>
public sealed class ExtensionManager
{
    private static readonly string ExtensionsDir = AppPaths.ExtensionsDir;

    private const string UBlockVersion = "1.73.0";

    private const string UBlockDownloadUrl =
        $"https://github.com/gorhill/uBlock/releases/download/{UBlockVersion}/uBlock0_{UBlockVersion}.chromium.zip";

    private const string TCLensUrl = "https://github.com/danieldamilola/T-C/archive/refs/heads/main.zip";

    /// <summary>Path to the stored SHA-256 hash for TOFU (Trust On First Use) verification.</summary>
    private static readonly string HashFilePath = AppPaths.UBlockHashFile + "." + UBlockVersion;

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!c.DefaultRequestHeaders.Contains("User-Agent"))
            c.DefaultRequestHeaders.Add("User-Agent", "StrideBrowser-ExtensionManager");
        return c;
    }

    public async Task InitializeAsync(CoreWebView2 webview, BrowserSettings settings)
    {
        try
        {
            var extensions = await webview.Profile.GetBrowserExtensionsAsync();
            
            var ublock = extensions.FirstOrDefault(e =>
                e.Name.Equals("uBlock Origin", StringComparison.OrdinalIgnoreCase) ||
                e.Name.Contains("uBlock", StringComparison.OrdinalIgnoreCase));

            // Clean up legacy IDM extension from older versions
            var idm = extensions.FirstOrDefault(e =>
                e.Name.Contains("IDM", StringComparison.OrdinalIgnoreCase) ||
                e.Name.Contains("Internet Download Manager", StringComparison.OrdinalIgnoreCase));

            if (idm is not null)
            {
                Trace.WriteLine($"ExtensionManager: Removing legacy IDM extension (id={idm.Id}).");
                await idm.RemoveAsync();
            }

            // Always ensure the unpacked folder exists on disk, downloading if needed.
            // This re-positions the extension after an auto-update that shipped without the bundled zip.
            var folderPath = await EnsureUBlockDownloadedAsync();

            // Detect post-update or version upgrade: if a newer folder was just created but
            // WebView2 still holds the old registration, cycle it so the new files are used.
            bool hasOldVersionDir = false;
            try
            {
                if (Directory.Exists(ExtensionsDir))
                {
                    hasOldVersionDir = Directory.EnumerateDirectories(ExtensionsDir, "uBlock0_*")
                        .Any(d => !string.Equals(Path.GetFileName(d), $"uBlock0_{UBlockVersion}", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch { }

            bool isPostUpdate = false;
            try { isPostUpdate = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "post_update.flag")); } catch { }

            if (ublock is not null && hasOldVersionDir && folderPath is not null)
            {
                Trace.WriteLine("ExtensionManager: detected uBlock version upgrade, re-adding extension.");
                await RemoveExtensionAsync(ublock);
                ublock = null;
            }
            else if (ublock is not null && isPostUpdate && folderPath is not null)
            {
                // Updater just swapped files; WebView2 profile may still reference stale paths.
                // Remove and re-add to reposition onto the freshly extracted folder.
                Trace.WriteLine("ExtensionManager: post-update flag detected, cycling extension.");
                await RemoveExtensionAsync(ublock);
                ublock = null;
            }

            // Post-update recovery: if WebView2 still reports uBlock but the folder vanished,
            // the registration is orphaned and will not block anything. Remove it so we can re-add.
            if (ublock is not null && folderPath is null)
            {
                Trace.WriteLine($"ExtensionManager: uBlock registered but folder missing, removing orphan (id={ublock.Id}).");
                await RemoveExtensionAsync(ublock);
                ublock = null;
                // Try once more to obtain the folder after orphan removal
                folderPath = await EnsureUBlockDownloadedAsync();
            }

            // Also handle the upgrade case where folder exists but manifest was not found
            // before; EnsureUBlockDownloadedAsync would have repaired it. If we still have
            // a stale registration with missing manifest, re-create it.
            if (ublock is not null && FindManifestDirectory(Path.Combine(ExtensionsDir, $"uBlock0_{UBlockVersion}")) is null)
            {
                Trace.WriteLine("ExtensionManager: uBlock folder corrupt after update, re-adding.");
                await RemoveExtensionAsync(ublock);
                ublock = null;
                folderPath = await EnsureUBlockDownloadedAsync();
            }

            if (ublock is not null)
            {
                Trace.WriteLine($"ExtensionManager: uBlock Origin already loaded (id={ublock.Id}).");
                // Sync the enable state with the BrowserSettings
                if (ublock.IsEnabled != settings.AdBlockEnabled)
                {
                    await ToggleExtensionAsync(ublock, settings.AdBlockEnabled);
                }
                CleanupOldVersions();
            }
            else if (folderPath is not null)
            {
                await LoadUnpackedAsync(webview, folderPath);
                Trace.WriteLine("ExtensionManager: uBlock Origin loaded successfully.");
                
                // The extension is enabled by default upon loading, check if we need to disable it immediately
                if (!settings.AdBlockEnabled)
                {
                    var newlyLoaded = (await webview.Profile.GetBrowserExtensionsAsync())
                        .FirstOrDefault(e => e.Name.Equals("uBlock Origin", StringComparison.OrdinalIgnoreCase) ||
                                             e.Name.Contains("uBlock", StringComparison.OrdinalIgnoreCase));
                    if (newlyLoaded != null) await ToggleExtensionAsync(newlyLoaded, false);
                }
                CleanupOldVersions();
            }
            else
            {
                Trace.WriteLine("ExtensionManager: uBlock folder unavailable, extension not loaded.");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager.InitializeAsync failed: {ex.Message}");
        }
    }

    public async Task<string?> EnsureUBlockDownloadedAsync()
    {
        try
        {
            var targetDir = Path.Combine(ExtensionsDir, $"uBlock0_{UBlockVersion}");
            var existingManifest = FindManifestDirectory(targetDir);
            if (existingManifest is not null)
            {
                Trace.WriteLine($"ExtensionManager: uBlock already extracted at {existingManifest}");
                // Helium parity: ensure assets are stripped even for already extracted folder
                StripUBlockAssets(existingManifest);
                return existingManifest;
            }

            Directory.CreateDirectory(ExtensionsDir);

            // Target may exist but be corrupt (no manifest). Clear it so we can re-extract.
            if (Directory.Exists(targetDir) && existingManifest is null)
            {
                Trace.WriteLine($"ExtensionManager: target dir exists but no manifest, clearing {targetDir}");
                try { Directory.Delete(targetDir, recursive: true); } catch (Exception ex) { Trace.WriteLine($"Failed to clear corrupt dir: {ex.Message}"); }
            }

            // GitHub asset is named uBlock0_*.chromium.zip; keep local cache name matching for bundling.
            var localZipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Extensions", $"uBlock0_{UBlockVersion}.chromium.zip");
            var fallbackZipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Extensions", $"uBlock0_{UBlockVersion}.zip");
            // Accept either naming for backward compat with older bundles
            if (!File.Exists(localZipPath) && File.Exists(fallbackZipPath))
                localZipPath = fallbackZipPath;

            Trace.WriteLine($"ExtensionManager: checking local uBlock zip at {localZipPath}");

            string? zipToExtract = null;
            string? tempDownloadPath = null;

            try
            {
                if (File.Exists(localZipPath) && await VerifyTofuHashAsync(localZipPath))
                {
                    zipToExtract = localZipPath;
                    Trace.WriteLine("ExtensionManager: using bundled local zip.");
                }
                else
                {
                    if (File.Exists(localZipPath))
                        Trace.WriteLine("ExtensionManager: local zip hash failed, falling back to remote download.");
                    else
                        Trace.WriteLine("ExtensionManager: local zip not found, downloading from GitHub.");

                    tempDownloadPath = Path.Combine(Path.GetTempPath(), $"uBlock0_{UBlockVersion}_{Guid.NewGuid():N}.zip");
                    Trace.WriteLine($"ExtensionManager: downloading uBlock from {UBlockDownloadUrl} to {tempDownloadPath}");
                    var data = await Http.GetByteArrayAsync(UBlockDownloadUrl);
                    await File.WriteAllBytesAsync(tempDownloadPath, data);

                    if (!await VerifyTofuHashAsync(tempDownloadPath))
                    {
                        Trace.WriteLine("ExtensionManager: downloaded zip hash mismatch, aborting.");
                        try { File.Delete(tempDownloadPath); } catch { }
                        tempDownloadPath = null;
                        return null;
                    }

                    zipToExtract = tempDownloadPath;
                }

                if (Directory.Exists(targetDir))
                {
                    var tempDir = Path.Combine(ExtensionsDir, $"uBlock0_{UBlockVersion}_{Guid.NewGuid():N}");
                    Directory.Move(targetDir, tempDir);
                    _ = Task.Run(() => { try { Directory.Delete(tempDir, recursive: true); } catch (Exception ex) { Trace.WriteLine($"Failed to delete temp dir: {ex}"); } });
                }

                ZipFile.ExtractToDirectory(zipToExtract!, targetDir);

                // Helium parity: strip CDN/patch URLs from assets.json to prevent first-run phone-home
                // Mirrors devutils/clear-ublock-assets.js: keep only local paths in contentURL, rename cdnURLs/patchURLs to ^cdnURLs/^patchURLs
                var extractedManifest = FindManifestDirectory(targetDir);
                if (extractedManifest is not null)
                    StripUBlockAssets(extractedManifest);

                var manifestDir = FindManifestDirectory(targetDir);
                if (manifestDir is null)
                {
                    Trace.WriteLine("ExtensionManager: manifest.json not found after extraction.");
                    return null;
                }

                Trace.WriteLine($"ExtensionManager: uBlock extracted to {manifestDir}");
                return manifestDir;
            }
            finally
            {
                if (tempDownloadPath is not null && File.Exists(tempDownloadPath))
                {
                    try { File.Delete(tempDownloadPath); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager.EnsureUBlockDownloadedAsync failed: {ex}");
            return null;
        }
    }


    public async Task LoadUnpackedAsync(CoreWebView2 webview, string folderPath)
    {
        try
        {
            await webview.Profile.AddBrowserExtensionAsync(folderPath);
            Trace.WriteLine($"ExtensionManager: loaded extension from {folderPath}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager.LoadUnpackedAsync failed: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<CoreWebView2BrowserExtension>> GetExtensionsAsync(CoreWebView2 webview)
    {
        try
        {
            return await webview.Profile.GetBrowserExtensionsAsync();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager.GetExtensionsAsync failed: {ex.Message}");
            return Array.Empty<CoreWebView2BrowserExtension>();
        }
    }

    public async Task ToggleExtensionAsync(CoreWebView2BrowserExtension ext, bool enabled)
    {
        try
        {
            await ext.EnableAsync(enabled);
            Trace.WriteLine($"ExtensionManager: extension '{ext.Name}' enabled={enabled}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager.ToggleExtensionAsync failed: {ex.Message}");
        }
    }

    public async Task RemoveExtensionAsync(CoreWebView2BrowserExtension ext)
    {
        try
        {
            await ext.RemoveAsync();
            Trace.WriteLine($"ExtensionManager: extension '{ext.Name}' removed.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager.RemoveExtensionAsync failed: {ex.Message}");
        }
    }

    private static string? FindManifestDirectory(string rootDir)
    {
        if (!Directory.Exists(rootDir))
            return null;

        if (File.Exists(Path.Combine(rootDir, "manifest.json")))
            return rootDir;

        foreach (var dir in Directory.EnumerateDirectories(rootDir, "*", SearchOption.AllDirectories))
        {
            if (File.Exists(Path.Combine(dir, "manifest.json")))
                return dir;
        }

        return null;
    }

    private async Task<bool> VerifyTofuHashAsync(string zipPath)
    {
        if (!File.Exists(zipPath))
        {
            Trace.WriteLine($"ExtensionManager: zip not found at {zipPath}");
            return false;
        }

        string actualHash;
        try
        {
            actualHash = await ComputeFileHashAsync(zipPath);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager: hash compute failed for {zipPath}: {ex.Message}");
            return false;
        }

        if (File.Exists(HashFilePath))
        {
            var storedHash = (await File.ReadAllTextAsync(HashFilePath)).Trim();
            if (!string.Equals(actualHash, storedHash, StringComparison.OrdinalIgnoreCase))
            {
                Trace.WriteLine($"ExtensionManager: SHA-256 mismatch. Expected={storedHash}, Actual={actualHash}");
                return false;
            }
            Trace.WriteLine("ExtensionManager: SHA-256 hash verified.");
        }
        else
        {
            try { await File.WriteAllTextAsync(HashFilePath, actualHash); } catch (Exception ex) { Trace.WriteLine($"Failed to store hash: {ex.Message}"); }
            Trace.WriteLine($"ExtensionManager: first download, stored SHA-256={actualHash}");
        }

        return true;
    }

    private void CleanupOldVersions()
    {
        try
        {
            if (!Directory.Exists(ExtensionsDir)) return;
            foreach (var dir in Directory.EnumerateDirectories(ExtensionsDir, "uBlock0_*"))
            {
                var name = Path.GetFileName(dir);
                if (!string.Equals(name, $"uBlock0_{UBlockVersion}", StringComparison.OrdinalIgnoreCase))
                {
                    Trace.WriteLine($"ExtensionManager: removing old version {name}");
                    try { Directory.Delete(dir, true); } catch (Exception ex) { Trace.WriteLine($"Failed to delete old version {name}: {ex.Message}"); }
                }
            }
        }
        catch (Exception ex) { Trace.WriteLine($"CleanupOldVersions failed: {ex.Message}"); }
    }

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hashBytes);
    }

    private static void StripUBlockAssets(string manifestDir)
    {
        try
        {
            var assetsPath = Path.Combine(manifestDir, "assets", "assets.json");
            if (!File.Exists(assetsPath)) return;

            var jsonText = File.ReadAllText(assetsPath);
            var root = JsonNode.Parse(jsonText) as JsonObject;
            if (root is null) return;

            foreach (var kvp in root.ToList())
            {
                if (kvp.Value is not JsonObject filter) continue;
                if (filter["off"]?.GetValue<bool>() == true) continue;

                // Keep only local paths in contentURL, strip remote URLs
                // Helium: [c].flat().filter(s => !URL.canParse(s)) handles string or array
                var contentNode = filter["contentURL"];
                if (contentNode is not null)
                {
                    var kept = new JsonArray();
                    if (contentNode is JsonArray contentUrls)
                    {
                        foreach (var entry in contentUrls)
                        {
                            var s = entry?.GetValue<string>();
                            if (s is null) continue;
                            if (!Uri.IsWellFormedUriString(s, UriKind.Absolute))
                                kept.Add(s);
                        }
                    }
                    else if (contentNode.GetValueKind() == System.Text.Json.JsonValueKind.String)
                    {
                        var s = contentNode.GetValue<string>();
                        if (!Uri.IsWellFormedUriString(s, UriKind.Absolute))
                            kept.Add(s);
                    }
                    filter["contentURL"] = kept;
                }

                // Rename cdnURLs -> ^cdnURLs and patchURLs -> ^patchURLs to disable CDN fetches
                if (filter.ContainsKey("cdnURLs"))
                {
                    var val = filter["cdnURLs"];
                    filter.Remove("cdnURLs");
                    filter["^cdnURLs"] = val?.DeepClone();
                }
                if (filter.ContainsKey("patchURLs"))
                {
                    var val = filter["patchURLs"];
                    filter.Remove("patchURLs");
                    filter["^patchURLs"] = val?.DeepClone();
                }
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(assetsPath, root.ToJsonString(options) + "\n");
            Trace.WriteLine("ExtensionManager: stripped uBlock assets.json CDN URLs");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager: StripUBlockAssets failed: {ex.Message}");
        }
    }

    public ExtensionHealth GetHealth()
    {
        var targetDir = Path.Combine(ExtensionsDir, $"uBlock0_{UBlockVersion}");
        var manifestDir = FindManifestDirectory(targetDir);
        var hasFolder = manifestDir is not null;
        var hasZip = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Extensions", $"uBlock0_{UBlockVersion}.chromium.zip")) ||
                     File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Extensions", $"uBlock0_{UBlockVersion}.zip"));
        var hasHash = File.Exists(HashFilePath);
        var assetsStripped = false;
        if (hasFolder)
        {
            var assetsPath = Path.Combine(manifestDir!, "assets", "assets.json");
            if (File.Exists(assetsPath))
            {
                try
                {
                    var text = File.ReadAllText(assetsPath);
                    assetsStripped = text.Contains("\"^cdnURLs\"") || !text.Contains("cdn.jsdelivr.net");
                }
                catch { }
            }
        }
        return new ExtensionHealth(hasFolder, hasZip, hasHash, assetsStripped, UBlockVersion);
    }

    public sealed record ExtensionHealth(bool HasFolder, bool HasZip, bool HasHash, bool AssetsStripped, string Version);
}
