namespace StrideBrowser.Services;

/// <summary>
/// Thread-safe transfer buffer for TCLens payloads.
/// MainWindow writes; TCLensMessageHandler reads.
/// </summary>
public sealed class TCLensTransferService
{
    public string PendingText { get; set; } = "";
    public string PendingUrl { get; set; } = "";
    public string PendingTitle { get; set; } = "";

    public void Clear()
    {
        PendingText = "";
        PendingUrl = "";
        PendingTitle = "";
    }
}
