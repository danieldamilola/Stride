using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using StrideBrowser.Helpers;
using Microsoft.Web.WebView2.Core;

namespace StrideBrowser.Services;

/// <summary>
/// Manages WebView2 browser extensions including built-in uBlock Origin.
/// </summary>
public sealed class ExtensionManager
{
    private static readonly string ExtensionsDir = AppPaths.ExtensionsDir;

    private const string UBlockVersion = "1.71.0";

    private const string UBlockDownloadUrl =
        $"https://github.com/gorhill/uBlock/releases/download/{UBlockVersion}/uBlock0_{UBlockVersion}.chromium.zip";

    /// <summary>Path to the stored SHA-256 hash for TOFU (Trust On First Use) verification.</summary>
    private static readonly string HashFilePath = AppPaths.UBlockHashFile;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task InitializeAsync(CoreWebView2 webview)
    {
        try
        {
            var extensions = await webview.Profile.GetBrowserExtensionsAsync();
            
            var ublock = extensions.FirstOrDefault(e =>
                e.Name.Contains("uBlock", StringComparison.OrdinalIgnoreCase));

            if (ublock is not null)
            {
                Trace.WriteLine($"ExtensionManager: uBlock Origin already loaded (id={ublock.Id}).");
                return;
            }

            var folderPath = await EnsureUBlockDownloadedAsync();
            if (folderPath is null)
            {
                Trace.WriteLine("ExtensionManager: failed to obtain uBlock folder.");
                return;
            }

            await LoadUnpackedAsync(webview, folderPath);
            Trace.WriteLine("ExtensionManager: uBlock Origin loaded successfully.");
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
                return existingManifest;
            }

            Directory.CreateDirectory(ExtensionsDir);
            var zipPath = Path.Combine(ExtensionsDir, $"uBlock0_{UBlockVersion}.zip");

            Trace.WriteLine($"ExtensionManager: downloading uBlock from {UBlockDownloadUrl}");
            const int maxRetries = 3;
            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var response = await Http.GetAsync(UBlockDownloadUrl);
                    response.EnsureSuccessStatusCode();

                    await using (var fs = File.Create(zipPath))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                    break;
                }
                catch when (attempt < maxRetries)
                {
                    Trace.WriteLine($"ExtensionManager: download attempt {attempt}/{maxRetries} failed, retrying...");
                    await Task.Delay(1000 * attempt);
                }
            }

            if (!await VerifyTofuHashAsync(zipPath))
            {
                try { File.Delete(zipPath); } catch { }
                return null;
            }

            if (Directory.Exists(targetDir))
            {
                var tempDir = Path.Combine(ExtensionsDir, $"uBlock0_{UBlockVersion}_{Guid.NewGuid():N}");
                Directory.Move(targetDir, tempDir);
                _ = Task.Run(() => { try { Directory.Delete(tempDir, recursive: true); } catch { } });
            }

            ZipFile.ExtractToDirectory(zipPath, targetDir);
            try { File.Delete(zipPath); } catch { }

            var manifestDir = FindManifestDirectory(targetDir);
            if (manifestDir is null)
            {
                Trace.WriteLine("ExtensionManager: manifest.json not found after extraction.");
                return null;
            }

            Trace.WriteLine($"ExtensionManager: uBlock extracted to {manifestDir}");
            return manifestDir;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager.EnsureUBlockDownloadedAsync failed: {ex.Message}");
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
        var actualHash = await ComputeFileHashAsync(zipPath);

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
            await File.WriteAllTextAsync(HashFilePath, actualHash);
            Trace.WriteLine($"ExtensionManager: first download, stored SHA-256={actualHash}");
        }

        return true;
    }

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hashBytes);
    }
}
