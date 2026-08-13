using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.WPF;

namespace StrideBrowser.Services;

public class UpdateService
{
    private SparkleUpdater _sparkle;
    private const string AppcastUrl = "https://raw.githubusercontent.com/danieldamilola/Stride/main/appcast.xml";
    private const string PublicKeyBase64 = "Ka3hqHLB+ZNutVBleNvne9HFFkk4gvAqYiOV5c7VewQ=";

    public event EventHandler? UpdateAvailable;

    public UpdateService()
    {
        _sparkle = new SparkleUpdater(AppcastUrl, new Ed25519Checker(SecurityMode.Strict, PublicKeyBase64))
        {
            UIFactory = new UIFactory(),
            UseNotificationToast = false // We manage our own red dot notification instead
        };

        _sparkle.UpdateDetected += (sender, e) =>
        {
            UpdateAvailable?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>
    /// Silently checks for updates in the background. If one is found, fires the UpdateAvailable event.
    /// </summary>
    public async Task CheckForUpdatesQuietlyAsync()
    {
        try
        {
            var updateInfo = await _sparkle.CheckForUpdatesQuietly();
            if (updateInfo.Status == NetSparkleUpdater.Enums.UpdateStatus.UpdateAvailable)
            {
                UpdateAvailable?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to check for updates: {ex.Message}");
        }
    }

    public async Task<(bool available, string version, string releaseNotes, string downloadUrl)> CheckForUpdateCustomAsync()
    {
        try
        {
            var updateInfo = await _sparkle.CheckForUpdatesQuietly();
            if (updateInfo.Status == NetSparkleUpdater.Enums.UpdateStatus.UpdateAvailable && updateInfo.Updates.Count > 0)
            {
                var latest = updateInfo.Updates[0];
                return (true, latest.Version, latest.ReleaseNotesLink, latest.DownloadLink);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed custom check: {ex.Message}");
        }
        return (false, "", "", "");
    }

    public async Task DownloadAndInstallUpdateAsync(string downloadUrl)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            var bytes = await client.GetByteArrayAsync(downloadUrl);
            var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StrideSetup.exe");
            await System.IO.File.WriteAllBytesAsync(tempFile, bytes);
            
            Process.Start(new ProcessStartInfo
            {
                FileName = tempFile,
                Arguments = "/SILENT",
                UseShellExecute = true
            });
            
            // Shut down the application gracefully to allow the installer to overwrite files
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.Application.Current.Shutdown();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to download/install: {ex.Message}");
        }
    }
}
