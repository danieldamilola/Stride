using System.Threading.Tasks;

namespace StrideBrowser.Services;

public class UpdateService
{
    public string? LatestVersion { get; private set; }
    public bool IsUpdateAvailable { get; private set; }
    
    public UpdateService()
    {
    }

    public async Task<bool> CheckForUpdatesAsync()
    {
        // Auto-update is disabled (Velopack removed, using Inno Setup).
        // A manual web check could be implemented here in the future.
        return await Task.FromResult(false);
    }

    public async Task DownloadAndInstallUpdateAsync()
    {
        // No-op
        await Task.CompletedTask;
    }
}
