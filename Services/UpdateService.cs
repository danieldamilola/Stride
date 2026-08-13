using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NetSparkleUpdater;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.WPF;

namespace StrideBrowser.Services;

public class UpdateService
{
    private SparkleUpdater _sparkle;
    private const string AppcastUrl = "https://raw.githubusercontent.com/danieldamilola/Stride/main/appcast.xml";

    public event EventHandler? UpdateAvailable;

    public UpdateService()
    {
        _sparkle = new SparkleUpdater(AppcastUrl, new Ed25519Checker(NetSparkleUpdater.Enums.SecurityMode.Unsafe))
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

    /// <summary>
    /// Triggers the actual NetSparkle UI window so the user can see release notes and install.
    /// </summary>
    public void ShowUpdateUI()
    {
        _sparkle.CheckForUpdatesAtUserRequest();
    }
}
