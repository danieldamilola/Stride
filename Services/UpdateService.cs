using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace StrideBrowser.Services;

/// <summary>
/// A completely native, zero-stress Micro-Updater with no external updater frameworks.
/// It uses the official GitHub REST API to detect releases and downloads the .zip package.
/// </summary>
public sealed class UpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/danieldamilola/Stride/releases/latest";
    private const string AssetName = "Stride-win-x64.zip";

    private readonly HttpClient _http;

    /// <summary>
    /// SHA-256 GitHub reports for the release asset, captured during the version check.
    /// Empty when the API did not supply one.
    /// </summary>
    private string _expectedAssetDigest = "";

    /// <summary>Asset URL reported by the release check. Null falls back to the built URL.</summary>
    private string? _downloadUrl;

    public event EventHandler? UpdateAvailable;
    public event EventHandler<string>? UpdateFailed;
    public event Action? AppExitRequested;

    private string? _downloadedZipPath;
    private string? _latestVersion;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    public event EventHandler<double>? DownloadProgressChanged;
    public event EventHandler? DownloadCompleted;

    public string? LatestVersion => _latestVersion;

    public UpdateService(HttpClient http)
    {
        _http = http;
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "StrideBrowser-Updater");
        }
    }

    private void Fail(string message)
    {
        Trace.WriteLine($"[updater] {message}");
        UpdateFailed?.Invoke(this, message);
    }

    /// <summary>
    /// Accepts only <c>v</c> followed by a dotted numeric version, optionally with a
    /// pre-release suffix.
    /// <para>
    /// The tag is interpolated straight into the download URL and handed to the updater
    /// process. Without this, a tampered or unexpected API response could steer the request
    /// somewhere else by putting path separators or a query string into the tag.
    /// </para>
    /// </summary>
    public static bool IsAcceptableTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return false;
        if (tag.Length > 32) return false;
        if (tag[0] != 'v') return false;

        var rest = tag[1..];
        var dash = rest.IndexOf('-');
        if (dash >= 0) rest = rest[..dash];

        var parts = rest.Split('.');
        if (parts.Length is < 1 or > 4) return false;

        foreach (var part in parts)
        {
            if (part.Length == 0 || part.Length > 6) return false;
            foreach (var c in part)
                if (!char.IsAsciiDigit(c)) return false;
        }
        return true;
    }

    /// <summary>
    /// Upper bound on an update package, so a hostile or broken response cannot fill the disk.
    /// A current Stride release is a small fraction of this.
    /// </summary>
    internal const long MaxPackageBytes = 256L * 1024 * 1024;

    public async Task<bool?> CheckForUpdatesQuietlyAsync()
    {
        _latestVersion = null;
        try
        {
            var release = await _http.GetFromJsonAsync<GitHubRelease>(GitHubApiUrl);
            if (release is null || string.IsNullOrEmpty(release.TagName))
                return false;

            if (!IsAcceptableTag(release.TagName))
            {
                Trace.WriteLine($"[updater] Rejected release tag '{release.TagName}'. Expected v<major>.<minor>.<patch>.");
                return false;
            }

            // Prefer the asset URL and digest the API reports over a string-built URL. This keeps the
            // request on the exact asset GitHub named and gives DownloadUpdateAsync something
            // to verify the bytes against.
            var asset = release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, AssetName, StringComparison.Ordinal) &&
                a.Url is not null &&
                a.Url.StartsWith("https://github.com/danieldamilola/Stride/releases/download/", StringComparison.Ordinal));

            _expectedAssetDigest = ParseAssetDigest(asset?.Digest) ?? "";

            string latestVersionStr = release.TagName.TrimStart('v');
            if (Version.TryParse(latestVersionStr, out var latestVersion))
            {
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (latestVersion > currentVersion)
                {
                    _latestVersion = release.TagName;
                    _downloadUrl = asset?.Url;
                    UpdateAvailable?.Invoke(this, EventArgs.Empty);
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[updater] Check failed: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> DownloadUpdateAsync()
    {
        if (string.IsNullOrEmpty(_latestVersion))
        {
            Fail("No update is currently available.");
            return false;
        }

        // Re-entrancy guard: only one download at a time.
        if (!await _downloadGate.WaitAsync(0))
        {
            Fail("An update download is already in progress.");
            return false;
        }

        try
        {
            // The API-reported asset URL when the release check found one, otherwise the built URL.
            // IsAcceptableTag already constrained the tag, so the fallback cannot be steered
            // by path separators or a query string.
            string downloadUrl = _downloadUrl
                ?? $"https://github.com/danieldamilola/Stride/releases/download/{_latestVersion}/Stride-win-x64.zip";
            string tempDir = Path.GetTempPath();
            _downloadedZipPath = Path.Combine(tempDir, $"StrideUpdate_{Guid.NewGuid():N}.zip");

            using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using (var fileStream = new FileStream(_downloadedZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    totalRead += read;
                    if (totalRead > MaxPackageBytes)
                    {
                        Fail($"Update package exceeds the {MaxPackageBytes} byte limit.");
                        await fileStream.DisposeAsync();
                        CleanupZip();
                        return false;
                    }

                    await fileStream.WriteAsync(buffer, 0, read);

                    if (canReportProgress)
                    {
                        double progress = ((double)totalRead / totalBytes) * 100.0;
                        // Marshal to the UI thread: subscribers touch WPF controls.
                        var ui = System.Windows.Application.Current?.Dispatcher;
                        if (ui is not null)
                            await ui.InvokeAsync(() => DownloadProgressChanged?.Invoke(this, progress));
                        else
                            DownloadProgressChanged?.Invoke(this, progress);
                    }
                }
                await fileStream.FlushAsync();
            }

            // Sanity check: confirm the download is a valid, non-trivial zip before trusting it.
            var info = new FileInfo(_downloadedZipPath);
            if (info.Length < 1024 || !IsValidZip(_downloadedZipPath))
            {
                Fail("Downloaded update is not a valid package.");
                CleanupZip();
                return false;
            }

            // The updater replaces every file in the install directory with this package, so
            // the archive shape is screened before install is offered.
            if (!IsPackageWithinLimits(_downloadedZipPath, out var limitReason))
            {
                Fail($"Downloaded update was rejected: {limitReason}");
                CleanupZip();
                return false;
            }

            // Confirm the bytes match the digest GitHub published for this asset. This catches
            // a corrupted or substituted download. It is not authentication: both the package
            // and the digest come from GitHub, so signing the package is what closes that.
            if (!string.IsNullOrEmpty(_expectedAssetDigest))
            {
                var actual = await ComputeSha256Async(_downloadedZipPath);
                if (!string.Equals(actual, _expectedAssetDigest, StringComparison.OrdinalIgnoreCase))
                {
                    Trace.WriteLine($"[updater] Digest mismatch. Expected={_expectedAssetDigest}, Actual={actual}");
                    Fail("Downloaded update does not match the published checksum.");
                    CleanupZip();
                    return false;
                }
            }
            else
            {
                Trace.WriteLine("[updater] Release carried no asset digest, so the package was not checksum verified.");
            }

            DownloadCompleted?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            Fail($"Update download failed: {ex.Message}");
            CleanupZip();
            return false;
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    /// <summary>Ceilings for an update package, all well above a real Stride release.</summary>
    internal const int MaxPackageEntries = 20000;
    internal const long MaxPackageUncompressedBytes = 1024L * 1024 * 1024;
    internal const long MaxPackageEntryBytes = 128L * 1024 * 1024;

    /// <summary>
    /// Bounds the archive by entry count, total declared output and largest single entry.
    /// The declared sizes come from the zip central directory, so a bomb can inflate them
    /// without spending bytes on the wire.
    /// </summary>
    internal static bool IsPackageWithinLimits(string path, out string reason)
    {
        reason = "";
        try
        {
            using var zip = ZipFile.OpenRead(path);

            if (zip.Entries.Count > MaxPackageEntries)
            {
                reason = $"{zip.Entries.Count} entries exceeds {MaxPackageEntries}";
                return false;
            }

            long total = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.Length > MaxPackageEntryBytes)
                {
                    reason = $"entry '{entry.FullName}' declares {entry.Length} bytes";
                    return false;
                }

                total += entry.Length;
                if (total > MaxPackageUncompressedBytes)
                {
                    reason = $"declared output exceeds {MaxPackageUncompressedBytes} bytes";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>Confirms the file is a real ZIP archive with at least one entry.</summary>
    private static bool IsValidZip(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private void CleanupZip()
    {
        try { if (_downloadedZipPath is not null && File.Exists(_downloadedZipPath)) File.Delete(_downloadedZipPath); } catch { }
        _downloadedZipPath = null;
    }

    public void InstallUpdate()
    {
        _ = TriggerMicroUpdaterAsync();
    }

    private async Task TriggerMicroUpdaterAsync()
    {
        if (string.IsNullOrEmpty(_downloadedZipPath) || !File.Exists(_downloadedZipPath))
        {
            Fail("Update file is missing.");
            return;
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string updaterExe = Path.Combine(baseDir, "Stride.Updater.exe");

        if (!File.Exists(updaterExe))
        {
            Fail("Stride.Updater.exe is missing. Cannot perform update.");
            return;
        }

        // Fire the event so the UI can close safely
        AppExitRequested?.Invoke();

        // Launch the invisible updater
        var psi = new ProcessStartInfo
        {
            FileName = updaterExe,
            Arguments = $"\"{_downloadedZipPath}\" \"{baseDir.TrimEnd('\\')}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(psi);

        // Ensure we shut down so the updater can swap the files
        Application.Current?.Dispatcher.Invoke(() => Application.Current.Shutdown());
    }

    /// <summary>
    /// Pulls the sha256 out of an asset descriptor.
    /// <para>
    /// GitHub publishes the digest of each uploaded asset on the release and repeats it at the
    /// blob URL. Checking the downloaded bytes against it means a corrupted or substituted
    /// package is refused before the updater is handed anything. It is not a substitute for
    /// signing: both values come from GitHub, so an attacker who can change the release can
    /// change the digest too. Signing the package with the key in tools/signing is the fix for
    /// that, and the Ed25519 signer for it already exists in this repo.
    /// </para>
    /// </summary>
    internal static string? ParseAssetDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        const string Prefix = "sha256:";
        if (!digest.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var hex = digest[Prefix.Length..].Trim();
        if (hex.Length != 64) return null;
        foreach (var c in hex)
            if (!Uri.IsHexDigit(c)) return null;
        return hex.ToUpperInvariant();
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? Url { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }
}
