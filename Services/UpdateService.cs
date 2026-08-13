using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NetSparkleUpdater;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.WPF;

namespace StrideBrowser.Services;

public class UpdateService
{
    private SparkleUpdater _sparkle;
    private const string AppcastUrl = "https://raw.githubusercontent.com/danieldamilola/Stride/main/appcast.xml";

    public UpdateService()
    {
        // For development, we skip signature verification (assuming HTTPS is secure enough for this project).
        // For production, you can configure DSAChecker or Ed25519Checker.
        _sparkle = new SparkleUpdater(AppcastUrl, new Ed25519Checker(Enums.SecurityMode.Unsafe))
        {
            UIFactory = new UIFactory(),
            ShowsUIOnMainThread = true,
            UseNotificationToast = true
        };
    }

    public void StartUpdateLoop()
    {
        _sparkle.StartLoop(true, true);
    }

    public async Task<bool> CheckForUpdatesAsync()
    {
        try
        {
            var updateInfo = await _sparkle.CheckForUpdatesQuietly();
            return updateInfo.Status == NetSparkleUpdater.Enums.UpdateStatus.UpdateAvailable;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to check for updates via NetSparkle: {ex.Message}");
            return false;
        }
    }
}
