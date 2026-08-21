using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.WPF;

namespace StrideBrowser.Services;

/// <summary>
/// Drives the NetSparkle update pipeline end-to-end: appcast check, verified download, install.
/// The installer binary is Ed25519-verified against the appcast's edSignature before anything
/// executes - no raw HttpClient in this path. Failure visibility: every failure path logs to
/// the Trace listener (file-wired in App startup) and raises <see cref="UpdateFailed"/>.
/// </summary>
public sealed class UpdateService
{
    private const string AppcastUrl = "https://raw.githubusercontent.com/danieldamilola/Stride/main/appcast.xml";
    private const string PublicKeyBase64 = "Ka3hqHLB+ZNutVBleNvne9HFFkk4gvAqYiOV5c7VewQ=";

    private readonly SparkleUpdater _sparkle;

    /// <summary>Fires when a newer version is detected (drives the update badge).</summary>
    public event EventHandler? UpdateAvailable;

    /// <summary>Fires when any step of the update flow fails (check, download, signature, install).</summary>
    public event EventHandler<string>? UpdateFailed;

    /// <summary>Fires when the app must exit so the installer can overwrite files. Wired to shutdown by the view layer.</summary>
    public event Action? AppExitRequested;

    public UpdateService()
    {
        // Env overrides exist purely for end-to-end testing (local appcast + throwaway key).
        // They weaken nothing: the Ed25519 signature check still gates every downloaded binary.
        var appcastUrl = Environment.GetEnvironmentVariable("STRIDE_APPCAST_URL") ?? AppcastUrl;
        var publicKey = Environment.GetEnvironmentVariable("STRIDE_UPDATE_PUBLIC_KEY") ?? PublicKeyBase64;

        // OnlyVerifySoftwareDownloads: the appcast XML itself is not signed (it lives on a
        // HTTPS-protected channel; no sidecar file needed), but EVERY installer download must
        // carry a valid Ed25519 signature before it can be installed.
        _sparkle = new SparkleUpdater(appcastUrl, new Ed25519Checker(SecurityMode.OnlyVerifySoftwareDownloads, publicKey))
        {
            UIFactory = new UIFactory(),
            UseNotificationToast = false, // own red-dot notification
            UserInteractionMode = UserInteractionMode.DownloadNoInstall, // silent: no progress window, we drive install
            RelaunchAfterUpdate = false,
            CustomInstallerArguments = "/SILENT",
            LogWriter = new TraceLogWriter()
        };

        // UpdateDetected defaults to NextUpdateAction.ShowStandardUserInterface, which for quiet
        // checks makes NetSparkle AUTO-DOWNLOAD the installer (UpdatesHaveBeenDownloaded).
        // We drive downloads explicitly (DownloadAndInstallUpdateAsync), so prohibit that.
        _sparkle.UpdateDetected += (_, e) =>
        {
            e.NextAction = NextUpdateAction.ProhibitUpdate;
            UpdateAvailable?.Invoke(this, EventArgs.Empty);
        };

        // Only reached after the downloaded file passed Ed25519 verification.
        _sparkle.DownloadFinished += (item, path) => _ = InstallDownloadedAsync(item, path);

        _sparkle.DownloadedFileIsCorrupt += (_, _) => Fail("Downloaded update failed signature verification and was rejected.");
        _sparkle.DownloadedFileThrewWhileCheckingSignature += (_, _) => Fail("Downloaded update could not be signature-verified and was rejected.");
        _sparkle.DownloadHadError += (_, _, ex) => Fail($"Update download failed: {ex.Message}");
        _sparkle.InstallUpdateFailed += (reason, _) => { Fail($"Update install failed: {reason}"); return true; };

        // NetSparkle fires this before running the installer; the batch script then waits for
        // this process to exit before the installer actually runs.
        _sparkle.PreparingToExit += (_, _) => AppExitRequested?.Invoke();
    }

    private void Fail(string message)
    {
        Trace.WriteLine($"Update flow failed: {message}");
        UpdateFailed?.Invoke(this, message);
    }

    /// <summary>Forwards NetSparkle's internal log to the app's Trace listener (file-wired at startup).</summary>
    private sealed class TraceLogWriter : NetSparkleUpdater.Interfaces.ILogger
    {
        public void PrintMessage(string message) => Trace.WriteLine($"[sparkle] {message}");
        public void PrintMessage(string message, params object[]? arguments) =>
            Trace.WriteLine($"[sparkle] {string.Format(message, arguments ?? Array.Empty<object>())}");
    }

    /// <summary>Silently checks for updates in the background; fires <see cref="UpdateAvailable"/> if one is found.</summary>
    public async Task CheckForUpdatesQuietlyAsync()
    {
        var item = await CheckForUpdateCustomAsync();
        if (item is not null)
            UpdateAvailable?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns the latest update item (already filtered/verified against the appcast), or null.
    /// The item's <see cref="AppCastItem.DownloadSignature"/> is what gates the download later.
    /// </summary>
    public async Task<AppCastItem?> CheckForUpdateCustomAsync()
    {
        try
        {
            var updateInfo = await _sparkle.CheckForUpdatesQuietly();
            if (updateInfo.Status == UpdateStatus.UpdateAvailable && updateInfo.Updates.Count > 0)
                return updateInfo.Updates[0];
        }
        catch (Exception ex)
        {
            Fail($"Update check failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Downloads the latest update via NetSparkle's own downloader and Ed25519-verifies the
    /// binary before installing. On success the app is asked to exit (<see cref="AppExitRequested"/>)
    /// so the installer can overwrite files. Returns true once the verified download has started.
    /// </summary>
    public async Task<bool> DownloadAndInstallUpdateAsync()
    {
        var item = await CheckForUpdateCustomAsync();
        if (item is null)
        {
            Fail("Update install requested but no update is available.");
            return false;
        }

        try
        {
            await _sparkle.InitAndBeginDownload(item);
            return true;
        }
        catch (Exception ex)
        {
            Fail($"Update download could not start: {ex.Message}");
            return false;
        }
    }

    private async Task InstallDownloadedAsync(AppCastItem item, string path)
    {
        try
        {
            // InstallUpdate re-verifies the file's signature before executing anything.
            await _sparkle.InstallUpdate(item, path);
        }
        catch (Exception ex)
        {
            Fail($"Update install failed: {ex.Message}");
        }
    }
}