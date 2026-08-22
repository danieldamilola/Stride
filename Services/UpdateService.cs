using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;

namespace StrideBrowser.Services;

/// <summary>
/// A completely native, zero-stress Micro-Updater that bypasses NetSparkle and Inno Setup.
/// It uses the official GitHub REST API to detect releases and downloads the .zip package.
/// </summary>
public sealed class UpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/danieldamilola/Stride/releases/latest";
    private static readonly HttpClient _http = new();

    public event EventHandler? UpdateAvailable;
    public event EventHandler<string>? UpdateFailed;
    public event Action? AppExitRequested;

    private string? _downloadedZipPath;
    private string? _latestVersion;

    public event EventHandler<double>? DownloadProgressChanged;
    public event EventHandler? DownloadCompleted;

    public string? LatestVersion => _latestVersion;

    public UpdateService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "StrideBrowser-Updater");
    }

    private void Fail(string message)
    {
        Trace.WriteLine($"[updater] {message}");
        UpdateFailed?.Invoke(this, message);
    }

    public async Task CheckForUpdatesQuietlyAsync()
    {
        try
        {
            var release = await _http.GetFromJsonAsync<GitHubRelease>(GitHubApiUrl);
            if (release is null || string.IsNullOrEmpty(release.TagName))
                return;

            string latestVersionStr = release.TagName.TrimStart('v');
            if (Version.TryParse(latestVersionStr, out var latestVersion))
            {
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (latestVersion > currentVersion)
                {
                    _latestVersion = release.TagName;
                    UpdateAvailable?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[updater] Check failed: {ex.Message}");
        }
    }

    public async Task<bool> DownloadUpdateAsync()
    {
        if (string.IsNullOrEmpty(_latestVersion))
        {
            Fail("No update is currently available.");
            return false;
        }

        try
        {
            string downloadUrl = $"https://github.com/danieldamilola/Stride/releases/download/{_latestVersion}/Stride-win-x64.zip";
            string tempDir = Path.GetTempPath();
            _downloadedZipPath = Path.Combine(tempDir, $"StrideUpdate_{Guid.NewGuid():N}.zip");

            using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(_downloadedZipPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                totalRead += read;

                if (canReportProgress)
                {
                    double progress = ((double)totalRead / totalBytes) * 100.0;
                    DownloadProgressChanged?.Invoke(this, progress);
                }
            }

            DownloadCompleted?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            Fail($"Update download failed: {ex.Message}");
            return false;
        }
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

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
    }
}