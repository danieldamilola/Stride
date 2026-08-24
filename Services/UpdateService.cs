using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
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
    private readonly HttpClient _http;

    public event EventHandler? UpdateAvailable;
    public event EventHandler<string>? UpdateFailed;
    public event Action? AppExitRequested;

    private string? _downloadedZipPath;
    private string? _latestVersion;

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

    public async Task<bool?> CheckForUpdatesQuietlyAsync()
    {
        _latestVersion = null;
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var response = await _http.GetAsync(GitHubApiUrl, cts.Token);
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    Trace.WriteLine($"[updater] GitHub rate limited {response.StatusCode}");
                    return null;
                }
                response.EnsureSuccessStatusCode();
                var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: cts.Token);
                if (release is null || string.IsNullOrEmpty(release.TagName))
                    return false;

                string latestVersionStr = release.TagName.TrimStart('v').Split('-')[0].Split('+')[0].Trim();
                if (Version.TryParse(latestVersionStr, out var latestVersion))
                {
                    var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                    if (currentVersion != null && latestVersion > currentVersion)
                    {
                        _latestVersion = release.TagName;
                        UpdateAvailable?.Invoke(this, EventArgs.Empty);
                        return true;
                    }
                }
                else
                {
                    Trace.WriteLine($"[updater] Unable to parse version {release.TagName}");
                }
                return false;
            }
            catch (OperationCanceledException ex)
            {
                Trace.WriteLine($"[updater] Check timed out attempt {attempt}: {ex.Message}");
                if (attempt == maxAttempts) return null;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[updater] Check failed attempt {attempt}: {ex.Message}");
                if (attempt == maxAttempts) return null;
            }
            if (attempt < maxAttempts)
                await Task.Delay(500 * attempt);
        }
        return null;
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
            var buffer = new byte[81920];
            long totalRead = 0;
            int read;

            // Write to temp file - scope ensures flush before validation
            {
                using var fileStream = new FileStream(_downloadedZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
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
                await fileStream.FlushAsync();
            }

            // Validate download - size and zip integrity
            var fileInfo = new FileInfo(_downloadedZipPath);
            if (fileInfo.Length == 0)
                throw new InvalidDataException("Downloaded file is empty");

            if (canReportProgress && totalRead != totalBytes)
                throw new InvalidDataException($"Incomplete download {totalRead} of {totalBytes} bytes");

            try
            {
                using var zip = ZipFile.OpenRead(_downloadedZipPath);
                bool hasStrideExe = zip.Entries.Any(e => e.Name.Equals("Stride.exe", StringComparison.OrdinalIgnoreCase));
                if (!hasStrideExe)
                    throw new InvalidDataException("Downloaded zip does not contain Stride.exe");
                if (zip.Entries.Count == 0)
                    throw new InvalidDataException("Downloaded zip is empty");
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Downloaded file is not a valid zip: {ex.Message}", ex);
            }

            DownloadCompleted?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            try { if (!string.IsNullOrEmpty(_downloadedZipPath) && File.Exists(_downloadedZipPath)) File.Delete(_downloadedZipPath); } catch { }
            _downloadedZipPath = null;
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

        // Launch the invisible updater - ArgumentList handles spaces and quoting safely
        var psi = new ProcessStartInfo
        {
            FileName = updaterExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        psi.ArgumentList.Add(_downloadedZipPath);
        psi.ArgumentList.Add(baseDir.TrimEnd('\\'));

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