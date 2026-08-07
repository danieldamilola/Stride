using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace StrideBrowser.Services;

public class UpdateService
{
    private const string GitHubRepoUrl = "https://github.com/danieldamilola/Stride";

    public string? LatestVersion { get; private set; }
    public bool IsUpdateAvailable { get; private set; }
    
    private UpdateInfo? _updateInfo;

    public UpdateService()
    {
    }

    public async Task<bool> CheckForUpdatesAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(GitHubRepoUrl, null, false));
            
            if (!mgr.IsInstalled)
            {
                // App is running locally, not installed via Velopack
                return false;
            }

            _updateInfo = await mgr.CheckForUpdatesAsync();
            if (_updateInfo != null)
            {
                LatestVersion = _updateInfo.TargetFullRelease.Version.ToString();
                IsUpdateAvailable = true;
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to check for updates: {ex.Message}");
        }

        return false;
    }

    public async Task DownloadAndInstallUpdateAsync()
    {
        if (!IsUpdateAvailable || _updateInfo == null)
            return;

        try
        {
            var mgr = new UpdateManager(new GithubSource(GitHubRepoUrl, null, false));
            
            // Silently download in background
            await mgr.DownloadUpdatesAsync(_updateInfo);
            
            // Apply updates and restart app
            mgr.ApplyUpdatesAndRestart(_updateInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to download or install update: {ex.Message}");
        }
    }
}
